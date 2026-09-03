using Probation.Game;
using Unity.Netcode;
using UnityEngine;

namespace Probation.Surgery
{
    public enum PatientState
    {
        Stable,
        Bleeding,
        Critical,
        Dead,
    }

    /// <summary>
    /// Something on the table that can be hurt, and that the whole room can hear.
    ///
    /// Simulated by the host and nobody else. Everyone's shift score depends on this state, so
    /// it can never be client-reported.
    ///
    /// Death is not a fail state. It is a logged incident, and the body stays in the world as a
    /// physical problem that somebody has to move.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class Patient : NetworkBehaviour
    {
        [Tooltip("Fallback only. Intake assigns the real species from the casebook at admission.")]
        [SerializeField] private Species species;
        [Tooltip("Seconds of bleeding this patient has already taken before the shift starts.")]
        [SerializeField] private float startingHarm;

        private readonly NetworkVariable<PatientState> _state = new(PatientState.Stable);
        private readonly NetworkVariable<float> _harm = new();          // 0 = untouched, 1 = dead
        private readonly NetworkVariable<float> _heartRate = new(70f);
        private readonly NetworkVariable<bool> _conscious = new();

        // Who this one is tonight. Indices rather than references because a NetworkVariable
        // cannot carry a ScriptableObject - see Casebook for what that costs us.
        private readonly NetworkVariable<int> _speciesIndex = new(-1);
        private readonly NetworkVariable<int> _conditionIndex = new(-1);

        public PatientState State => _state.Value;
        public float Harm => _harm.Value;
        public float HeartRate => _heartRate.Value;
        public bool IsDead => _state.Value == PatientState.Dead;

        /// <summary>
        /// What they are. Resolved through the casebook, falling back to the serialized field so
        /// a patient dropped into a scene by hand still behaves rather than null-referencing.
        /// </summary>
        public Species Species => Casebook.Active?.SpeciesAt(_speciesIndex.Value) ?? species;

        /// <summary>What is wrong with them. Null until intake admits them with a case.</summary>
        public Condition Condition => Casebook.Active?.ConditionAt(_conditionIndex.Value);

        /// <summary>
        /// How ill they look before anybody has touched them.
        ///
        /// Deliberately separate from <see cref="Harm"/>: harm is the score, and a patient who
        /// arrived looking dreadful has not been hurt by anyone yet.
        /// </summary>
        public float PresentingSickness => Condition != null ? Condition.presentingSickness : 0f;

        /// <summary>
        /// True pain and consciousness state. Readable only through the scanner, which is one
        /// object that one person has to be holding.
        /// </summary>
        public bool IsConscious => _conscious.Value;

        public event System.Action<PatientState> StateChanged;

        private float _bleedRate;

        /// <summary>Everyone currently on a table. Cheaper than searching the scene per frame.</summary>
        public static readonly System.Collections.Generic.List<Patient> All = new();

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            _state.OnValueChanged += (_, next) => StateChanged?.Invoke(next);

            // Everybody starts off the ward. Intake decides who comes in and when.
            if (IsServer) transform.position = HoldingPosition;

            if (!IsServer) return;
            _harm.Value = Mathf.Clamp01(startingHarm);
            _heartRate.Value = Species != null ? Species.restingHeartRate : 70f;

            // Patients arrive awake. Somebody has to put them under, and if nobody does then
            // every step of the operation is performed on a conscious alien.
            _conscious.Value = true;
        }

        public override void OnNetworkDespawn() => All.Remove(this);

        [Header("Handling")]
        [Tooltip("Impacts slower than this are free. Above it, carelessness costs the patient.")]
        [SerializeField] private float safeImpactSpeed = 3.5f;
        [Tooltip("Harm per m/s of impact over the safe speed.")]
        [SerializeField] private float harmPerImpactSpeed = 0.04f;

        /// <summary>
        /// The single thing that makes hauling a patient a skill rather than a walk.
        ///
        /// R.E.P.O.'s valuables lose money when you bump them, and that one rule is what turns
        /// carrying a piano into gameplay. Ours lose blood. Without it you can ram a bleeding
        /// alien through three doorframes at a sprint and the game does not care.
        /// </summary>
        private void OnCollisionEnter(Collision collision)
        {
            if (!IsServer || IsDead || HasLeft) return;

            float speed = collision.relativeVelocity.magnitude;
            if (speed <= safeImpactSpeed) return;

            var grabbable = GetComponent<Interaction.Grabbable>();
            ulong blame = grabbable != null ? grabbable.LastHandledBy : ulong.MaxValue;

            ApplyHarmInternal((speed - safeImpactSpeed) * harmPerImpactSpeed, blame,
                              blame == ulong.MaxValue ? null : "slammed a patient into something");

            if (speed > safeImpactSpeed * 2f)
                ShiftDirector.Instance?.Announce("Handle them gently.");
        }

