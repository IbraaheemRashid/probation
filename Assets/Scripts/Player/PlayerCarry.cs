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
        [Tooltip("Ceiling on how fast a held tool chases your hand. Lower feels heavier.")]
        [SerializeField] private float maxCarrySpeed = 26f;
        [Tooltip("How much mass eats into that ceiling. 0 makes every tool equally nimble.")]
        [SerializeField] private float massDrag = 0.5f;
        [Tooltip("How hard the hand can accelerate a held tool, before mass. THIS is the weight knob - the speed ceiling never binds at bracing speed. Lower makes heavy instruments trail further behind a fast drag; too low and they fall behind dropDistance.")]
        [SerializeField] private float carryForce = 8f;
        [SerializeField] private float maxCarrySpin = 32f;
        [Tooltip("Extra push on release, on top of the speed it already had.")]
        [SerializeField] private float throwBoost = 1.15f;
        [Tooltip("Drop the tool if it somehow ends up further than this from your hand.")]
        [SerializeField] private float dropDistance = 2.5f;

        [Header("Timing")]
        [Tooltip("Give up on a grab the host never confirmed.")]
        [SerializeField] private float grabTimeout = 1f;
        [Tooltip("Hold Interact longer than this and letting go drops the item. A quicker tap keeps it.")]
        [SerializeField] private float holdToCarrySeconds = 0.25f;

        public Transform Hand => handAnchor;
        public Vector3 HandPosition => handAnchor != null ? handAnchor.position : transform.position;

        /// <summary>What this intern currently has hold of, tool or heavy. Null when empty handed.</summary>
        public Grabbable Carried { get; private set; }
        public bool IsCarrying => Carried != null;

        private Rigidbody _carriedBody;
        private float _grabbedAt;
        private float _pressedAt;

        private Collider[] _ownColliders;
        private readonly System.Collections.Generic.List<Collider> _ignored = new();
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

            _ownColliders = GetComponentsInChildren<Collider>(true);
        }

        // ---------------------------------------------------------------- input

        private void Update()
        {
            if (!IsOwner || input == null) return;

            if (input.InteractPressed) _pressedAt = Time.time;

            // Two ways to let go, which between them cover how everyone expects this to work:
            //
            //   tap E            -> pick up and keep it, tap again to drop
            //   hold E ... let go -> carry while held, drops the moment you release
            //
            // Grabbing itself arrives through PlayerInteractor -> Grabbable.Interact -> TryGrab.
            // Grabbable.CanInteract is false while carrying, so with full hands the interactor
            // finds nothing and Interact can only mean "let go".
            if (!IsCarrying) return;

            // Never on the frame we grabbed - the press that picked it up would drop it again.
            if (Time.frameCount == _grabbedFrame) return;

            if (input.InteractPressed)
            {
                Release();
                return;
            }

            if (input.InteractReleased && Time.time - _pressedAt >= holdToCarrySeconds)
                Release();
        }

        // ---------------------------------------------------------------- grab / release

        public void TryGrab(Grabbable grabbable)
        {
            if (!IsOwner || grabbable == null || IsCarrying) return;

            Carried = grabbable;
            _carriedBody = grabbable.GetComponent<Rigidbody>();
            _grabbedAt = Time.time;

            if (_carriedBody != null)
            {
                // Stays a dynamic body throughout - that is the whole point. It just stops
                // falling, and stops colliding with the person carrying it.
                _carriedBody.angularVelocity = Vector3.zero;
                _carriedBody.useGravity = false;
                _carriedBody.interpolation = RigidbodyInterpolation.Interpolate;
                IgnoreSelfCollision(true);
            }
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

            ReleaseRigid(_carriedBody);

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

            if ((HandPosition - _carriedBody.position).sqrMagnitude > dropDistance * dropDistance)
            {
                Release();
                return;
            }

            TrackToHand();
        }

        /// <summary>
        /// Velocity tracking: set the velocity that lands the tool on your hand <em>this</em>
        /// step, rather than pushing it in roughly the right direction and hoping.
        ///
        /// This is what the gravity gun and the physgun do, and what Unity's own XR toolkit
        /// calls Velocity Tracking. An earlier version applied spring force toward the hand,
        /// which is an oscillator chasing a moving target - it can only ever lag, and tuning
        /// the spring only changes how it lags.
        ///
        /// The tool stays a real rigidbody throughout. Physics still sweeps it, so it is
        /// blocked by walls rather than passing through them, and it still knocks things over.
        ///
        /// The speed ceiling is where weight comes from: a heavy instrument cannot keep up with
        /// a fast turn, so it trails and swings behind you. That lag is wanted - it is the
        /// difference between carrying a scalpel and carrying a bone saw.
        /// </summary>
        private void TrackToHand()
        {
            float step = Time.fixedDeltaTime;
            if (step <= 0f || handAnchor == null) return;

            // Was Max(1f, mass * massDrag), and that floor ate the whole mechanic: every tool in
            // the game masses under 2 kg, so the product never reached 1 and all three testbed
            // scalpels clamped to exactly maxCarrySpeed. The blade-weight experiment the testbed
            // was built for could not have shown anything. 1 + mass*massDrag is continuous, still
            // never exceeds maxCarrySpeed, and bites at surgical masses.
            float ceiling = maxCarrySpeed / (1f + _carriedBody.mass * massDrag);

            Vector3 wanted = (handAnchor.position - _carriedBody.position) / step;
            Vector3 target = Vector3.ClampMagnitude(wanted, ceiling);

            // The ceiling alone still cannot make weight felt while bracing, and it is worth being
            // clear why: a braced hand moves at well under 1 m/s, and the ceiling sits one to two
            // orders of magnitude above that, so it never binds during a cut. Setting velocity
            // straight to target is perfect tracking - mass is mathematically incapable of
            // mattering. An acceleration limit divided by mass is what actually produces lag, at
            // any speed, which is the difference between carrying a scalpel and carrying a saw.
            float accel = carryForce / Mathf.Max(0.05f, _carriedBody.mass);
            _carriedBody.linearVelocity = Vector3.MoveTowards(_carriedBody.linearVelocity, target, accel * step);

            Quaternion delta = handAnchor.rotation * Quaternion.Inverse(_carriedBody.rotation);

            // Shortest path, or a nearly-aligned tool spins the long way round.
            if (delta.w < 0f) delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);

            delta.ToAngleAxis(out float angle, out Vector3 axis);
            if (axis.sqrMagnitude < 0.0001f || float.IsNaN(axis.x) || float.IsInfinity(axis.x)) return;
            if (angle > 180f) angle -= 360f;

            Vector3 spin = axis.normalized * (angle * Mathf.Deg2Rad / step);
            _carriedBody.angularVelocity = Vector3.ClampMagnitude(spin, maxCarrySpin);
        }

        /// <summary>
        /// Hand it back. Velocity tracking means it is already moving at roughly hand speed, so
        /// a throw falls out of the physics rather than being faked on release.
        /// </summary>
        private void ReleaseRigid(Rigidbody body)
        {
            IgnoreSelfCollision(false);
            if (body == null) return;

            body.useGravity = true;
            body.linearVelocity *= throwBoost;
        }

        /// <summary>
        /// Stop the thing in our hand from colliding with us, and only with us. It stays solid
        /// against the ward, so a held scalpel still sweeps a tray off a bench - it just cannot
        /// push the person holding it.
        /// </summary>
        private void IgnoreSelfCollision(bool ignore)
        {
            if (ignore)
            {
                _ignored.Clear();
                if (Carried == null) return;
                Carried.GetComponentsInChildren(true, _ignored);
            }

            if (_ownColliders == null) return;

            foreach (var theirs in _ignored)
            {
                if (theirs == null) continue;
                foreach (var ours in _ownColliders)
                {
                    if (ours == null) continue;
                    Physics.IgnoreCollision(theirs, ours, ignore);
                }
            }

            if (!ignore) _ignored.Clear();
        }

        private void ApplyEncumbrance()
        {
            if (locomotion != null)
                locomotion.Encumbrance = Carried != null ? Carried.Encumbrance : 0f;
        }

        public override void OnNetworkDespawn()
        {
            // Never leave a tool kinematic and floating because the holder disconnected.
            ReleaseRigid(_carriedBody);
            Carried = null;
            _carriedBody = null;
        }
    }
}
