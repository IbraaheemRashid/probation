using UnityEngine;

namespace Probation.Player
{
    /// <summary>
    /// Floating-capsule rigidbody controller.
    ///
    /// The capsule never touches the floor. A downward probe measures the gap and a damped
    /// spring holds the body at <see cref="standRideHeight"/> above whatever is below it. All
    /// movement is applied as force toward a goal velocity, never by writing to the transform.
    ///
    /// Why this and not CharacterController:
    ///   - a runaway gurney can shove you, and standing on one pushes it down
    ///   - carrying weight changes how the body handles for free (see Encumbrance)
    ///   - the float gap doubles as free step-over height for cables, kerbs and dropped tools
    ///   - knockdown is just unfreezing rotation for a second
    ///
    /// The body's rotation is frozen and meaningless. PlayerLook owns the view; this reads
    /// its yaw as the movement basis.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    public class PlayerLocomotion : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerInputReader input;
        [SerializeField] private PlayerLook look;
        [Tooltip("The CameraPivot transform. Gets raised and lowered when crouching.")]
        [SerializeField] private Transform cameraPivot;
        [Tooltip("Everything the player can stand on. Exclude the Player layer.")]
        [SerializeField] private LayerMask groundMask = ~0;

        [Header("Ride spring")]
        [Tooltip("Distance from this transform's origin down to the floor when standing.")]
        [SerializeField] private float standRideHeight = 0.95f;
        [SerializeField] private float crouchRideHeight = 0.65f;
        [Tooltip("How far past the ride height we still look for ground.")]
        [SerializeField] private float probeExtra = 0.30f;
        [SerializeField] private float probeRadius = 0.20f;
        [Tooltip("N per metre of error. Stiffer = snappier, but too stiff oscillates at 50Hz.")]
        [SerializeField] private float rideSpring = 45000f;
        [Tooltip("Critical damping for mass 70 at this stiffness is ~2900.")]
        [SerializeField] private float rideDamper = 3600f;

        [Header("Capsule")]
        [SerializeField] private float standCapsuleHeight = 1.4f;
        [SerializeField] private float crouchCapsuleHeight = 0.8f;
        [SerializeField] private float standEyeHeight = 0.65f;
        [SerializeField] private float crouchEyeHeight = 0.35f;
        [SerializeField] private float crouchBlendSpeed = 10f;

        [Header("Speed (m/s)")]
        [SerializeField] private float walkSpeed = 4.2f;
        [SerializeField] private float sprintSpeed = 6.8f;
        [SerializeField] private float crouchSpeed = 1.8f;

        [Header("Acceleration (m/s^2)")]
        [SerializeField] private float groundAcceleration = 65f;
        [SerializeField] private float airAcceleration = 14f;

        [Header("Jump")]
        [SerializeField] private float jumpHeight = 0.85f;
        [SerializeField] private float coyoteTime = 0.12f;
        [SerializeField] private float jumpBuffer = 0.12f;
        [Tooltip("The spring would yank you straight back down, so mute it briefly after a jump.")]
        [SerializeField] private float springMuteAfterJump = 0.2f;
        [Tooltip("Extra gravity while falling. 1 = realistic, higher = less floaty.")]
        [SerializeField] private float fallGravityMultiplier = 1.25f;

        [Header("Slopes")]
        [SerializeField] private float maxSlopeAngle = 50f;

        [Header("Stamina")]
        [Tooltip("Seconds of sprinting from full, unencumbered.")]
        [SerializeField] private float sprintSeconds = 6f;
        [Tooltip("Seconds to refill from empty once you stop sprinting.")]
        [SerializeField] private float recoverSeconds = 5f;
        [Tooltip("Pause before stamina starts coming back.")]
        [SerializeField] private float recoverDelay = 0.7f;
        [Tooltip("Drain multiplier at full encumbrance. Hauling a patient at a run is expensive.")]
        [SerializeField] private float encumberedDrain = 2.6f;

        [Header("Encumbrance")]
        [Tooltip("Speed multiplier at Encumbrance = 1 (both hands on a patient).")]
        [SerializeField] private float encumberedSpeedMultiplier = 0.45f;
        [SerializeField] private float encumberedAccelMultiplier = 0.35f;

        /// <summary>
        /// 0 = empty handed, 1 = hauling something you should not be hauling alone.
        /// Set by the carry system. This is the hook that makes moving a patient feel like work.
        /// </summary>
        public float Encumbrance { get; set; }

        /// <summary>
        /// 0 to 1. Sprinting spends it, standing still refills it, and carrying a patient
        /// spends it far faster.
        ///
        /// Walking is deliberately free - this is meant to make crossing a six bed ward at a
        /// run a decision, not to put a leash on ordinary movement. PEAK can afford a constant
        /// drain because climbing <em>is</em> the game; here traversal is connective tissue,
        /// so the cost only bites when you are in a hurry or carrying somebody.
        /// </summary>
        public float Stamina { get; private set; } = 1f;

        /// <summary>Too tired to run. Recovers, but not instantly.</summary>
        public bool Winded { get; private set; }

