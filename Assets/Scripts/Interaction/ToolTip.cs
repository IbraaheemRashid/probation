using UnityEngine;

namespace Probation.Interaction
{
    /// <summary>
    /// The working end of an instrument.
    ///
    /// Until now a tool was a box with one collider and no idea which end was which - the old
    /// step evaluator overlap-tested the site and accepted any collider on any held tool, so a
    /// scalpel held backwards worked exactly as well as one held properly. Aiming is the whole
    /// point of bracing, and you cannot aim something that has no point.
    ///
    /// Convention: a tool's working direction is its local +Z, and the tip sits at the far +Z end.
    /// </summary>
    public class ToolTip : MonoBehaviour
    {
        [Tooltip("Drawn in the editor so you can see which way round the instrument is.")]
        [SerializeField] private float gizmoLength = 0.06f;

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.35f, 0.4f, 0.9f);
            Gizmos.DrawSphere(transform.position, 0.006f);
            Gizmos.color = new Color(1f, 0.35f, 0.4f, 0.35f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * gizmoLength);
        }
    }
}
