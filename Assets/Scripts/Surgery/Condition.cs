using System;
using System.Collections.Generic;
using UnityEngine;

namespace Probation.Surgery
{
    /// <summary>
    /// What to do about one condition, on one species.
    ///
    /// A null <see cref="treatment"/> means <b>do not operate</b>. That is not a missing value;
    /// it is the answer, and it is the whole reason this type exists.
    /// </summary>
    [Serializable]
    public class ConditionAnswer
    {
        [Tooltip("Leave empty for the fallback answer used by every species not named elsewhere.")]
        public Species species;

        [Tooltip("The procedure that fixes it. EMPTY MEANS DO NOT OPERATE - see the class summary.")]
        public Procedure treatment;

        [Header("Getting it right")]
        [Tooltip("Harm removed when the correct procedure completes.")]
        [Range(0f, 1f)] public float reliefIfCorrect = 0.35f;

        [Header("Getting it wrong")]
        [Tooltip("Harm per completed step of the wrong procedure. Quiet on purpose - nothing announces it.")]
        [Range(0f, 1f)] public float harmPerWrongStep = 0.06f;
        [Tooltip("Harm dealt for operating at all, when the answer was to leave them alone. 1 kills.")]
        [Range(0f, 1f)] public float harmIfOperated;
        [Tooltip("How fragile the wrong procedure leaves them. They look fine and die on the way out.")]
        [Range(0f, 1f)] public float fragilityIfWrong = 0.6f;

        [Header("What the supervisor reads out")]
        public string reviewLineWrong = "ran the wrong procedure";
        public string reviewLineRight = "read it correctly";
    }

    /// <summary>
    /// Something wrong with a patient, described by how it <em>presents</em> rather than by what
    /// it is.
    ///
    /// One asset per presentation, with the answers keyed by species inside it. That is
    /// deliberate and it is the point of the whole system: "an organ that presents exactly like
    /// a foreign body" is one presentation with two answers. Splitting it into two assets would
    /// mean keeping their signs byte-identical by hand forever, and the moment they drift the
    /// players can tell them apart without knowing any biology. The interns are supposed to be
    /// confused by that pair. The person authoring it is not.
    ///
    /// Nothing here may name the answer. <see cref="scannerLines"/> reports signs; the player
    /// maps sign plus species onto a condition themselves. That mapping, carried in their heads
    /// and shouted across a ward, is the only progression this game has.
    /// </summary>
    [CreateAssetMenu(menuName = "Probation/Condition", fileName = "Condition")]
    public class Condition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable key. Used for authoring checks and save data, never shown to a player.")]
        public string id = "condition";
        [Tooltip("Authoring and end-of-shift review lines ONLY. Never put this on the scanner.")]
        public string displayName = "unnamed condition";

        [Header("How it presents")]
        [Tooltip("Signs, never the answer. 'dense mass, upper cavity - static', not 'foreign body'.")]
        [TextArea] public string[] scannerLines = Array.Empty<string>();

        [Tooltip("Added to the species' resting rate. This is how one number means two things: 110 is alarming on a Thoracid and dull on a Vithrid.")]
        public float restingRateOffset;

        [Tooltip("How ill they look from across the ward, before anybody has touched them. Drives the flesh shader independently of harm.")]
        [Range(0f, 1f)] public float presentingSickness;

        [Header("How they arrive")]
        public bool arrivesUnconscious;
        public float arrivesBleedingRate;
        [Tooltip("Harm already taken before the shift started.")]
        [Range(0f, 1f)] public float arrivesHarmed;

        [Tooltip("How fast it gets worse while nobody is doing anything about it.")]
        public float untreatedHarmPerSecond = 0.004f;

        [Header("What to do about it")]
        public List<ConditionAnswer> answers = new();

        /// <summary>
        /// The answer for this species, falling back to the entry with no species set.
        ///
        /// Returns null when neither exists, which is an authoring fault rather than a meaning -
        /// see the note on <see cref="LeaveAlone"/>.
        /// </summary>
        public ConditionAnswer AnswerFor(Species species)
        {
            ConditionAnswer fallback = null;

            foreach (var answer in answers)
            {
                if (answer == null) continue;
                if (answer.species == species && species != null) return answer;
                if (answer.species == null) fallback = answer;
            }

            return fallback;
        }

        /// <summary>The procedure that fixes this, or null if the answer is to leave them alone.</summary>
        public Procedure TreatmentFor(Species species) => AnswerFor(species)?.treatment;

        /// <summary>
        /// True when the correct action is to operate on nobody.
        ///
        /// Note the asymmetry with <see cref="TreatmentFor"/>: a condition with no answer at all
        /// for this species also has no treatment, but it is <em>not</em> a leave-alone case. A
        /// missing answer is a broken asset, and quietly reading it as "correct to do nothing"
        /// would turn every authoring slip into a patient the interns are punished for treating.
        /// </summary>
        public bool LeaveAlone(Species species)
        {
            var answer = AnswerFor(species);
            return answer != null && answer.treatment == null;
        }
    }
}
