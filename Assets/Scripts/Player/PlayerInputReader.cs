using UnityEngine;
using UnityEngine.InputSystem;

namespace Probation.Player
{
    /// <summary>
    /// The single place the rest of the player reads input from. Nothing else touches the
    /// Input System directly, so when netcode lands the only thing a remote player needs is
    /// this component disabled.
    /// </summary>
    public class PlayerInputReader : MonoBehaviour
    {
        [Tooltip("Drag Assets/InputSystem_Actions.inputactions in here.")]
        [SerializeField] private InputActionAsset actionAsset;
        [SerializeField] private string actionMapName = "Player";

        public Vector2 Move { get; private set; }
        public Vector2 Look { get; private set; }
        public bool Sprint { get; private set; }
        public bool Crouch { get; private set; }
        public bool Attack { get; private set; }

        /// <summary>
        /// True while Brace is held. The one input that changes what the mouse means: unbraced it
        /// is your head, braced it is your hands. Nothing else in the game does two jobs.
        /// </summary>
        public bool Brace { get; private set; }
        public bool BracePressed { get; private set; }
        public bool BraceReleased { get; private set; }

        /// <summary>True only on the frame Interact went down.</summary>
        public bool InteractPressed { get; private set; }

        /// <summary>True while Interact is down. Drives hold-to-carry.</summary>
        public bool InteractHeld { get; private set; }

        /// <summary>True only on the frame Interact came back up.</summary>
        public bool InteractReleased { get; private set; }

        /// <summary>
        /// True when Look last came from a mouse. Mouse delta is already per-frame pixels and
        /// must NOT be multiplied by deltaTime; a stick is a -1..1 value and must be.
        /// </summary>
        public bool LookIsPointer { get; private set; }

        /// <summary>True only on the frame the free-cursor key went down. Read by CursorLock.</summary>
        public bool FreeCursorPressed { get; private set; }

        /// <summary>Whether this reader's own action map is live. Shown in the F3 overlay.</summary>
        public bool ActionsEnabled => _map != null && _map.enabled;

        // Jump is pressed during Update but consumed during FixedUpdate, so a tap that lands
        // between two physics ticks would be lost if we polled it. Latch the timestamp instead.
        public float JumpPressedAt { get; private set; } = float.NegativeInfinity;
        public void ConsumeJump() => JumpPressedAt = float.NegativeInfinity;

        private InputActionAsset _ownCopy;
        private InputActionMap _map;
        private InputAction _move, _look, _sprint, _crouch, _jump, _interact, _attack, _brace;
        private InputAction _freeCursor;

        private void Awake()
        {
            if (actionAsset == null)
            {
                Debug.LogError($"{nameof(PlayerInputReader)} on {name} has no action asset assigned.", this);
                enabled = false;
                return;
            }

            // Every player object on this machine points at the SAME InputActionAsset, which
            // means they share one action map instance. Enabling and disabling that map from
            // per-instance OnEnable/OnDisable is then a global switch: a remote player spawning
            // and being gated off calls Disable() on the map the LOCAL player is reading from,
            // and local input dies. Symptom is "only the newest player can move".
            //
            // Clone it so each reader owns its own map and can only ever affect itself.
            _ownCopy = Instantiate(actionAsset);
            _ownCopy.name = $"{actionAsset.name} ({name})";

            _map = _ownCopy.FindActionMap(actionMapName, throwIfNotFound: true);
            _move = _map.FindAction("Move", throwIfNotFound: true);
            _look = _map.FindAction("Look", throwIfNotFound: true);
            _sprint = _map.FindAction("Sprint", throwIfNotFound: true);
            _crouch = _map.FindAction("Crouch", throwIfNotFound: true);
            _jump = _map.FindAction("Jump", throwIfNotFound: true);
            _interact = _map.FindAction("Interact", throwIfNotFound: true);
            _attack = _map.FindAction("Attack", throwIfNotFound: true);
            _brace = _map.FindAction("Brace", throwIfNotFound: true);

            // Not throwIfNotFound, unlike everything above it. This action was added after the
            // asset shipped, so a project with an older InputSystem_Actions is missing it - and
            // the cost of that should be "no free-cursor key", not a player that cannot move.
            _freeCursor = _map.FindAction("FreeCursor", throwIfNotFound: false);
            if (_freeCursor == null)
                Debug.LogWarning($"[Probation] No 'FreeCursor' action in the '{actionMapName}' map. " +
                                 "F1 will not release the cursor. Add a Button action named " +
                                 "FreeCursor bound to <Keyboard>/f1.", this);
        }

        private void OnEnable() => _map?.Enable();

        private void OnDestroy()
        {
            if (_ownCopy != null) Destroy(_ownCopy);
        }

        private void OnDisable()
        {
            _map?.Disable();
            Move = Look = Vector2.zero;
            Sprint = Crouch = Attack = false;
            InteractPressed = InteractHeld = InteractReleased = false;
            Brace = BracePressed = BraceReleased = false;
            FreeCursorPressed = false;
        }

        private void Update()
        {
            // Read before the free-cursor gate below, or the key that let the cursor go would be
            // the one input that could not take it back.
            FreeCursorPressed = _freeCursor != null && _freeCursor.WasPressedThisFrame();

            // Pointing at a button is not playing. Report neutral for everything else while the
            // cursor is loose, so clicking "Invite friends" over a running shift cannot also
            // swing whatever is in your hands, and so the head does not follow the pointer.
            //
            // Zeroing Brace here is enough to unwind a brace cleanly: PlayerBrace unbraces on
            // any frame CanHoldBrace() is false, which reads this same flag.
            if (CursorLock.Freed)
            {
                Move = Look = Vector2.zero;
                Sprint = Crouch = Attack = false;
                InteractPressed = InteractHeld = InteractReleased = false;
                Brace = BracePressed = BraceReleased = false;

                // Latched, not polled - a jump caught on the frame the cursor came loose would
                // otherwise still be waiting when it went back.
                ConsumeJump();
                return;
            }

            Move = _move.ReadValue<Vector2>();
            Look = _look.ReadValue<Vector2>();
            LookIsPointer = _look.activeControl?.device is Pointer;

            Sprint = _sprint.IsPressed();
            Crouch = _crouch.IsPressed();
            Attack = _attack.IsPressed();
            InteractPressed = _interact.WasPressedThisFrame();
            InteractHeld = _interact.IsPressed();
            InteractReleased = _interact.WasReleasedThisFrame();

            Brace = _brace.IsPressed();
            BracePressed = _brace.WasPressedThisFrame();
            BraceReleased = _brace.WasReleasedThisFrame();

            if (_jump.WasPressedThisFrame())
                JumpPressedAt = Time.time;
        }
    }
}