        public bool IsGrounded { get; private set; }
        public bool IsCrouching { get; private set; }
        public bool IsDowned => Time.time < _downedUntil;
        public Vector3 Velocity => _rb.linearVelocity;
        public Rigidbody Body => _rb;

        private Rigidbody _rb;
        private CapsuleCollider _capsule;

        private float _lastGroundedTime = float.NegativeInfinity;
        private float _springMutedUntil = float.NegativeInfinity;
        private float _downedUntil = float.NegativeInfinity;
        private float _crouchBlend;              // 0 = standing, 1 = crouched
        private Vector3 _groundNormal = Vector3.up;
        private Rigidbody _groundBody;
        private Vector3 _groundPoint;
        private float _groundDistance = float.PositiveInfinity;

        private float CurrentRideHeight => Mathf.Lerp(standRideHeight, crouchRideHeight, _crouchBlend);

        private void Reset()
        {
            input = GetComponent<PlayerInputReader>();
            look = GetComponentInChildren<PlayerLook>();
            cameraPivot = look != null ? look.transform : null;
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _capsule = GetComponent<CapsuleCollider>();

            if (input == null) input = GetComponent<PlayerInputReader>();
            if (look == null) look = GetComponentInChildren<PlayerLook>();
            if (cameraPivot == null && look != null) cameraPivot = look.transform;

            // Damping is handled explicitly by the spring and the acceleration model. Letting
            // Unity also apply linear damping makes every value below lie about what it does.
            _rb.freezeRotation = true;
            _rb.linearDamping = 0f;
            _rb.angularDamping = 0.05f;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rb.useGravity = true;
        }

        private void Update()
        {
            UpdateStamina();

            // Crouch blend is visual as much as physical, so drive it at framerate.
            bool wantsCrouch = input != null && input.Crouch && !IsDowned;
            if (!wantsCrouch && IsCrouching && !HasHeadroom()) wantsCrouch = true;

            IsCrouching = wantsCrouch;
            _crouchBlend = Mathf.MoveTowards(_crouchBlend, wantsCrouch ? 1f : 0f,
                                             crouchBlendSpeed * Time.deltaTime);

            ApplyCapsuleShape();
        }

        private float _spentAt;

        private void UpdateStamina()
        {
            bool wantsToSprint = input != null && input.Sprint
                              && !IsCrouching && !IsDowned
                              && new Vector2(Velocity.x, Velocity.z).sqrMagnitude > 1f;

            if (wantsToSprint && !Winded)
            {
                float drain = Mathf.Lerp(1f, encumberedDrain, Mathf.Clamp01(Encumbrance));
                Stamina -= Time.deltaTime / sprintSeconds * drain;
                _spentAt = Time.time;

                if (Stamina <= 0f)
                {
                    Stamina = 0f;
                    Winded = true;
                }
                return;
            }

            if (Time.time - _spentAt < recoverDelay) return;

            Stamina = Mathf.Min(1f, Stamina + Time.deltaTime / recoverSeconds);

            // Deliberately not the instant you have a drop of stamina - being winded should
            // cost you the next few seconds, which is when the Code fires.
            if (Winded && Stamina > 0.35f) Winded = false;
        }

        private void FixedUpdate()
        {
            ProbeGround();

            if (IsDowned)
            {
                // No spring, no steering. You are luggage until you get up.
                return;
            }

            if (IsGrounded && Time.time >= _springMutedUntil)
                ApplyRideSpring();

            ApplyMovement();
            TryJump();
            ApplyFallGravity();
        }

        // ---------------------------------------------------------------- ground

        private void ProbeGround()
        {
            _groundBody = null;
            _groundNormal = Vector3.up;
            IsGrounded = false;
            _groundDistance = float.PositiveInfinity;

            float castDistance = CurrentRideHeight + probeExtra - probeRadius;
            if (castDistance <= 0f) return;

            if (!Physics.SphereCast(transform.position, probeRadius, Vector3.down,
                                    out RaycastHit hit, castDistance,
                                    groundMask, QueryTriggerInteraction.Ignore))
                return;

            // Too steep to stand on: let the body slide off instead of springing up it.
            if (Vector3.Angle(hit.normal, Vector3.up) > maxSlopeAngle) return;

            _groundDistance = hit.distance + probeRadius;
            _groundNormal = hit.normal;
            _groundBody = hit.rigidbody;
            _groundPoint = hit.point;
            IsGrounded = true;
            _lastGroundedTime = Time.time;
        }


        private void ApplyRideSpring()
        {
            Vector3 rayDir = Vector3.down;

            float selfVel = Vector3.Dot(rayDir, _rb.linearVelocity);
            float otherVel = _groundBody != null ? Vector3.Dot(rayDir, _groundBody.linearVelocity) : 0f;
            float relativeVel = selfVel - otherVel;

            float offset = _groundDistance - CurrentRideHeight;   // positive = floating too high
            float springForce = (offset * rideSpring) - (relativeVel * rideDamper);

            _rb.AddForce(rayDir * springForce);

            // Newton's third law, and the reason standing on a gurney sinks it.
            if (_groundBody != null)
                _groundBody.AddForceAtPosition(rayDir * -springForce, _groundPoint);
        }

