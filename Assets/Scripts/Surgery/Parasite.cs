using System.Collections.Generic;
using Probation.Game;
using Probation.Interaction;
using Probation.Player;
using Unity.Netcode;
using UnityEngine;

namespace Probation.Surgery
{
    public enum ParasiteState
    {
        /// <summary>Parked under the map, waiting to be somebody's mistake.</summary>
        Pooled,

        /// <summary>Loose on the ship and looking for something that is holding still.</summary>
        Loose,

        /// <summary>Under. An object, until it wakes up.</summary>
        Sedated,
    }

    /// <summary>
    /// The thing you did not get out of somebody this afternoon.
    ///
    /// The rule the whole system hangs on: <b>a parasite is never spawned by the game.</b> Every
    /// one loose on the ship got there because a brood was left in a patient too long, or a brood
    /// patient died, or somebody pulled one out and put it down. Every night is the bill for that
    /// day, and there is nobody else to blame for it.
    ///
    /// It is not lethal, and that is the design rather than a shortcut. This is friendly horror:
    /// the threat is to your work and your dignity. It knocks a player down, which costs a whole
    /// second player to come and pick them up; it infests a patient, which turns a ten-second job
    /// into a twenty-second one you now have to redo. Killing interns would make people careful,
    /// and careful is the opposite of what this ward is for.
    ///
    /// There are no weapons. You put it under with the gas you were going to use on a patient,
    /// you pick it up, and you carry it the length of the ship to the airlock. Everything it
    /// costs you is something somebody else needed.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(Grabbable))]
    public class Parasite : NetworkBehaviour
    {
        [Header("Hunting")]
        [Tooltip("Slow on purpose. You should always be able to walk away from one - the cost is that walking away means abandoning whatever you were doing.")]
        [SerializeField] private float speed = 1.15f;
        [Tooltip("How near it has to get to do something about you.")]
        [SerializeField] private float reach = 0.9f;
        [Tooltip("Seconds it sits still after knocking somebody down, before it wants something else.")]
        [SerializeField] private float sated = 6f;

        [Header("Sedation")]
        [Tooltip("How near a gas rig has to be to put one under.")]
        [SerializeField] private float gasRange = 1.4f;
        [Tooltip("How long it stays under. Long enough to carry it to the airlock, not long enough to forget about it.")]
        [SerializeField] private float sedationSeconds = 22f;

        [Header("What it does")]
        [SerializeField] private float knockdownSeconds = 3.5f;
        [Tooltip("Harm done to a patient it reaches, on top of infesting them.")]
        [Range(0f, 1f)] [SerializeField] private float infestHarm = 0.12f;

        private readonly NetworkVariable<int> _state = new((int)ParasiteState.Pooled);

        public ParasiteState State => (ParasiteState)_state.Value;
        public bool IsLoose => State == ParasiteState.Loose;
        public bool IsSedated => State == ParasiteState.Sedated;

        /// <summary>Everything that exists, loose or not. Pooled like patients and gurneys.</summary>
        public static readonly List<Parasite> All = new();

        /// <summary>Anything still on the ship when the shift ends is a thing that got found.</summary>
        public static int LooseCount
        {
            get
            {
                int n = 0;
                foreach (var p in All) if (p != null && p.State != ParasiteState.Pooled) n++;
                return n;
            }
        }

        private static readonly Vector3 Nowhere = new(0f, -40f, 0f);

        private Grabbable _grabbable;
        private Collider _collider;
        private Rigidbody _body;

        private ShipNode _heading;
        private float _idleUntil;
        private float _wakesAt;

        /// <summary>Whose mistake this was. Read out at the end of the night.</summary>
        public ulong Blame { get; private set; } = ulong.MaxValue;

        private void Awake()
        {
            _grabbable = GetComponent<Grabbable>();
            _collider = GetComponent<Collider>();
            _body = GetComponent<Rigidbody>();

            BuildVoice();
        }

        /// <summary>
        /// You hear one before you see one, and that is the whole horror budget.
        ///
        /// Synthesised rather than authored, the same way ScalpelTool makes its drag and
        /// VitalsMonitor makes its beeps - a wet irregular skitter is broadband noise pushed
        /// through a low pass, and nothing about it needs an artist yet.
        ///
        /// Spatial, and audible from further away than it is visible in a dim ward. The point is
        /// that somebody says "can anyone else hear that" a good few seconds before anybody can
        /// point at it.
        /// </summary>
        private void BuildVoice()
        {
            _voice = GetComponent<AudioSource>();
            if (_voice == null) _voice = gameObject.AddComponent<AudioSource>();

            _voice.clip = Skitter();
            _voice.loop = true;
            _voice.playOnAwake = false;
            _voice.spatialBlend = 1f;
            _voice.volume = 0f;
            _voice.minDistance = 2f;
            _voice.maxDistance = 22f;
            _voice.Play();
        }

