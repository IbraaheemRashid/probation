using Unity.Netcode;
using UnityEngine;

namespace Probation.Player
{
    /// <summary>
    /// Decides which player object on this machine is yours, and switches everything else off.
    ///
    /// This is the whole of the client-authoritative model: the owner simulates its own intern
    /// and a NetworkTransform ships the result. Remote interns are puppets - no input, no
    /// physics, no camera. There is no prediction and no reconciliation because there is
    /// nothing to reconcile against.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class PlayerNetworkSetup : NetworkBehaviour
    {
        [SerializeField] private PlayerInputReader input;
        [SerializeField] private PlayerLook look;
        [SerializeField] private PlayerLocomotion locomotion;
        [SerializeField] private PlayerInteractor interactor;
        [SerializeField] private PlayerCarry carry;
        [SerializeField] private PlayerBrace brace;
        [SerializeField] private PlayerHands hands;
        [SerializeField] private CursorLock cursorLock;
        [SerializeField] private Camera playerCamera;
        [SerializeField] private AudioListener audioListener;
        [SerializeField] private Rigidbody body;

        [Header("Spawn")]
        [Tooltip("Interns are spaced along this axis so nobody materialises inside anybody.")]
        [SerializeField] private Vector3 spawnSpacing = new Vector3(1.5f, 0f, 0f);
        [SerializeField] private Vector3 spawnOrigin = new Vector3(0f, 1.0f, 0f);

        /// <summary>
        /// The local player, or null before one spawns.
        ///
        /// NGO's own answers to this question are inconsistent between host and client
        /// (LocalClient.PlayerObject and SpawnManager.GetLocalPlayerObject have both had
        /// versions where one side returns null), so we record it ourselves at the one moment
        /// we definitely know: our own spawn.
        /// </summary>
        public static PlayerNetworkSetup Local { get; private set; }

        /// <summary>
        /// Self-heal the brace.
        ///
        /// Every Player prefab authored before PlayerBrace existed is missing it, and the symptom
        /// is the worst kind: right mouse does nothing, and there is no error anywhere to say why.
        /// The editor setup step adds it too, but a player that silently cannot brace is not worth
        /// leaving to whether somebody remembered to re-run a menu item.
        ///
        /// PlayerBrace resolves all of its own references in Awake, so one added here is wired
        /// exactly as well as one authored on the prefab.
        /// </summary>
        private void Awake()
        {
            brace = Ensure(brace);
            hands = Ensure(hands);
        }

        private T Ensure<T>(T existing) where T : MonoBehaviour
        {
            if (existing != null) return existing;

            var found = GetComponent<T>();
            if (found != null) return found;

            var added = gameObject.AddComponent<T>();
            Debug.Log($"[Probation] Player prefab had no {typeof(T).Name} - added one at runtime. " +
                      "Run Probation > Setup > 4 to make it stick.", this);
            return added;
        }

        public override void OnNetworkSpawn()
        {
            bool mine = IsOwner;
            if (mine) Local = this;

            // NetworkManager spawns every player prefab at the same place. The second arrival
            // then appears inside the first, and two capsules resolving an overlap look exactly
            // like a controller that has stopped responding.
            //
            // Only the owner may move it: with owner-authoritative NetworkTransform, a position
            // written by anyone else is overwritten on the next tick anyway.
            if (mine)
            {
                transform.position = spawnOrigin + spawnSpacing * OwnerClientId;
                if (body != null) body.linearVelocity = Vector3.zero;
            }

            if (input != null) input.enabled = mine;
            if (look != null) look.enabled = mine;
            if (locomotion != null) locomotion.enabled = mine;
            if (interactor != null) interactor.enabled = mine;
            if (carry != null) carry.enabled = mine;

            // Gated with the rest of the input-driven components. A remote one is already inert
            // because its reader is disabled and reports no input, but it would still write that
            // player's camera and hand anchor every frame for nobody's benefit.
            if (brace != null) brace.enabled = mine;
            if (hands != null) hands.enabled = mine;

            // Only one of these may ever be live, or Unity logs an error every frame and
            // proximity voice picks the wrong ears.
            if (playerCamera != null) playerCamera.enabled = mine;
            if (audioListener != null) audioListener.enabled = mine;
            if (cursorLock != null) cursorLock.enabled = mine;

            if (body != null)
            {
                // A remote body is driven entirely by NetworkTransform. Leaving it dynamic means
                // the local physics solver and the network state fight over the same transform,
                // which reads as permanent jitter.
                body.isKinematic = !mine;
                body.interpolation = mine
                    ? RigidbodyInterpolation.Interpolate
                    : RigidbodyInterpolation.None;
            }

            gameObject.name = mine
                ? $"Player {OwnerClientId} (you)"
                : $"Player {OwnerClientId}";

            Debug.Log($"[Probation] Spawned {gameObject.name} - owner:{mine} " +
                      $"kinematic:{(body != null && body.isKinematic)} at {transform.position}");
        }

        public override void OnNetworkDespawn()
        {
            if (Local == this) Local = null;
            Debug.Log($"[Probation] Despawned {gameObject.name}.");
        }
    }
}
