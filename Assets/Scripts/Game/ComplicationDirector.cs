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

        private System.Random _rng;
        private int _seededForDay = -1;
        private float _nextHiccup;
        private float _nextCode;

        private void Update()
        {
            if (!IsServer) return;

            var director = ShiftDirector.Instance;
            if (director == null || director.Phase != ShiftPhase.Shift) return;

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
