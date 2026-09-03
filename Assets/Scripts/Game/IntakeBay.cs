using UnityEngine;

namespace Probation.Game
{
    /// <summary>
    /// Where new patients come in.
    ///
    /// Arrivals only ever appear on a trolley standing <em>here</em>, which turns the empty
    /// gurney into a resource: every one you wheel to a theatre or out to discharge is one that
    /// is no longer available to admit onto.
    ///
    /// Placeholder. Patients should eventually walk in, or arrive by ambulance - see the note
    /// in Scripts/Player/README.md.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class IntakeBay : MonoBehaviour
    {
        public static readonly System.Collections.Generic.List<IntakeBay> All = new();

        private Collider _volume;

        private void Reset()
        {
            var collider = GetComponent<Collider>();
            if (collider != null) collider.isTrigger = true;
        }

        private void Awake() => _volume = GetComponent<Collider>();
        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        public bool Contains(Vector3 point) => _volume != null && _volume.bounds.Contains(point);

        /// <summary>True when this trolley is parked somewhere a patient can be put on it.</summary>
        public static bool IsInIntake(Vector3 point)
        {
            foreach (var bay in All)
                if (bay != null && bay.Contains(point)) return true;

            return false;
        }

        private void OnDrawGizmos()
        {
            var collider = GetComponent<Collider>();
            if (collider == null) return;

            Gizmos.color = new Color(0.95f, 0.72f, 0.35f, 0.15f);
            Gizmos.DrawCube(collider.bounds.center, collider.bounds.size);
        }
    }
}