        private static AudioClip Skitter()
        {
            const int rate = 44100;
            var samples = new float[rate * 3 / 2];

            var random = new System.Random(77);
            float previous = 0f;
            float envelope = 0f;
            float nextTick = 0f;

            for (int i = 0; i < samples.Length; i++)
            {
                float white = (float)(random.NextDouble() * 2.0 - 1.0);
                previous = Mathf.Lerp(previous, white, 0.14f);

                // Irregular little scrapes rather than a drone. A steady tone reads as machinery;
                // something uneven reads as alive, which is the only difference that matters.
                if (i >= nextTick)
                {
                    envelope = 1f;
                    nextTick = i + rate * (0.06f + (float)random.NextDouble() * 0.22f);
                }

                envelope *= 0.9993f;
                samples[i] = previous * envelope;
            }

            // Crossfade the tail into the head so the loop has no click in it.
            int blend = rate / 20;
            for (int i = 0; i < blend; i++)
            {
                float t = i / (float)blend;
                samples[i] = Mathf.Lerp(samples[samples.Length - blend + i], samples[i], t);
            }

            float peak = 0f;
            foreach (float s in samples) peak = Mathf.Max(peak, Mathf.Abs(s));
            if (peak > 0.0001f)
                for (int i = 0; i < samples.Length; i++) samples[i] /= peak;

            var clip = AudioClip.Create("ParasiteSkitter", samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private AudioSource _voice;

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            _state.OnValueChanged += (_, __) => Refresh();
            Refresh();

            if (IsServer) Park();
        }

        public override void OnNetworkDespawn() => All.Remove(this);

        // ---------------------------------------------------------------- becoming a problem

        /// <summary>
        /// Let one out, at a position, blamed on somebody.
        ///
        /// Takes the first pooled one rather than spawning: nothing in this project instantiates
        /// a network prefab at runtime, and a parasite you park under the map is the same thing
        /// with less machinery.
        /// </summary>
        public static Parasite Release(Vector3 at, ulong blame)
        {
            foreach (var parasite in All)
            {
                if (parasite == null || parasite.State != ParasiteState.Pooled) continue;
                if (!parasite.IsServer) continue;

                parasite.Blame = blame;
                parasite.Loosen(at);
                return parasite;
            }

            return null;
        }

        /// <summary>
        /// The payoff of a brood extraction: it comes out already under, in front of you.
        ///
        /// Which is not the end of it. You are now holding the problem, the airlock is at the far
        /// end of the ship, and it wakes up. Getting one out of a patient correctly is the point
        /// at which it stops being surgery and starts being tidying up after yourself.
        /// </summary>
        public static Parasite Extract(Vector3 at, ulong blame)
        {
            var parasite = Release(at, blame);
            parasite?.Sedate();
            return parasite;
        }

        private void Loosen(Vector3 at)
        {
            transform.position = at + Vector3.up * 0.2f;
            _heading = null;
            _idleUntil = 0f;
            _state.Value = (int)ParasiteState.Loose;

            ShiftDirector.Instance?.Announce("Something just came out of one of them.");
        }

        /// <summary>Put it under. Called by the gas rig, and by anything else that should stop it.</summary>
        public void Sedate()
        {
            if (!IsServer || State != ParasiteState.Loose) return;

            _wakesAt = Time.time + sedationSeconds;
            _state.Value = (int)ParasiteState.Sedated;
            ShiftDirector.Instance?.Announce("It has gone limp. Move it, now.");
        }

        /// <summary>Out the airlock. The only way anything gets off this ship.</summary>
        public void Incinerate()
        {
            if (!IsServer || State == ParasiteState.Pooled) return;

            Park();
            ShiftDirector.Instance?.Announce("It is off the ship.");
        }

        private void Park()
        {
            _state.Value = (int)ParasiteState.Pooled;
            _heading = null;

            if (_body != null)
            {
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
            }

            transform.position = Nowhere;
        }

        /// <summary>
        /// Presentation follows state on every machine, not just the host.
        ///
        /// A loose one has no collider at all: it walks a node graph rather than physics, it must
        /// not shove patients or trolleys around on its way past, and - the part that matters -
        /// PlayerInteractor casts for something to grab, so a collider here would let somebody
        /// pick up a wide-awake parasite with their bare hands.
        /// </summary>
        private void Refresh()
        {
            bool pooled = State == ParasiteState.Pooled;
            bool sedated = State == ParasiteState.Sedated;

            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
                renderer.enabled = !pooled;

            if (_collider != null) _collider.enabled = sedated;
            if (_grabbable != null) _grabbable.enabled = sedated;
            if (_body != null) _body.isKinematic = !sedated;

            // Loud while awake, silent while under, gone while pooled. Everyone hears this, not
            // just the host - it is the only warning anybody gets.
            if (_voice != null) _voice.volume = State == ParasiteState.Loose ? 0.55f : 0f;
        }

        // ---------------------------------------------------------------- hunting

        private void Update()
        {
            if (!IsServer) return;

            switch (State)
            {
                case ParasiteState.Sedated:
                    // Carrying it is a race. Leave it on a bench and it is your problem again.
                    if (Time.time >= _wakesAt && !_grabbable.IsHeld)
                    {
                        _state.Value = (int)ParasiteState.Loose;
                        ShiftDirector.Instance?.Announce("It is moving again.");
                    }
                    return;

                case ParasiteState.Loose:
                    Hunt();
                    return;
            }
        }

        private void Hunt()
        {
            CheckForGas();
            if (State != ParasiteState.Loose) return;

            if (Time.time < _idleUntil) return;

            Transform quarry = Quarry();
            if (quarry == null) return;

            if ((quarry.position - transform.position).sqrMagnitude < reach * reach)
            {
                Reach(quarry);
                return;
            }

            Walk(quarry.position);
        }

        /// <summary>
        /// Step along the node graph towards whatever it wants.
        ///
        /// It only ever walks to the NEXT node, and re-asks on arrival - so a target that moves is
        /// followed with no replanning, and the graph's loops get used because that is genuinely
        /// the shortest way round.
        /// </summary>
        private void Walk(Vector3 towards)
        {
            ShipNode.EnsureLinked();

            var here = ShipNode.Nearest(transform.position);
            var there = ShipNode.Nearest(towards);

            if (_heading == null || (_heading.transform.position - transform.position).sqrMagnitude < 0.5f)
                _heading = ShipNode.StepFrom(here, there);

            // Last node before the target, or no graph at all: go straight at them.
            Vector3 goal = _heading != null && _heading != there
                ? _heading.transform.position
                : towards;

            goal.y = transform.position.y;
            transform.position = Vector3.MoveTowards(transform.position, goal, speed * Time.deltaTime);

            Vector3 facing = goal - transform.position;
            if (facing.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(new Vector3(facing.x, 0f, facing.z));
        }

        /// <summary>
        /// What it goes for: whatever is nearest and least able to leave.
        ///
        /// Patients count double because they cannot run, and a braced surgeon is the most
        /// attractive thing on the ship - somebody who has suspended their own view to work is
        /// exactly who this should be creeping up on.
        /// </summary>
        private Transform Quarry()
        {
            Transform best = null;
            float bestScore = float.PositiveInfinity;

            foreach (var patient in Patient.All)
            {
                if (patient == null || patient.HasLeft || patient.IsDead) continue;

                float score = (patient.transform.position - transform.position).sqrMagnitude;
                if (score >= bestScore) continue;

                bestScore = score;
                best = patient.transform;
            }

            foreach (var player in Object.FindObjectsByType<PlayerLocomotion>(FindObjectsSortMode.None))
            {
                if (player == null || player.IsDowned) continue;

                float score = (player.transform.position - transform.position).sqrMagnitude;

                // Somebody braced cannot see it coming, so it prefers them.
                var brace = player.GetComponent<PlayerBrace>();
                if (brace != null && brace.IsBraced) score *= 0.35f;

                if (score >= bestScore) continue;

                bestScore = score;
                best = player.transform;
            }

            return best;
        }

        private void Reach(Transform quarry)
        {
            _idleUntil = Time.time + sated;
            _heading = null;

            var patient = quarry.GetComponentInParent<Patient>();
            if (patient != null)
            {
                Infest(patient);
                return;
            }

            var locomotion = quarry.GetComponent<PlayerLocomotion>();
            if (locomotion == null) return;

            Vector3 shove = (quarry.position - transform.position).normalized * 3f + Vector3.up * 2f;
            locomotion.Knockdown(knockdownSeconds, shove);

            ShiftDirector.Instance?.Announce("It has got somebody.");
            IncidentLog.Record(Blame, "let one get loose on the ward");
        }

        /// <summary>
        /// Into a patient. The worst thing it can do, and the reason you cannot ignore one.
        ///
        /// Deliberately does not rewrite their condition - that would be the host silently
        /// changing what the chart should say, after somebody has already read them and written
        /// it. Instead it hurts them and reopens the bleeding, so the team find out the way they
        /// find out about everything else: the rate climbing and the flesh going yellow.
        /// </summary>
        private void Infest(Patient patient)
        {
            patient.ApplyHarm(infestHarm, Blame, "let one get into a patient");
            patient.StartBleeding(0.02f);
            patient.AddFragility(0.3f, Blame, null);

            ShiftDirector.Instance?.Announce("It has got into one of them.");
        }

        /// <summary>
        /// A gas rig held near it puts it under. No weapon, no new verb - the same object you
        /// sedate patients with, which is the whole point: every second spent on this is a second
        /// somebody on a table is still awake.
        /// </summary>
        private void CheckForGas()
        {
            var gas = Grabbable.HeldNear(transform.position, gasRange, "gas rig");
            if (gas == null) return;

            Blame = gas.HeldBy;
            Sedate();
        }
    }
}