        // ---------------------------------------------------------------- movement

        private void ApplyMovement()
        {
            Vector2 move = input != null ? input.Move : Vector2.zero;
            Vector3 wish = look != null
                ? look.FlatRight * move.x + look.FlatForward * move.y
                : transform.right * move.x + transform.forward * move.y;

            if (wish.sqrMagnitude > 1f) wish.Normalize();

            // Follow the floor rather than skiing up or launching off slopes.
            if (IsGrounded && wish.sqrMagnitude > 0.0001f)
            {
                Vector3 projected = Vector3.ProjectOnPlane(wish, _groundNormal);
                if (projected.sqrMagnitude > 0.0001f) wish = projected;
            }

            float speed = crouchSpeed;
            if (!IsCrouching)
            {
                bool sprinting = input != null && input.Sprint && !Winded && Stamina > 0f;
                speed = sprinting ? sprintSpeed : walkSpeed;
            }
            speed *= Mathf.Lerp(1f, encumberedSpeedMultiplier, Mathf.Clamp01(Encumbrance));

            Vector3 goalVelocity = wish * speed;

            // Inherit the motion of whatever we are riding, so a moving gurney carries you.
            if (_groundBody != null)
            {
                Vector3 carrier = _groundBody.linearVelocity;
                goalVelocity += new Vector3(carrier.x, 0f, carrier.z);
            }

            Vector3 current = _rb.linearVelocity;
            Vector3 error = goalVelocity - new Vector3(current.x, 0f, current.z);

            float accelCap = IsGrounded ? groundAcceleration : airAcceleration;
            accelCap *= Mathf.Lerp(1f, encumberedAccelMultiplier, Mathf.Clamp01(Encumbrance));

            Vector3 acceleration = Vector3.ClampMagnitude(error / Time.fixedDeltaTime, accelCap);
            _rb.AddForce(acceleration * _rb.mass);
        }

        private void TryJump()
        {
            if (input == null) return;

            bool buffered = Time.time - input.JumpPressedAt <= jumpBuffer;
            bool footing = Time.time - _lastGroundedTime <= coyoteTime;
            if (!buffered || !footing) return;

            input.ConsumeJump();
            _lastGroundedTime = float.NegativeInfinity;
            _springMutedUntil = Time.time + springMuteAfterJump;

            float launch = Mathf.Sqrt(2f * jumpHeight * Mathf.Abs(Physics.gravity.y));
            Vector3 v = _rb.linearVelocity;
            v.y = Mathf.Max(v.y, 0f) + launch;
            _rb.linearVelocity = v;
        }

        private void ApplyFallGravity()
        {
            if (_rb.linearVelocity.y >= 0f || fallGravityMultiplier <= 1f) return;
            _rb.AddForce(Physics.gravity * ((fallGravityMultiplier - 1f) * _rb.mass));
        }

        // ---------------------------------------------------------------- shape

        private void ApplyCapsuleShape()
        {
            float height = Mathf.Lerp(standCapsuleHeight, crouchCapsuleHeight, _crouchBlend);
            _capsule.height = height;
            _capsule.center = Vector3.zero;   // origin sits at the capsule's centre

            if (cameraPivot != null)
            {
                Vector3 p = cameraPivot.localPosition;
                p.y = Mathf.Lerp(standEyeHeight, crouchEyeHeight, _crouchBlend);
                cameraPivot.localPosition = p;
            }
        }

        private bool HasHeadroom()
        {
            float needed = standCapsuleHeight * 0.5f + (standRideHeight - CurrentRideHeight);
            return !Physics.SphereCast(transform.position, _capsule.radius * 0.95f, Vector3.up,
                                       out _, needed, groundMask, QueryTriggerInteraction.Ignore);
        }

        // ---------------------------------------------------------------- states

        /// <summary>
        /// Knock the intern over. Unfreezes rotation so the body actually tumbles, then locks
        /// it upright again on recovery. This is the hook for a gurney to the shins, a botched
        /// defib, or an Outbreak swipe.
        /// </summary>
        public void Knockdown(float duration, Vector3 impulse = default)
        {
            _downedUntil = Mathf.Max(_downedUntil, Time.time + duration);
            _rb.freezeRotation = false;
            if (impulse != default) _rb.AddForce(impulse, ForceMode.Impulse);
            CancelInvoke(nameof(Recover));
            Invoke(nameof(Recover), duration);
        }

        private void Recover()
        {
            _rb.freezeRotation = true;
            _rb.angularVelocity = Vector3.zero;
            transform.rotation = Quaternion.identity;
        }

        // ---------------------------------------------------------------- gizmos

        private void OnDrawGizmosSelected()
        {
            float ride = Application.isPlaying ? CurrentRideHeight : standRideHeight;
            Vector3 origin = transform.position;

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(origin, origin + Vector3.down * ride);
            Gizmos.DrawWireSphere(origin + Vector3.down * ride, 0.05f);

            Gizmos.color = new Color(1f, 0.5f, 0f, 0.6f);
            Gizmos.DrawWireSphere(origin + Vector3.down * (ride + probeExtra - probeRadius), probeRadius);
        }
    }
}
