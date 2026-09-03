using Probation.Surgery;
using UnityEngine;

namespace Probation.Game
{
    /// <summary>
    /// A room you are allowed to operate in.
    ///
    /// Without this the ward is one big room and the gurneys are decoration - you would simply
    /// operate on whoever wherever they happened to arrive. Gating procedures on being inside a
    /// bay is what turns "a patient arrived" into "somebody has to wheel them somewhere", which
    /// is the trip that makes the corridor, the doorways and the other three interns matter.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class OperatingBay : MonoBehaviour
    {
        public static readonly System.Collections.Generic.List<OperatingBay> All = new();

        [SerializeField] private string bayName = "OR";
        public string BayName => bayName;

        private Collider _volume;

        private void Reset()
        {
            var collider = GetComponent<Collider>();
            if (collider != null) collider.isTrigger = true;
        }

        private void Awake() => _volume = GetComponent<Collider>();
        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        public bool Contains(Vector3 point) =>
            _volume != null && _volume.bounds.Contains(point);

        /// <summary>Which bay this patient is in, or null if they are still in a corridor.</summary>
        public static OperatingBay Holding(Patient patient)
        {
            if (patient == null) return null;

            foreach (var bay in All)
                if (bay != null && bay.Contains(patient.transform.position)) return bay;

            return null;
        }

        private void OnDrawGizmos()
        {
            var collider = GetComponent<Collider>();
            if (collider == null) return;

            Gizmos.color = new Color(0.36f, 0.78f, 0.72f, 0.15f);
            Gizmos.DrawCube(collider.bounds.center, collider.bounds.size);
        }
    }
}
