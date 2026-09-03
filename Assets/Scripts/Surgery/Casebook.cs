using System;
using System.Collections.Generic;
using UnityEngine;

namespace Probation.Surgery
{
    /// <summary>One kind of patient that can walk through the door, and how often.</summary>
    [Serializable]
    public class CaseWeight
    {
        public Condition condition;
        public Species species;
        [Min(0f)] public float weight = 1f;

        [Tooltip("Nights this case can appear on. Use it as the unlock curve - night one should not contain anything ambiguous.")]
        public int fromNight = 1;
        public int untilNight = 99;

        public bool AvailableOn(int night) => night >= fromNight && night <= untilNight;
    }

    /// <summary>
    /// Every species, procedure and condition in the game, in a fixed order.
    ///
    /// This exists for one hard reason: <b>a NetworkVariable cannot carry a ScriptableObject
    /// reference.</b> A patient's species and condition have to reach every client, so they
    /// travel as integer indices into these lists, and each client resolves them back through
    /// its own copy of this asset.
    ///
    /// Which means: <b>the order of these lists is the wire format.</b> Reordering one mid-session
    /// silently renames every patient on the ward - the host's Thoracid with a foreign body
    /// becomes somebody else's Vithrid with a laceration, and nothing anywhere will report an
    /// error. Add to the end. Never insert, never sort.
    /// </summary>
    [CreateAssetMenu(menuName = "Probation/Casebook", fileName = "Casebook")]
    public class Casebook : ScriptableObject
    {
        [Header("ORDER IS THE WIRE FORMAT - append only, never insert or sort")]
        public List<Species> species = new();
        public List<Procedure> procedures = new();
        public List<Condition> conditions = new();

        [Header("Who comes through the door")]
        public List<CaseWeight> arrivals = new();

        /// <summary>
        /// The casebook this session is running.
        ///
        /// Set from <c>PatientIntake.Awake</c>, which lives on the NetworkManager object and so
        /// exists on every client. Every Awake runs before any OnNetworkSpawn, so this is
        /// populated by the time a patient tries to resolve an index.
        /// </summary>
        public static Casebook Active { get; set; }

        // ---------------------------------------------------------------- index <-> asset

        public Species SpeciesAt(int index) => At(species, index);
        public Procedure ProcedureAt(int index) => At(procedures, index);
        public Condition ConditionAt(int index) => At(conditions, index);

        public int IndexOf(Species value) => species.IndexOf(value);
        public int IndexOf(Procedure value) => procedures.IndexOf(value);
        public int IndexOf(Condition value) => conditions.IndexOf(value);

        private static T At<T>(List<T> list, int index) where T : class =>
            list != null && index >= 0 && index < list.Count ? list[index] : null;

        // ---------------------------------------------------------------- drawing a case

        /// <summary>
        /// Roll the next arrival for this night.
        ///
        /// The caller owns the RNG so a night is reproducible - two groups can argue about the
        /// same night four, and a bad one can be replayed instead of described.
        /// </summary>
        public bool TryDraw(int night, System.Random rng, out Species drawnSpecies, out Condition drawnCondition)
        {
            drawnSpecies = null;
            drawnCondition = null;

            float total = 0f;
            foreach (var arrival in arrivals)
                if (Eligible(arrival, night)) total += arrival.weight;

            if (total <= 0f) return false;

            double roll = rng.NextDouble() * total;

            foreach (var arrival in arrivals)
            {
                if (!Eligible(arrival, night)) continue;

                roll -= arrival.weight;
                if (roll > 0d) continue;

                drawnSpecies = arrival.species;
                drawnCondition = arrival.condition;
                return true;
            }

            return false;
        }

        private static bool Eligible(CaseWeight arrival, int night) =>
            arrival != null && arrival.weight > 0f && arrival.condition != null
            && arrival.species != null && arrival.AvailableOn(night);

        /// <summary>
        /// Everything a chart may be written for tonight.
        ///
        /// This is the night's list, not this patient's - offering only the procedures that could
        /// possibly be right would put the answer in the chart itself. It doubles as the unlock
        /// curve: a night whose arrivals need only triage never offers extraction, so early
        /// nights are narrow without anybody being told they are.
        ///
        /// It will never shrink to a single option, because a list with one entry answers itself.
        /// </summary>
        public List<Procedure> ChartableOn(int night)
        {
            var chartable = new List<Procedure>();

            foreach (var arrival in arrivals)
            {
                if (!Eligible(arrival, night)) continue;

                var treatment = arrival.condition.TreatmentFor(arrival.species);
                if (treatment != null && !chartable.Contains(treatment)) chartable.Add(treatment);
            }

            foreach (var procedure in procedures)
            {
                if (chartable.Count >= MinimumChartOptions) break;
                if (procedure != null && !chartable.Contains(procedure)) chartable.Add(procedure);
            }

            return chartable;
        }

        /// <summary>Below this the chart stops being a choice.</summary>
        private const int MinimumChartOptions = 2;
    }
}
