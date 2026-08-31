using System;
using System.Collections.Generic;
using UnityEngine;

namespace Probation.Surgery
{
    /// <summary>
    /// One step of an operation. A step knows four things and nothing else: which tool
    /// satisfies it, which site it targets, what counts as done, and how it fails.
    ///
    /// Note what is absent - there is no "is this allowed" check. A step never refuses an
    /// input. Wrong things are allowed to happen and then have consequences, because a
    /// framework that validates and rejects produces a puzzle game, and this is not one.
    /// </summary>
    [Serializable]
    public class ProcedureStep
    {
        [Tooltip("Shown on the operation HUD, e.g. 'Open the thoracic seam'.")]
        public string displayName = "step";

        [Header("What satisfies it")]
        [Tooltip("Grabbable.toolId that this step needs.")]
        public string requiredToolId = "scalpel";
        [Tooltip("SurgerySite.siteId on the patient.")]
        public string targetSite = "torso";
        [Tooltip("Distinct interns who must hold a correct tool at the site at once.")]
        [Min(1)] public int handsRequired = 1;

        [Header("Tolerance band")]
        [Tooltip("How near the site the tool must be. The host judges slightly stale positions, so exact contact tests feel broken to everyone who is not hosting.")]
        public float tolerance = 0.35f;
        [Tooltip("How long it must stay there. 'Close enough for long enough', never 'touched this exact point'.")]
        public float holdSeconds = 1.5f;

        [Header("How it fails")]
        [Tooltip("Harm dealt when the wrong tool is used at this site.")]
        [Range(0f, 1f)] public float wrongToolHarm = 0.12f;
        [Tooltip("Completing this step opens a bleed that somebody has to close.")]
        public bool opensBleed;
        public float bleedRatePerSecond = 0.02f;
        [Tooltip("Completing this step closes any open bleed.")]
        public bool closesBleed;
        [Tooltip("Completing this step puts the patient under.")]
        public bool sedates;
        [Tooltip("Doing this to a patient who is still awake hurts them, badly.")]
        public bool requiresUnconscious = true;
    }

    /// <summary>
    /// A whole operation, as data. Procedure two should be a new asset and a new tool, not new
    /// code - if it is not, this framework grew in the wrong direction and wants cutting back.
    /// </summary>
    [CreateAssetMenu(menuName = "Probation/Procedure", fileName = "Procedure")]
    public class Procedure : ScriptableObject
    {
        public string displayName = "procedure";
        [TextArea] public string description;
        public List<ProcedureStep> steps = new();
    }
}
