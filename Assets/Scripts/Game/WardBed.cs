using Probation.Surgery;
using UnityEngine;

namespace Probation.Game
{
    /// <summary>
    /// A place a patient can be put. Beds are positions, not patients - patients are objects
    /// that get wheeled between them, which is what lets the ward have six beds and a pool of
    /// bodies rather than six permanent props.
    /// </summary>
    public class WardBed : MonoBehaviour
    {
        [Tooltip("Where a patient sits when admitted here.")]
        [SerializeField] private Transform surface;

        public static readonly System.Collections.Generic.List<WardBed> All = new();

        public Patient Occupant { get; private set; }
        public bool IsFree => Occupant == null;
        public Vector3 Surface => surface != null ? surface.position : transform.position + Vector3.up * 0.7f;

        private void OnEnable() => All.Add(this);
        private void OnDisable() => All.Remove(this);

        public void Occupy(Patient patient)
        {
            Occupant = patient;
            if (patient != null) patient.Bed = this;
        }

        public void Clear()
        {
            if (Occupant != null && Occupant.Bed == this) Occupant.Bed = null;
            Occupant = null;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = IsFree ? new Color(0.3f, 0.8f, 0.7f, 0.5f) : new Color(0.9f, 0.5f, 0.3f, 0.5f);
            Gizmos.DrawWireCube(Surface, new Vector3(0.7f, 0.1f, 1.9f));
        }
    }
}
