using Probation.Surgery;
using Unity.Netcode;
using UnityEngine;

namespace Probation.Game
{
    /// <summary>
    /// Patients arrive through the night on a rising curve, so the ward gets busier while the
    /// interns get more tired and more of the equipment is already covered in something.
    ///
    /// This is Overcooked's task overload: the target is that a competent team is only just
    /// coping. If everybody has time to stand and watch one operation, intake is too slow.
    ///
    /// Nothing is spawned. Bodies and trolleys are both pooled - a patient who leaves the ward
    /// is parked out of sight and wheeled back in later as somebody else.
    /// </summary>
    public class PatientIntake : NetworkBehaviour
    {
        [Tooltip("Patients already on trolleys when the doors open, so the ward is never empty.")]
        [SerializeField] private int startingPatients = 3;
        [SerializeField] private float firstArrivalAfter = 12f;
        [Tooltip("Gap between arrivals at the start of the night.")]
        [SerializeField] private float slowestGap = 26f;
        [Tooltip("Gap between arrivals by the end of it.")]
        [SerializeField] private float fastestGap = 9f;
        [Tooltip("Never fill every trolley - leave somewhere to put the next one.")]
        [SerializeField] private int leaveFree = 1;

        private float _nextArrival;

        private void Update()
        {
            if (!IsServer) return;

            var director = ShiftDirector.Instance;
            if (director == null || director.Phase != ShiftPhase.Shift)
            {
                _nextArrival = Time.time + firstArrivalAfter;
                _seededForDay = -1;
                return;
            }

            SweepEmptyTrolleys();
            SeedNight(director.Day);

            if (Time.time < _nextArrival || !TryAdmit()) return;

            float gap = Mathf.Lerp(slowestGap, fastestGap, director.PhaseProgress);
            _nextArrival = Time.time + gap;
        }

        /// <summary>
        /// Start the night with a few already on trolleys. Walking into an empty ward and
        /// waiting is a bad opening, and it makes the game unreadable for anybody seeing it for
        /// the first time.
        /// </summary>
        private void SeedNight(int day)
        {
            if (_seededForDay == day) return;

            _seededForDay = day;
            for (int i = 0; i < startingPatients; i++)
                if (!TryAdmit()) break;

            ShiftDirector.Instance?.Announce("The ward is already busy.");
        }

        private int _seededForDay = -1;

        /// <summary>
        /// A patient who is done with - treated or dead - keeps their trolley until somebody
        /// physically wheels them out. That is deliberate: the ward silting up with bodies
        /// nobody has moved is the pressure.
        /// </summary>
        private void SweepEmptyTrolleys()
        {
            foreach (var gurney in Gurney.All)
                if (gurney != null && gurney.Occupant != null && gurney.Occupant.HasLeft)
                    gurney.Unload();
        }

        private bool TryAdmit()
        {
            Gurney target = null;

            foreach (var gurney in Gurney.All)
            {
                if (gurney == null || !gurney.IsFree) continue;
                if (!IntakeBay.IsInIntake(gurney.transform.position)) continue;
                target = gurney;
                break;
            }

            if (target == null)
            {
                WarnNoTrolley();
                return false;
            }

            Patient waiting = null;
            foreach (var patient in Patient.All)
            {
                if (patient == null || !patient.HasLeft) continue;
                waiting = patient;
                break;
            }

            if (waiting == null) return false;

            waiting.Admit();
            waiting.GetComponent<Operation>()?.Restart();
            target.Load(waiting);
            return true;
        }

        /// <summary>
        /// Said rarely, because the point is to be noticed. If every trolley is parked in a
        /// theatre or a corridor then nobody can be admitted, the quota stops moving, and
        /// somebody has to go and fetch one.
        /// </summary>
        private void WarnNoTrolley()
        {
            if (Time.time < _nextWarning) return;

            _nextWarning = Time.time + 20f;
            ShiftDirector.Instance?.Announce("No empty trolley in intake.");
        }

        private float _nextWarning;
    }
}
