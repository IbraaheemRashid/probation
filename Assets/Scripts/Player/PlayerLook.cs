using UnityEngine;

namespace Probation.Player
{
    /// <summary>
    /// Owns the entire view rotation. Lives on the CameraPivot child, NOT on the rigidbody root.
    ///
    /// The physics body deliberately never rotates (see PlayerLocomotion): a capsule is
    /// rotationally symmetric, so its yaw carries no information, and keeping it frozen means
    /// look input never has to be routed through the physics tick. Look therefore runs in
    /// Update at full framerate and stays sharp regardless of the fixed timestep.
    /// </summary>
    public class PlayerLook : MonoBehaviour
    {
        [SerializeField] private PlayerInputReader input;

        [Header("Sensitivity")]
        [Tooltip("Degrees per mouse count. ~0.05-0.15 is a normal range.")]
        [SerializeField] private float mouseSensitivity = 0.08f;
        [Tooltip("Degrees per second at full stick deflection.")]
        [SerializeField] private float gamepadSensitivity = 200f;
        [SerializeField] private bool invertY;

        [Header("Limits")]
        [SerializeField] private float minPitch = -85f;
        [SerializeField] private float maxPitch = 85f;


        /// <summary>Current view yaw in degrees. Locomotion uses this as its movement basis.</summary>
        public float Yaw { get; private set; }
        public float Pitch { get; private set; }

        /// <summary>
        /// Set by <see cref="PlayerBrace"/>. While true the mouse is driving an instrument instead
        /// of the head, and this component must not touch the pivot.
        ///
        /// Yaw and Pitch keep their last values rather than being zeroed, so unbracing returns you
        /// to exactly the view you leaned in from - and Locomotion's movement basis, which reads
        /// Yaw, stays correct throughout.
        /// </summary>
        public bool Suspended { get; set; }

        /// <summary>Yaw-only rotation. Multiply a local move vector by this to get world space.</summary>
        public Quaternion YawRotation => Quaternion.Euler(0f, Yaw, 0f);

        /// <summary>Flattened forward, safe to use for movement on any slope.</summary>
        public Vector3 FlatForward => YawRotation * Vector3.forward;
        public Vector3 FlatRight => YawRotation * Vector3.right;

        private void Reset() => input = GetComponentInParent<PlayerInputReader>();

        private void Awake()
        {
            if (input == null) input = GetComponentInParent<PlayerInputReader>();

            // Seed from whatever the pivot was authored at so the prefab's facing is respected.
            Vector3 e = transform.localEulerAngles;
            Yaw = e.y;
            Pitch = NormalizeAngle(e.x);
        }

        // Cursor locking lives in CursorLock so that disabling this component on a remote
        // player cannot release the local player's cursor.

        private void Update()
        {
            if (input == null || Suspended) return;

            Vector2 look = input.Look;

            // Mouse delta is already a per-frame value. A stick is a rate and needs deltaTime.
            float scale = input.LookIsPointer
                ? mouseSensitivity
                : gamepadSensitivity * Time.deltaTime;

            Yaw += look.x * scale;
            Pitch += (invertY ? look.y : -look.y) * scale;

            Yaw = Mathf.Repeat(Yaw, 360f);
            Pitch = Mathf.Clamp(Pitch, minPitch, maxPitch);

            // The pivot is a child of a root that never rotates, so local == world here.
            transform.localRotation = Quaternion.Euler(Pitch, Yaw, 0f);
        }

        private static float NormalizeAngle(float degrees)
        {
            degrees %= 360f;
            return degrees > 180f ? degrees - 360f : degrees;
        }
    }
}
