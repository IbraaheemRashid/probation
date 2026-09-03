using Probation.Surgery;
using Unity.Netcode;
using UnityEngine;

namespace Probation.Game
{
    /// <summary>
    /// A trolley with somebody on it. This is how patients move through the ward, and it is why
    /// the map matters: a patient is never where you need them, and getting them there is a
    /// two-hand job down a corridor full of other people doing the same thing.
    ///
    /// The occupant is pinned to the surface on the host rather than left to balance there.
    /// Physics-riding looks better for about four seconds and then somebody takes a corner.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class Gurney : NetworkBehaviour
    {
        [SerializeField] private Transform surface;

        public static readonly System.Collections.Generic.List<Gurney> All = new();

        public Patient Occupant { get; private set; }
        public bool IsFree => Occupant == null || Occupant.HasLeft;
        public Vector3 Surface => surface != null ? surface.position : transform.position + Vector3.up * 0.5f;

        public override void OnNetworkSpawn() => All.Add(this);
        public override void OnNetworkDespawn() => All.Remove(this);

        public void Load(Patient patient)
        {
            if (!IsServer) return;

            Occupant = patient;
            if (patient == null) return;

            patient.Ride = this;
            Teleport(patient);
        }

        /// <summary>
        /// Hard placement, used when a patient is first put on the trolley.
        ///
        /// This has to be a teleport rather than MovePosition: patients wait far below the ward
        /// between admissions, and MovePosition on a dynamic body is a <em>swept</em> move, so
        /// lifting one up to the trolley gets stopped dead by the ward floor on the way. The
        /// symptom is a patient who never appears and is quietly sitting under the map.
        /// </summary>
        private void Teleport(Patient patient)
        {
            var body = patient.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.position = Surface;
                body.rotation = transform.rotation;
            }

            patient.transform.SetPositionAndRotation(Surface, transform.rotation);
        }

        public void Unload()
        {
            if (!IsServer) return;

            if (Occupant != null && Occupant.Ride == this) Occupant.Ride = null;
            Occupant = null;
        }

        private void FixedUpdate()
        {
            if (!IsServer || Occupant == null) return;

            if (Occupant.HasLeft)
            {
                Unload();
                return;
            }

            Place(Occupant);
        }

        /// <summary>
        /// Per-tick follow once they are already on board. Small deltas, so a swept move is
        /// correct here - it keeps the patient a real collider that instruments and the
        /// discharge trigger still see, rather than something teleporting through walls.
        /// </summary>
        private void Place(Patient patient)
        {
            var body = patient.GetComponent<Rigidbody>();
            if (body == null)
            {
                patient.transform.position = Surface;
                return;
            }

            // If they have somehow ended up a long way off, snap rather than sweep.
            if ((body.position - Surface).sqrMagnitude > 4f)
            {
                Teleport(patient);
                return;
            }

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.MovePosition(Surface);
            body.MoveRotation(transform.rotation);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = IsFree ? new Color(0.3f, 0.8f, 0.7f, 0.5f) : new Color(0.9f, 0.5f, 0.3f, 0.6f);
            Gizmos.DrawWireCube(Surface, new Vector3(0.7f, 0.15f, 1.9f));
        }
    }
}
