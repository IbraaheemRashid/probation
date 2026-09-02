using Probation.Interaction;
using UnityEngine;

namespace Probation.Player
{
    /// <summary>
    /// The one idea in the control scheme: the mouse does two jobs and never both at once.
    /// Unbraced it is your head. Braced it is your hands.
    ///
    /// That swap is what makes precision possible without a menu, a minigame or a camera
    /// transition, and it is what makes an operating medic <em>blind</em> - which is the asymmetry
    /// the whole co-op design is built on. The person with their hands inside a patient is the
    /// only one who can see the evidence and the only one who cannot see the consequence.
    ///
    /// Notice what this component does NOT do: it never touches the held tool, its rigidbody, or
    /// PlayerCarry. It moves the hand anchor, and PlayerCarry.TrackToHand drives the instrument
    /// there with a mass-derived speed ceiling that already exists. A heavy instrument therefore
    /// lags behind the cursor and trails on a fast drag, for free - and that lag is the feel.
    /// </summary>
    public class PlayerBrace : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerInputReader input;
        [SerializeField] private PlayerInteractor interactor;
        [SerializeField] private PlayerCarry carry;
        [SerializeField] private PlayerLook look;
        [SerializeField] private PlayerLocomotion locomotion;
        [Tooltip("The camera under the pivot. Leaning moves this, never the pivot - Locomotion owns the pivot's local Y for crouch and eye height and will fight you for it.")]
        [SerializeField] private Camera view;

        [Header("Reach")]
        [Tooltip("How far in front of you a bracing surface can be. Bracing is a commitment to something you were already close enough to touch.")]
        [SerializeField] private float braceReach = 1.6f;
        [SerializeField] private LayerMask workMask = ~0;

        [Header("Cursor")]
        [Tooltip("Metres of instrument travel per mouse count. Independent of look sensitivity - this is a hand, not a head.")]
        [SerializeField] private float braceSensitivity = 0.0012f;
        [Tooltip("Metres per second at full stick deflection.")]
        [SerializeField] private float gamepadBraceSpeed = 0.35f;
        [Tooltip("How much body one brace reaches, in metres either side of where you leaned in. The clamp is what keeps a brace local: you work a site, not a patient.")]
        [SerializeField] private Vector2 cursorExtent = new(0.20f, 0.20f);
        [Tooltip("How far the instrument's tip is aimed off the surface. Near zero: the blade should rest on the body, and the carry physics will not let it sink in.")]
        [SerializeField] private float handStandoff = 0.01f;

        [Tooltip("Log why a brace did not take. A silent refusal is indistinguishable from a broken build, which is exactly how long it takes to notice the component was never on the prefab.")]
        [SerializeField] private bool explainFailures = true;

        [Header("Lean")]
        [SerializeField] private float leanSeconds = 0.25f;
        [SerializeField] private float bracedFov = 50f;
        [Tooltip("Camera-local offset at full brace. Down and forwards - you are leaning in.")]
        [SerializeField] private Vector3 leanOffset = new(0f, -0.05f, 0.15f);

        /// <summary>Hands, not head. True from the moment the brace takes.</summary>
        public bool IsBraced { get; private set; }

        /// <summary>0 while stood up, 1 fully leaned in. Drives every visual and the hand.</summary>
        public float Blend => _blend;

        /// <summary>The surface being worked, captured on entry and frozen. It does not track the camera.</summary>
        public Plane WorkPlane { get; private set; }
        public Vector3 PlaneNormal { get; private set; } = Vector3.up;

        /// <summary>Where the instrument's tip is being aimed. The input to every surgical tool.</summary>
        public Vector3 Cursor { get; private set; }

        /// <summary>What was in the hand when the brace took. Tools read this rather than re-querying.</summary>
        public Grabbable Instrument { get; private set; }

        private Transform _pivot;

        private Vector3 _handRestLocal;
        private Quaternion _handRestLocalRot;
        private Vector3 _camRestLocal;
        private float _restFov;

        private Vector3 _planeOrigin;
        private Vector3 _planeRight;
        private Vector3 _planeUp;
        private Vector2 _cursorOffset;

        // Frozen at the last braced value when you let go, so the hand blends back out along the
        // path it came in on instead of snapping.
        private Vector3 _handLocalTarget;
        private Quaternion _handLocalTargetRot;

        private float _blend;

        private void Reset()
        {
            input = GetComponent<PlayerInputReader>();
            interactor = GetComponent<PlayerInteractor>();
            carry = GetComponent<PlayerCarry>();
            locomotion = GetComponent<PlayerLocomotion>();
            look = GetComponentInChildren<PlayerLook>();
            view = GetComponentInChildren<Camera>();
        }

