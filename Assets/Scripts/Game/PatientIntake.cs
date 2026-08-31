using Probation.Surgery;
using Unity.Netcode;
using UnityEngine;

namespace Probation.Game
{
    /// <summary>
    /// Patients arrive across the night on a rising curve, so the ward gets busier while the
    /// interns get more tired and more of the equipment is already covered in something.
    ///
    /// This is Overcooked's task overload: the target is that a competent team is only just
    /// coping. If everybody has time to stand and watch one operation, the intake is too slow.
    ///
    /// Beds and bodies are pooled, not spawned - a patient that leaves the ward is parked and
    /// wheeled back in later as somebody else.
    /// </summary>
    public class PatientIntake : NetworkBehaviour
    {
        [SerializeField] private float firstArrivalAfter = 6f;
        [Tooltip("Gap between arrivals at the start of the night.")]
        [SerializeField] private float slowestGap = 26f;
        [Tooltip("Gap between arrivals by the end of it.")]
        [SerializeField] private float fastestGap = 9f;
        [Tooltip("Never fill the ward completely - leave somewhere to put the next one.")]
        [SerializeField] private int leaveBedsFree = 1;

        private float _nextArrival;

        private void Update()
        {
            if (!IsServer) return;

            var director = ShiftDirector.Instance;
            if (director == null || director.Phase != ShiftPhase.Shift)
            {
                _nextArrival = Time.time + firstArrivalAfter;
                return;
            }

            SweepFinishedBeds();

            if (Time.time < _nextArrival || !TryAdmit()) return;

            float gap = Mathf.Lerp(slowestGap, fastestGap, director.PhaseProgress);
            _nextArrival = Time.time + gap;
        }

        /// <summary>
        /// A patient who is done with - treated or dead - still occupies their bed until
        /// somebody physically moves them. That is deliberate: the ward silting up with bodies
        /// nobody has wheeled out is the pressure.
        /// </summary>
        private void SweepFinishedBeds()
        {
            foreach (var bed in WardBed.All)
                if (bed.Occupant != null && bed.Occupant.HasLeft) bed.Clear();
        }

        private bool TryAdmit()
        {
            int free = 0;
            WardBed target = null;

            foreach (var bed in WardBed.All)
            {
                if (!bed.IsFree) continue;
                free++;
                if (target == null) target = bed;
            }

            if (target == null || free <= leaveBedsFree - 1) return false;

            Patient waiting = null;
            foreach (var patient in Patient.All)
            {
                if (patient == null || !patient.HasLeft) continue;
                waiting = patient;
                break;
            }

            if (waiting == null) return false;

            var body = waiting.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            waiting.transform.SetPositionAndRotation(target.Surface, target.transform.rotation);

            waiting.Admit();
            waiting.GetComponent<Operation>()?.Restart();
            target.Occupy(waiting);

            ShiftDirector.Instance?.Announce("A patient is on the table.");
            return true;
        }
    }
}
