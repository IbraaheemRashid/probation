using UnityEngine;

namespace Probation.Surgery
{
    /// <summary>
    /// One kind of alien. Species are how the game gets variety without new systems: the same
    /// five procedures behave differently because the patient's rules changed, which is the
    /// cheapest content in the design.
    /// </summary>
    [CreateAssetMenu(menuName = "Probation/Species", fileName = "Species")]
    public class Species : ScriptableObject
    {
        [Header("Identity")]
        public string displayName = "unknown";

        [Header("Vitals")]
        [Tooltip("Resting rate. A 'flatline' is not always bad and a fast rate is not always alarming - only Xenobiology knows which.")]
        public float restingHeartRate = 70f;
        public float criticalHeartRate = 190f;
        [Tooltip("Seconds of untreated bleeding before this species dies.")]
        public float bleedOutSeconds = 45f;

        [Header("Rules")]
        [Tooltip("Reacts to noise. Voice volume becomes an input - shout near one of these and it wakes.")]
        public bool wakesToNoise;
        [Tooltip("Harmed by metal instruments. The manual does not mention this.")]
        public bool allergicToMetal;

        // There used to be a diagnosisText here, and the scanner printed it. It named the
        // condition outright - and because it lived on the species, every patient of a species
        // scanned identically no matter what was wrong with them. What a scan reports now lives
        // on Condition, as signs rather than as an answer.
    }
}