        private void Awake()
        {
            if (input == null) input = GetComponent<PlayerInputReader>();
            if (interactor == null) interactor = GetComponent<PlayerInteractor>();
            if (carry == null) carry = GetComponent<PlayerCarry>();
            if (locomotion == null) locomotion = GetComponent<PlayerLocomotion>();
            if (look == null) look = GetComponentInChildren<PlayerLook>();
            if (view == null) view = GetComponentInChildren<Camera>();

            if (carry != null && carry.Hand != null)
            {
                _pivot = carry.Hand.parent;
                _handRestLocal = carry.Hand.localPosition;
                _handRestLocalRot = carry.Hand.localRotation;
                _handLocalTarget = _handRestLocal;
                _handLocalTargetRot = _handRestLocalRot;
            }

            if (view != null)
            {
                _camRestLocal = view.transform.localPosition;
                _restFov = view.fieldOfView;
            }

            // You are not a surface. Always stripped rather than left to the serialized value,
            // because a brace added at runtime gets the default ~0 mask, and the raycast starts
            // inside your own capsule.
            workMask &= ~(1 << gameObject.layer);
        }

        /// <summary>
        /// Leave no trace. This component is switched off on remote players by PlayerNetworkSetup,
        /// and a disable mid-brace must not leave somebody planted, blind, or holding an
        /// instrument out at arm's length forever.
        /// </summary>
        private void OnDisable() => ReleaseImmediate();

        private void Update()
        {
            if (input == null) return;

            if (IsBraced && !CanHoldBrace()) Unbrace();
            else if (!IsBraced && input.BracePressed) TryBrace();

            if (IsBraced) DriveCursor();

            _blend = Mathf.MoveTowards(_blend, IsBraced ? 1f : 0f,
                                       Time.deltaTime / Mathf.Max(0.01f, leanSeconds));

            ApplyLean();
            ApplyHand();
        }

        // ---------------------------------------------------------------- entry and exit

        /// <summary>
        /// You cannot brace on nothing, and you cannot brace empty handed. The raycast is the
        /// whole rule: where you stood and what you were looking at before you leaned in is the
        /// decision, and once it is made the plane is frozen.
        /// </summary>
        /// <summary>
        /// Whether a brace would take right now, and if not, why not in words.
        ///
        /// Side-effect free and safe to poll, so the HUD can show the live reason rather than
        /// leaving you to press the button and guess. A silent refusal is indistinguishable from
        /// a broken build.
        /// </summary>
        public bool CanBrace(out string reason, out RaycastHit hit)
        {
            hit = default;

            if (interactor == null || interactor.ViewSource == null)
            {
                reason = "not wired - run Probation > Setup > 4";
                return false;
            }

            if (carry == null || !carry.IsCarrying)
            {
                reason = "nothing in hand";
                return false;
            }

            if (carry.Carried.Kind != GrabKind.Tool)
            {
                reason = $"{carry.Carried.DisplayName} is not an instrument";
                return false;
            }

            if (locomotion != null && locomotion.IsDowned)
            {
                reason = "downed";
                return false;
            }

            Transform eye = interactor.ViewSource;
            if (!Physics.Raycast(eye.position, eye.forward, out hit, braceReach,
                                 workMask, QueryTriggerInteraction.Ignore))
            {
                reason = $"nothing within {braceReach:0.0} m";
                return false;
            }

            reason = null;
            return true;
        }

        private void TryBrace()
        {
            if (!CanBrace(out string why, out RaycastHit hit))
            {
                Explain(why);
                return;
            }

            Transform eye = interactor.ViewSource;

            PlaneNormal = hit.normal;
            WorkPlane = new Plane(hit.normal, hit.point);
            _planeOrigin = hit.point;

            // A basis that keeps left-right on screen meaning left-right on the surface. Falls
            // back to the camera's up when you are looking straight down a wall.
            _planeRight = Vector3.ProjectOnPlane(eye.right, PlaneNormal);
            if (_planeRight.sqrMagnitude < 1e-6f) _planeRight = Vector3.ProjectOnPlane(eye.up, PlaneNormal);
            _planeRight.Normalize();

            // Cross(right, normal), NOT Cross(normal, right) - the other order flips the vertical
            // axis, and pushing the mouse away from you would drag the blade back towards you.
            _planeUp = Vector3.Cross(_planeRight, PlaneNormal).normalized;

            _cursorOffset = Vector2.zero;
            Cursor = _planeOrigin;
            Instrument = carry.Carried;

            IsBraced = true;
            if (look != null) look.Suspended = true;
            if (locomotion != null) locomotion.Planted = true;
        }

