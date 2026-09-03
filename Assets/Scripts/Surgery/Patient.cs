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
        [SerializeField] private Species species;
        [Tooltip("Seconds of bleeding this patient has already taken before the shift starts.")]
        [SerializeField] private float startingHarm;

        private readonly NetworkVariable<PatientState> _state = new(PatientState.Stable);
        private readonly NetworkVariable<float> _harm = new();          // 0 = untouched, 1 = dead
        private readonly NetworkVariable<float> _heartRate = new(70f);
        private readonly NetworkVariable<bool> _conscious = new();

        public PatientState State => _state.Value;
        public float Harm => _harm.Value;
        public float HeartRate => _heartRate.Value;
        public bool IsDead => _state.Value == PatientState.Dead;
        public Species Species => species;

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
            _heartRate.Value = species != null ? species.restingHeartRate : 70f;

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
                ApplyHarmInternal(_bleedRate * Time.deltaTime, ulong.MaxValue, null);

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
        /// The patient is out of danger. Called when a procedure completes - without this,
        /// finishing an operation changes a bool and nothing else, and there is no such thing
        /// as saving anyone.
        /// </summary>
        public void Stabilise()
        {
            if (!IsServer || IsDead) return;

            _bleedRate = 0f;
            _harm.Value = Mathf.Max(0f, _harm.Value - 0.35f);
            _conscious.Value = false;
            SetState(PatientState.Stable);
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
        public void Admit()
        {
            if (!IsServer) return;

            HasLeft = false;
            _bleedRate = 0f;
            _harm.Value = Mathf.Clamp01(startingHarm);
            _heartRate.Value = species != null ? species.restingHeartRate : 70f;
            _conscious.Value = true;
            SetState(PatientState.Stable);
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
                var operation = GetComponent<Operation>();
                return !IsDead && operation != null && operation.Finished;
            }
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
        public bool ObjectsToMetal => species != null && species.allergicToMetal;

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
            float resting = species != null ? species.restingHeartRate : 70f;
            float critical = species != null ? species.criticalHeartRate : 190f;

            // Climbs with harm. This is the room's shared clock and the signature sound of the
            // game - a rate rising while three people argue about which organ is which.
            float target = Mathf.Lerp(resting, critical, _harm.Value);
            _heartRate.Value = Mathf.MoveTowards(_heartRate.Value, target, 25f * Time.deltaTime);
        }
    }
}
