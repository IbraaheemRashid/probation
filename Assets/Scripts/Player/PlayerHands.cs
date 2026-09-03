using Probation.Surgery;
using UnityEngine;

namespace Probation.Player
{
    /// <summary>
    /// The bare hand, which is an instrument like any other - it just does one thing.
    ///
    /// Empty-handed, LMB on a wound holds pressure on it. It bleeds while you hold, and it stops
    /// the moment you let go.
    ///
    /// This is the first job in the game that costs a whole player, occupies a whole hand, and
    /// makes **zero progress**. That is not a shortcoming, it is the entire design: it is what
    /// makes four-player surgery need four players rather than being one surgeon with an audience,
    /// and it is the reason the solo answer later is a clamp that does the job worse rather than a
    /// separate mode.
    ///
    /// Local-only, like everything else in the testbed. Wounds are not networked yet.
    /// </summary>
    public class PlayerHands : MonoBehaviour
    {
        [SerializeField] private PlayerInputReader input;
        [SerializeField] private PlayerInteractor interactor;
        [SerializeField] private PlayerCarry carry;

        [Tooltip("How far you can reach to put a hand on something. Shorter than the interact reach - you have to actually be at the wound, not pointing at it from across the room.")]
        [SerializeField] private float reach = 1.6f;
        [Tooltip("How near the crosshair a wound has to be. Forgiving, because a wound is small and you are meant to be hurrying.")]
        [SerializeField] private float aimTolerance = 0.22f;

        /// <summary>The wound this intern currently has a hand on, or null.</summary>
        public Wound Pressing { get; private set; }

        private void Reset()
        {
            input = GetComponent<PlayerInputReader>();
            interactor = GetComponent<PlayerInteractor>();
            carry = GetComponent<PlayerCarry>();
        }

        private void Awake()
        {
            if (input == null) input = GetComponent<PlayerInputReader>();
            if (interactor == null) interactor = GetComponent<PlayerInteractor>();
            if (carry == null) carry = GetComponent<PlayerCarry>();
        }

        private void OnDisable() => Pressing = null;

        private void Update()
        {
            Pressing = null;

            if (input == null || !input.Attack) return;
            if (carry != null && carry.IsCarrying) return;          // both hands are not free
            if (interactor == null || interactor.ViewSource == null) return;

            Wound wound = Aimed();
            if (wound == null) return;

            Pressing = wound;

            // Re-asserted every frame rather than latched. A holder who walks away, is knocked
            // down or disconnects stops holding without anything having to notice.
            wound.HoldPressure();
        }

        /// <summary>
        /// The wound nearest the crosshair within arm's length.
        ///
        /// Deliberately not a raycast: a wound is a couple of centimetres across and sitting flat
        /// on a body you are leaning over, and demanding a pixel-perfect hit on one while somebody
        /// shouts at you is the kind of precision that reads as the game being broken.
        /// </summary>
        private Wound Aimed()
        {
            Transform eye = interactor.ViewSource;

            Wound best = null;
            float bestScore = float.PositiveInfinity;

            foreach (var wound in Wound.All)
            {
                if (wound == null || !wound.IsOpen) continue;

                Vector3 delta = wound.transform.position - eye.position;
                if (delta.magnitude > reach) continue;
                if (Vector3.Dot(delta, eye.forward) <= 0f) continue;      // behind you

                // Perpendicular distance from the view axis, in metres. eye.forward is unit, so
                // the cross product's magnitude is exactly how far off the crosshair it sits.
                float offAxis = Vector3.Cross(delta, eye.forward).magnitude;
                if (offAxis > aimTolerance) continue;

                if (offAxis >= bestScore) continue;

                bestScore = offAxis;
                best = wound;
            }

            return best;
        }
    }
}
