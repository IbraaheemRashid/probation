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
        /// True pain and consciousness state. Replicated to everyone but only *shown* to the
        /// anaesthetist - see PlayerRole. Hiding it properly would need targeted RPCs, which is
        /// not worth it against four friends who could just look at each other's screens.
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

            if (!IsServer) return;
            _harm.Value = Mathf.Clamp01(startingHarm);
            _heartRate.Value = species != null ? species.restingHeartRate : 70f;
        }

        public override void OnNetworkDespawn() => All.Remove(this);

        private void Update()
        {
            if (!IsServer || IsDead) return;

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
            if (IsServer && !IsDead) _conscious.Value = conscious;
        }

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