        private void Update()
        {
            if (!IsServer || IsDead || HasLeft) return;

            if (_bleedRate > 0f)
            {
                // bleedOutSeconds was authored on day one and never read by anything. It earns
                // its place here: a species that empties in twenty seconds makes an opened bleed
                // a genuine emergency, one at ninety makes the same bleed a nuisance - and that
                // changes which procedures are safe to run on whom, for one line.
                float scale = Species != null && Species.bleedOutSeconds > 0.01f
                    ? BaselineBleedOutSeconds / Species.bleedOutSeconds
                    : 1f;

                ApplyHarmInternal(_bleedRate * scale * Time.deltaTime, ulong.MaxValue, null);
            }

            // Whatever they walked in with gets worse while nobody is dealing with it. Leaving
            // somebody in a corridor has to be a decision with a price, or triage is just a
            // queue and the order you work in never matters.
            //
            // Keyed on whether the condition was actually resolved rather than on whether an
            // operation finished, which is what gives a misdiagnosis teeth: the ward closes them
            // up, believes it is done, and the untreated thing goes on quietly killing them all
            // the way to the discharge door.
            var condition = Condition;
            if (condition != null && condition.untreatedHarmPerSecond > 0f && !ConditionResolved)
                ApplyHarmInternal(condition.untreatedHarmPerSecond * Time.deltaTime, ulong.MaxValue, null);

            UpdateVitals();
        }

        // ---------------------------------------------------------------- harm

        /// <summary>
        /// Hurt the patient, and put a name on it. Every caller passes the intern responsible -
        /// that is what the review screen reads out at the end of the shift.
        /// </summary>
        public void ApplyHarm(float amount, ulong byClientId, string reason)
        {
            if (!IsServer) return;
            ApplyHarmInternal(amount, byClientId, reason);
        }

        private void ApplyHarmInternal(float amount, ulong byClientId, string reason)
        {
            if (IsDead || amount <= 0f) return;

            _harm.Value = Mathf.Clamp01(_harm.Value + amount);

            if (reason != null && byClientId != ulong.MaxValue)
                IncidentLog.Record(byClientId, reason);

            if (_harm.Value >= 1f) Die(byClientId);
            else if (_harm.Value > 0.75f) SetState(PatientState.Critical);
            else if (_bleedRate > 0f) SetState(PatientState.Bleeding);
        }

        /// <summary>Open a bleed. Only Vascular can close one permanently.</summary>
        public void StartBleeding(float ratePerSecond)
        {
            if (!IsServer || IsDead) return;
            bool wasDry = _bleedRate <= 0f;
            _bleedRate += Mathf.Max(0f, ratePerSecond);
            SetState(PatientState.Bleeding);

            if (wasDry) ShiftDirector.Instance?.Announce("The patient is bleeding.");
        }

        public void StopBleeding()
        {
            if (!IsServer) return;
            _bleedRate = 0f;
            if (!IsDead && _state.Value == PatientState.Bleeding)
                SetState(_harm.Value > 0.75f ? PatientState.Critical : PatientState.Stable);
        }

        /// <summary>
        /// Take some harm back off them. The only thing in the game that moves harm downwards.
        ///
        /// This used to be half of a Stabilise() that also stopped the bleeding and put them
        /// under, which meant finishing any procedure at all fixed everything about a patient.
        /// Broken into verbs so Operation can compose them differently depending on whether the
        /// ward actually treated what was wrong - Patient has no business knowing what a
        /// procedure is, let alone whether it was the right one.
        /// </summary>
        public void Heal(float amount)
        {
            if (!IsServer || IsDead || amount <= 0f) return;

            _harm.Value = Mathf.Max(0f, _harm.Value - amount);

            if (_bleedRate <= 0f && _harm.Value <= 0.75f) SetState(PatientState.Stable);
        }

        public void SetConscious(bool conscious)
        {
            if (!IsServer || IsDead || _conscious.Value == conscious) return;

            _conscious.Value = conscious;
            ShiftDirector.Instance?.Announce(conscious ? "The patient is awake." : "The patient is under.");
        }

        /// <summary>
        /// Wheel a fresh one in. Beds are reused across the night - the alternative is spawning
        /// network prefabs, and a bed you reset is the same thing with less machinery.
        /// </summary>
        public void Admit(Species assigned, Condition condition)
        {
            if (!IsServer) return;

            var book = Casebook.Active;

            // Set before anything reads Species or Condition below - the accessors resolve
            // through these indices, so ordering here is not cosmetic.
            _speciesIndex.Value = book != null && assigned != null ? book.IndexOf(assigned) : -1;
            _conditionIndex.Value = book != null && condition != null ? book.IndexOf(condition) : -1;

            HasLeft = false;
            _bleedRate = condition != null ? Mathf.Max(0f, condition.arrivesBleedingRate) : 0f;
            _harm.Value = Mathf.Clamp01(condition != null ? condition.arrivesHarmed : startingHarm);
            _heartRate.Value = Species != null ? Species.restingHeartRate : 70f;
            _conscious.Value = condition == null || !condition.arrivesUnconscious;

            ConditionResolved = false;

            // Or the new arrival inherits the last occupant's diagnosis, which is the worst
            // possible version of this bug: the chart reads plausibly and is about someone else.
            _chart?.Clear();

            SetState(_bleedRate > 0f ? PatientState.Bleeding : PatientState.Stable);
        }

