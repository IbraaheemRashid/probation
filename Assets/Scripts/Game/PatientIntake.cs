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

        [Tooltip("Every species, condition and procedure in the game. Assigned by ProbationSetup.")]
        [SerializeField] private Casebook casebook;

        private float _nextArrival;

        /// <summary>Rolls the night's arrivals. Seeded per night so a bad one can be replayed.</summary>
        private System.Random _rng = new(1);

        /// <summary>Last case drawn, so an immediate repeat can be rerolled once.</summary>
        private Condition _lastCondition;

        private bool _warnedNoCasebook;

        private void Awake()
        {
            // Every client needs this, not only the host. Patients replicate their species and
            // condition as indices into the casebook's lists, so a client without one resolves
            // every patient in the ward to nothing at all.
            if (casebook != null) Casebook.Active = casebook;
        }

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

            // One seed per night, so the host rolls a night two groups can argue about by
            // number. A different prime to ComplicationDirector's, or intake and the emergency
            // ladder would march in step all night.
            _rng = new System.Random(day * 6151);
            _lastCondition = null;

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
            if (!TryDrawCase(out var drawnSpecies, out var drawnCondition)) return false;

            waiting.Admit(drawnSpecies, drawnCondition);

            var operation = waiting.GetComponent<Operation>();
            operation?.Restart();

            // TEMPORARY SCAFFOLD. Hands the ward the correct answer so a night is playable
            // before the chart exists. Delete this the moment PatientChart lands - a game that
            // assigns the right procedure by itself has made diagnosis decorative, which is
            // precisely the thing this whole system exists to stop.
            operation?.Assign(drawnCondition != null ? drawnCondition.TreatmentFor(drawnSpecies) : null);

            target.Load(waiting);
            return true;
        }

        /// <summary>
        /// Roll the next case, rejecting an immediate repeat once.
        ///
        /// Without that reroll the three seeded openers land on the same case far more often
        /// than a player reads as random, and the first thing anybody learns about the ward is
        /// that it does not vary.
        /// </summary>
        private bool TryDrawCase(out Species drawnSpecies, out Condition drawnCondition)
        {
            drawnSpecies = null;
            drawnCondition = null;

            var director = ShiftDirector.Instance;
            if (director == null) return false;

            var book = Casebook.Active;
            if (book == null)
            {
                // Loud, because the symptom is otherwise indistinguishable from a quiet night:
                // no patient is ever admitted, nothing errors, and the ward simply stays empty.
                if (!_warnedNoCasebook)
                {
                    _warnedNoCasebook = true;
                    Debug.LogError("[Intake] No casebook assigned - nobody can be admitted. " +
                                   "Run Probation > Setup > 7, or Probation > Verify and Repair Scene.", this);
                }

                return false;
            }

            if (!book.TryDraw(director.Day, _rng, out drawnSpecies, out drawnCondition)) return false;

            if (drawnCondition == _lastCondition
                && book.TryDraw(director.Day, _rng, out var second, out var secondCondition))
            {
                drawnSpecies = second;
                drawnCondition = secondCondition;
            }

            _lastCondition = drawnCondition;
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
