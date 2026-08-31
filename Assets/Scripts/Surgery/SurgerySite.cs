using UnityEngine;

namespace Probation.Surgery
{
    /// <summary>A named place on a patient that procedure steps can target.</summary>
    public class SurgerySite : MonoBehaviour
    {
        [Tooltip("Matched against ProcedureStep.targetSite.")]
        public string siteId = "torso";

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 0.8f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, 0.15f);
        }
    }
}