        /// <summary>The trolley this one is on, if any. Set by Gurney.</summary>
        public Probation.Game.Gurney Ride { get; set; }

        /// <summary>Off the ward entirely - discharged or in the morgue. Waiting to be re-used.</summary>
        public bool HasLeft { get; private set; } = true;

        /// <summary>Their procedure is done and they are alive. The only thing the quota counts.</summary>
        public bool IsTreated
        {
            get
            {
                if (IsDead) return false;
                if (_operation != null && _operation.Finished) return true;

                // Deciding not to operate is a treatment, and it has to be, or a patient whose
                // correct answer is "leave them alone" can never satisfy the discharge door and
                // their bed is gone for the night. Whether the decision was right is settled at
                // the door rather than here - this game does not block, it charges.
                return _chart != null && _chart.SaysNoOperation;
            }
        }

        /// <summary>What the ward decided to do about them, if anybody has decided yet.</summary>
        public PatientChart Chart => _chart;

        /// <summary>
        /// Whether the thing that was actually wrong with them has been dealt with.
        ///
        /// Pointedly not the same question as <see cref="IsTreated"/>. That one asks whether the
        /// ward is finished with this patient, which is what the discharge door needs to know.
        /// This asks whether they are any better, which is what their body needs to know. A
        /// wrongly diagnosed patient is finished with and not better, and the gap between those
        /// two is where this entire game lives.
        ///
        /// Host-only, and it must stay that way: replicating it would tell every client whether
        /// their diagnosis was right, and there would be nothing left to work out.
        /// </summary>
        public bool ConditionResolved { get; private set; }

        /// <summary>Called by Operation, and only when the right procedure has finished.</summary>
        public void ResolveCondition()
        {
            if (IsServer) ConditionResolved = true;
        }

        /// <summary>Cached because the untreated-harm tick asks every frame, on every patient.</summary>
        private Operation _operation;
        private PatientChart _chart;

        /// <summary>The rate bleedOutSeconds is expressed against. A species at 45 bleeds as authored.</summary>
        private const float BaselineBleedOutSeconds = 45f;

        private void Awake()
        {
            _operation = GetComponent<Operation>();
            _chart = GetComponentInChildren<PatientChart>(true);
        }

        /// <summary>
        /// Leave the ward. The body is parked out of the way and its bed freed, ready to be
        /// wheeled back in as somebody else - beds are reused rather than spawned.
        /// </summary>
        public void SendAway()
        {
            if (!IsServer || HasLeft) return;

            HasLeft = true;
            Ride?.Unload();

            var body = GetComponent<Rigidbody>();
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            transform.position = HoldingPosition;
        }

        /// <summary>Somewhere out of the way. Patients wait here between admissions.</summary>
        private static readonly Vector3 HoldingPosition = new(0f, -40f, 0f);

        /// <summary>Species rule that mutates every procedure without any new procedure code.</summary>
        public bool ObjectsToMetal => Species != null && Species.allergicToMetal;

        private void Die(ulong byClientId)
        {
            _bleedRate = 0f;
            _conscious.Value = false;
            _heartRate.Value = 0f;
            SetState(PatientState.Dead);

            if (byClientId != ulong.MaxValue)
                IncidentLog.Record(byClientId, "lost a patient");

            // The run-ending condition is the hospital's body count, not any one intern's.
            ShiftDirector.Instance?.RecordDeath();
            ShiftDirector.Instance?.Announce("You have lost the patient.");
        }

        private void SetState(PatientState next)
        {
            if (_state.Value != next) _state.Value = next;
        }

        // ---------------------------------------------------------------- vitals

        private void UpdateVitals()
        {
            // What is wrong with them shifts the baseline, which is how one number comes to mean
            // two things. 110 bpm is alarming on a Thoracid that rests at 68 and thoroughly dull
            // on a Vithrid that rests at 112 - same reading, opposite conclusion, and the only
            // way to tell is to know which one you are looking at.
            float resting = (Species != null ? Species.restingHeartRate : 70f)
                            + (Condition != null ? Condition.restingRateOffset : 0f);
            float critical = Species != null ? Species.criticalHeartRate : 190f;

            // Climbs with harm. This is the room's shared clock and the signature sound of the
            // game - a rate rising while three people argue about which organ is which.
            float target = Mathf.Lerp(resting, critical, _harm.Value);
            _heartRate.Value = Mathf.MoveTowards(_heartRate.Value, target, 25f * Time.deltaTime);
        }
    }
}
