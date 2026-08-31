using Probation.Interaction;
using Unity.Netcode;
using UnityEngine;

namespace Probation.Player
{
    /// <summary>
    /// Picking things up. Owns both grab mechanics so the objects themselves stay dumb.
    ///
    /// A tool is held by applying spring force and torque toward the hand anchor rather than by
    /// parenting it. Parenting takes the object out of the physics simulation, and the physics
    /// is where the comedy is - a scalpel should still clatter off a tray and shove a colleague.
    /// </summary>
    [RequireComponent(typeof(PlayerLocomotion))]
    public class PlayerCarry : NetworkBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerInputReader input;
        [SerializeField] private PlayerInteractor interactor;
        [SerializeField] private PlayerLocomotion locomotion;
        [Tooltip("Where held tools are pulled toward. Child of CameraPivot.")]
        [SerializeField] private Transform handAnchor;

        [Header("Tool hold")]
        [Tooltip("Spring strength per kg of tool. Stiffer feels more precise, softer more comic.")]
        [SerializeField] private float holdSpring = 300f;
        [SerializeField] private float holdDamper = 30f;
        [Tooltip("How hard a held tool rotates to match the hand.")]
        [SerializeField] private float holdTorque = 25f;
        [Tooltip("Drop the tool if something drags it further than this from your hand.")]
        [SerializeField] private float dropDistance = 2f;

        [Header("Timing")]
        [Tooltip("Give up on a grab the host never confirmed.")]
        [SerializeField] private float grabTimeout = 1f;

        public Transform Hand => handAnchor;
        public Vector3 HandPosition => handAnchor != null ? handAnchor.position : transform.position;

        /// <summary>What this intern currently has hold of, tool or heavy. Null when empty handed.</summary>
        public Grabbable Carried { get; private set; }
        public bool IsCarrying => Carried != null;

        private Rigidbody _carriedBody;
        private float _grabbedAt;
        private int _grabbedFrame = -1;

        private void Reset()
        {
            input = GetComponent<PlayerInputReader>();
            interactor = GetComponent<PlayerInteractor>();
            locomotion = GetComponent<PlayerLocomotion>();
        }

        private void Awake()
        {
            if (input == null) input = GetComponent<PlayerInputReader>();
            if (interactor == null) interactor = GetComponent<PlayerInteractor>();
            if (locomotion == null) locomotion = GetComponent<PlayerLocomotion>();
        }

        // ---------------------------------------------------------------- input

        private void Update()
        {
            if (!IsOwner || input == null) return;

            // Grabbing arrives through PlayerInteractor -> Grabbable.Interact -> TryGrab.
            // Grabbable.CanInteract returns false while carrying, so when your hands are full
            // the interactor finds nothing and Interact means "let go" instead.
            if (!input.InteractPressed) return;
            if (Time.frameCount == _grabbedFrame) return;   // don't release on the grab frame

            if (IsCarrying) Release();
        }

        // ---------------------------------------------------------------- grab / release

        public void TryGrab(Grabbable grabbable)
        {
            if (!IsOwner || grabbable == null || IsCarrying) return;

            Carried = grabbable;
            _carriedBody = grabbable.GetComponent<Rigidbody>();
            _grabbedAt = Time.time;
            _grabbedFrame = Time.frameCount;

            ApplyEncumbrance();

            // Where on the object the hand took hold, in the object's own space, so two people
            // hauling a gurney pull from the two places they actually grabbed.
            Vector3 localPoint = grabbable.transform.InverseTransformPoint(HandPosition);
            grabbable.RequestGrabRpc(localPoint);
        }

        public void Release()
        {
            if (Carried == null) return;

            Carried.RequestReleaseRpc();
            Carried = null;
            _carriedBody = null;
            ApplyEncumbrance();
        }

        // ---------------------------------------------------------------- hold

        private void FixedUpdate()
        {
            if (!IsOwner || Carried == null) return;

            // Heavy objects are pulled by the host, which reads our hand position straight off
            // the replicated player transform. All we do on this side is notice when it has been
            // dragged out of reach - the host applies the same test to drop its end of the spring.
            if (Carried.Kind != GrabKind.Tool)
            {
                float reach = Carried.HaulBreakDistance;
                if ((Carried.transform.position - HandPosition).sqrMagnitude > reach * reach)
                    Release();
                return;
            }

            // Ownership transfer is a round trip. Until it lands we are not allowed to move the
            // tool, and if it never lands the host refused us.
            if (!Carried.IsOwner)
            {
                if (Time.time - _grabbedAt > grabTimeout) Release();
                return;
            }

            if (_carriedBody == null) { Release(); return; }

            Vector3 toHand = HandPosition - _carriedBody.worldCenterOfMass;
            if (toHand.sqrMagnitude > dropDistance * dropDistance) { Release(); return; }

            float mass = _carriedBody.mass;
            Vector3 force = toHand * (holdSpring * mass) - _carriedBody.linearVelocity * (holdDamper * mass);
            _carriedBody.AddForce(force);

            AlignToHand(_carriedBody);
        }

        /// <summary>Torque the tool toward the hand's orientation without ever snapping it.</summary>
        private void AlignToHand(Rigidbody body)
        {
            if (handAnchor == null) return;

            Quaternion delta = handAnchor.rotation * Quaternion.Inverse(body.rotation);
            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (float.IsInfinity(axis.x)) return;
            if (angle > 180f) angle -= 360f;

            Vector3 target = axis.normalized * (angle * Mathf.Deg2Rad * holdTorque);
            body.AddTorque(target - body.angularVelocity * (holdTorque * 0.1f), ForceMode.Acceleration);
        }

        private void ApplyEncumbrance()
        {
            if (locomotion != null)
                locomotion.Encumbrance = Carried != null ? Carried.Encumbrance : 0f;
        }

        public override void OnNetworkDespawn()
        {
            Carried = null;
            _carriedBody = null;
        }
    }
}