        /// <summary>Only ever called on the frame the button went down, so this cannot spam.</summary>
        private void Explain(string why)
        {
            if (explainFailures) Debug.Log($"[Brace] not braced: {why}", this);
        }

        /// <summary>Everything that ends a brace other than letting go of the button.</summary>
        private bool CanHoldBrace()
        {
            if (!input.Brace) return false;
            if (carry == null || !carry.IsCarrying) return false;
            if (carry.Carried != Instrument) return false;          // swapped tools mid-brace
            if (locomotion != null && locomotion.IsDowned) return false;
            return true;
        }

        private void Unbrace()
        {
            IsBraced = false;
            Instrument = null;

            // Handed back the moment you release, not when the lean finishes - a quarter second
            // of dead controls on exit reads as input lag. The hand blends out in pivot-local
            // space, so it simply follows the camera home.
            if (look != null) look.Suspended = false;
            if (locomotion != null) locomotion.Planted = false;
        }

        private void ReleaseImmediate()
        {
            Unbrace();
            _blend = 0f;
            ApplyLean();
            ApplyHand();
        }

        // ---------------------------------------------------------------- the cursor

        private void DriveCursor()
        {
            // Mouse delta is already per-frame. A stick is a rate and needs deltaTime.
            float scale = input.LookIsPointer
                ? braceSensitivity
                : gamepadBraceSpeed * Time.deltaTime;

            _cursorOffset += input.Look * scale;
            _cursorOffset.x = Mathf.Clamp(_cursorOffset.x, -cursorExtent.x, cursorExtent.x);
            _cursorOffset.y = Mathf.Clamp(_cursorOffset.y, -cursorExtent.y, cursorExtent.y);

            Cursor = _planeOrigin + _planeRight * _cursorOffset.x + _planeUp * _cursorOffset.y;

            if (_pivot == null) return;

            // Point the instrument into the surface. Tools are authored working-end along +Z.
            Quaternion aim = Quaternion.LookRotation(-PlaneNormal, _planeUp);

            // Put the TIP on the cursor rather than the wrist, so a long instrument and a short
            // one are aimed the same way.
            Vector3 target = Cursor + PlaneNormal * handStandoff - aim * TipOffset();

            _handLocalTarget = _pivot.InverseTransformPoint(target);
            _handLocalTargetRot = Quaternion.Inverse(_pivot.rotation) * aim;
        }

        /// <summary>
        /// Where the working end sits relative to the instrument's origin, in metres, in the
        /// instrument's own rotational frame.
        ///
        /// Deliberately NOT InverseTransformPoint: instruments are scaled primitives, and that
        /// would hand back local units. A scalpel 0.28 long would report a tip half a metre out
        /// and the blade would be aimed a foot past the patient.
        /// </summary>
        private Vector3 TipOffset()
        {
            if (Instrument == null) return Vector3.zero;

            var tip = Instrument.GetComponentInChildren<ToolTip>();
            if (tip == null) return Vector3.zero;

            Vector3 world = tip.transform.position - Instrument.transform.position;
            return Quaternion.Inverse(Instrument.transform.rotation) * world;
        }

        // ---------------------------------------------------------------- output

        private void ApplyLean()
        {
            if (view == null) return;

            view.transform.localPosition = _camRestLocal + leanOffset * _blend;
            view.fieldOfView = Mathf.Lerp(_restFov, bracedFov, _blend);
        }

        private void ApplyHand()
        {
            if (carry == null || carry.Hand == null) return;

            // Everything is pivot-local, so at blend 0 the anchor is exactly where it was
            // authored - no drift accumulates over a hundred braces.
            carry.Hand.localPosition = Vector3.Lerp(_handRestLocal, _handLocalTarget, _blend);
            carry.Hand.localRotation = Quaternion.Slerp(_handRestLocalRot, _handLocalTargetRot, _blend);
        }

        private void OnDrawGizmosSelected()
        {
            if (!IsBraced) return;

            Gizmos.color = new Color(0.2f, 0.9f, 0.8f, 0.9f);
            Gizmos.DrawWireSphere(Cursor, 0.015f);

            Gizmos.color = new Color(0.2f, 0.9f, 0.8f, 0.25f);
            Vector3 x = _planeRight * cursorExtent.x;
            Vector3 y = _planeUp * cursorExtent.y;
            Gizmos.DrawLine(_planeOrigin - x - y, _planeOrigin + x - y);
            Gizmos.DrawLine(_planeOrigin + x - y, _planeOrigin + x + y);
            Gizmos.DrawLine(_planeOrigin + x + y, _planeOrigin - x + y);
            Gizmos.DrawLine(_planeOrigin - x + y, _planeOrigin - x - y);
        }
    }
}
