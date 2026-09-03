using Probation.Surgery;
using Unity.Netcode;
using UnityEngine;

namespace Probation.Game
{
    /// <summary>
    /// Things going wrong on their own.
    ///
    /// Overcooked's third fix for players settling into comfortable roles was <em>disruption</em>
    /// - the level changing under you mid-round so the plan you agreed thirty seconds ago stops
    /// working. Without this, a ward is just a queue you work through.
    ///
    /// Seeded per night, so a run has a shape and two groups can compare the same night.
    /// Host only: these are consequences of host-simulated state.
    /// </summary>
    public class ComplicationDirector : NetworkBehaviour
    {
        [Header("Hiccup - handled by whoever is in the room, in seconds")]
        [SerializeField] private float hiccupGapMin = 18f;
        [SerializeField] private float hiccupGapMax = 34f;

        [Header("Code - needs hands from another room, now")]
        [SerializeField] private float codeGapMin = 70f;
        [SerializeField] private float codeGapMax = 120f;
        [Tooltip("How fast a coding patient bleeds. This one is meant to hurt.")]
        [SerializeField] private float codeBleedRate = 0.05f;

        [Header("Cover-up - the bill for what you thought you got away with")]
        [Tooltip("How fragile a patient has to be before the night catches up with them.")]
        [SerializeField] private float crashThreshold = 0.35f;
        [Tooltip("Harm per second during cover-up, scaled by how fragile they are.")]
        [SerializeField] private float crashHarmPerSecond = 0.09f;

        private System.Random _rng;
        private int _seededForDay = -1;
        private float _nextHiccup;
        private float _nextCode;

        private void Update()
        {
            if (!IsServer) return;

            var director = ShiftDirector.Instance;
            if (director == null) return;

            if (director.Phase == ShiftPhase.CoverUp)
            {
                CoverUp();
                return;
            }

            if (director.Phase != ShiftPhase.Shift) return;

            SeedForNight(director.Day);

            if (Time.time >= _nextHiccup)
            {
                Hiccup();
                _nextHiccup = Time.time + Range(hiccupGapMin, hiccupGapMax);
            }

            if (Time.time >= _nextCode)
            {
                Code();
                _nextCode = Time.time + Range(codeGapMin, codeGapMax);
            }
        }

        /// <summary>
        /// One seed per night, so every client's host rolls the same night and groups can say
        /// "night four is nonsense" and mean the same thing.
        /// </summary>
        private void SeedForNight(int day)
        {
            if (_seededForDay == day) return;

            _seededForDay = day;
            _rng = new System.Random(day * 7919);
            _crashing.Clear();
            _nextHiccup = Time.time + Range(hiccupGapMin, hiccupGapMax);
            _nextCode = Time.time + Range(codeGapMin, codeGapMax);
        }

        private float Range(float min, float max) => (float)(min + _rng.NextDouble() * (max - min));

        // ---------------------------------------------------------------- rungs

        /// <summary>A bleed opens on somebody nobody is looking at. Several per night.</summary>
        private void Hiccup()
        {
            var patient = PickOnWard(alive: true);
            if (patient == null) return;

            patient.StartBleeding(0.015f);
            ShiftDirector.Instance?.Announce("Something has opened up.");
        }

        /// <summary>
        /// Somebody crashes. Forces a choice: abandon your table or let theirs go - which is
        /// the whole point of having more beds than people.
        /// </summary>
        private void Code()
        {
            var patient = PickOnWard(alive: true);
            if (patient == null) return;

            patient.StartBleeding(codeBleedRate);
            patient.SetConscious(true);
            ShiftDirector.Instance?.Announce("CODE. Somebody is crashing.");
        }

        /// <summary>
        /// The twenty seconds after the doors shut, which until now were a title card that no
        /// system in the game read.
        ///
        /// Anybody you only half fixed comes apart here. Note exactly what that costs, because
        /// the timing is the whole design: ShiftDirector scores the quota on the Shift to CoverUp
        /// transition, so a patient who dies now has already been counted or has already failed
        /// to count. It cannot touch tonight. It goes on the hospital's body count, which is the
        /// thing that ends the run.
        ///
        /// So a cover-up death does not cost you the night. It costs you the week - which is
        /// precisely what the phase is named after.
        ///
        /// The discharge door stays open throughout, so these twenty seconds are damage control
        /// and not a cutscene: wheel them out and you can still save them.
        /// </summary>
        private void CoverUp()
        {
            foreach (var patient in Patient.All)
            {
                if (patient == null || patient.HasLeft || patient.IsDead) continue;
                if (patient.Fragility < crashThreshold) continue;

                // Named to whoever wrote the chart, so if this one dies the review reads out the
                // decision that killed them rather than "a patient was lost".
                ulong blame = patient.Chart != null ? patient.Chart.ChartedBy : ulong.MaxValue;

                patient.ApplyHarm(patient.Fragility * crashHarmPerSecond * Time.deltaTime,
                                  blame, null);

                if (_crashing.Add(patient))
                    ShiftDirector.Instance?.Announce("Somebody is going off in theatre.");
            }
        }

        /// <summary>Announced once each, not once a frame.</summary>
        private readonly System.Collections.Generic.HashSet<Patient> _crashing = new();

        private Patient PickOnWard(bool alive)
        {
            int count = 0;
            Patient chosen = null;

            // Reservoir sampling, so this stays one pass with no allocation.
            foreach (var patient in Patient.All)
            {
                if (patient == null || patient.HasLeft) continue;
                if (alive && patient.IsDead) continue;

                count++;
                if (_rng.Next(count) == 0) chosen = patient;
            }

            return chosen;
        }
    }
}
