using UnityEngine;

namespace Probation.Player
{
    /// <summary>
    /// Looks for an <see cref="IInteractable"/> under the crosshair and fires it on press.
    ///
    /// Deliberately a spherecast rather than a ray: in a room where four people are crowded
    /// round one table, a pixel-perfect ray is infuriating. A little forgiveness here is worth
    /// more than precision.
    /// </summary>
    public class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] private PlayerInputReader input;
        [Tooltip("Usually the Camera transform, so the cast follows the view rather than the body.")]
        [SerializeField] private Transform viewSource;
        [SerializeField] private LayerMask interactMask = ~0;
        [Tooltip("Point-and-pull range. Long enough to take an instrument off a bench across the ward - and out of a colleague's hand.")]
        [SerializeField] private float reach = 4.5f;
        [SerializeField] private float castRadius = 0.12f;

        /// <summary>What is currently under the crosshair, or null. Drive the HUD prompt from this.</summary>
        public IInteractable Focused { get; private set; }
        public Component FocusedComponent { get; private set; }

        public PlayerLocomotion Locomotion { get; private set; }

        /// <summary>This player's hands. Interactables route grabs through it.</summary>
        public PlayerCarry Carry { get; private set; }

        /// <summary>Where this intern is looking. Used by held instruments that need to aim.</summary>
        public Transform ViewSource => viewSource;

        private void Reset()
        {
            input = GetComponent<PlayerInputReader>();
            Camera cam = GetComponentInChildren<Camera>();
            viewSource = cam != null ? cam.transform : null;
        }

        private void Awake()
        {
            if (input == null) input = GetComponent<PlayerInputReader>();
            Locomotion = GetComponent<PlayerLocomotion>();
            Carry = GetComponent<PlayerCarry>();
            if (viewSource == null)
            {
                Camera cam = GetComponentInChildren<Camera>();
                if (cam != null) viewSource = cam.transform;
            }
        }

        private void Update()
        {
            RefreshFocus();

            if (input != null && input.InteractPressed && Focused != null && Focused.CanInteract(this))
                Focused.Interact(this);
        }

        private void RefreshFocus()
        {
            Focused = null;
            FocusedComponent = null;
            if (viewSource == null) return;

            if (!Physics.SphereCast(viewSource.position, castRadius, viewSource.forward,
                                    out RaycastHit hit, reach, interactMask,
                                    QueryTriggerInteraction.Collide))
                return;

            // GetComponentInParent so a collider on a child mesh still resolves to its owner.
            IInteractable found = hit.collider.GetComponentInParent<IInteractable>();
            if (found == null || !found.CanInteract(this)) return;

            Focused = found;
            FocusedComponent = found as Component;
        }

        private void OnDrawGizmosSelected()
        {
            if (viewSource == null) return;
            Gizmos.color = Focused != null ? Color.green : Color.grey;
            Gizmos.DrawRay(viewSource.position, viewSource.forward * reach);
            Gizmos.DrawWireSphere(viewSource.position + viewSource.forward * reach, castRadius);
        }
    }
}
