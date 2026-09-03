using System.Collections.Generic;
using Probation.Game;
using Probation.Player;
using Unity.Netcode;
using UnityEngine;

namespace Probation.Interaction
{
    /// <summary>
    /// How an object is picked up. See PATCHES-adjacent notes in Scripts/Player/README.md:
    /// an object has exactly one authority at all times, and these are the two ways to get
    /// multiple people interacting with one thing without ever having two owners.
    /// </summary>
    public enum GrabKind
    {
        /// <summary>
        /// Precision item - scalpel, forceps, retractor. Ownership moves to whoever holds it,
        /// because round-trip lag on a blade would gut the surgery minigames.
        /// </summary>
        Tool,

        /// <summary>
        /// Heavy or shared item - patient, corpse, gurney. The host keeps ownership forever and
        /// each grab is a spring force, so any number of people can haul one object and the
        /// physics resolves the tug-of-war. The object lagging behind your hand reads as weight.
        /// </summary>
        Heavy,
    }

    /// <summary>
    /// Anything an intern can pick up. The grab mechanics live in <see cref="PlayerCarry"/>;
    /// this declares what kind of thing it is and owns the networked state.
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(Rigidbody))]
    public class Grabbable : NetworkBehaviour, IInteractable
    {
        private const ulong Nobody = ulong.MaxValue;

        [Header("Identity")]
        [Tooltip("Shown on the interaction prompt and in incident log lines.")]
        [SerializeField] private string displayName = "object";
        [SerializeField] private GrabKind kind = GrabKind.Tool;
        [Tooltip("What procedure steps ask for by name, e.g. scalpel, forceps, retractor.")]
        [SerializeField] private string toolId = "";
        [Tooltip("Some species are harmed by metal. The manual does not mention this.")]
        [SerializeField] private bool isMetal = true;

        [Header("Handling")]
        [Tooltip("How much this slows you down while carried. 1 = both hands on a patient.")]
        [Range(0f, 1f)] [SerializeField] private float encumbrance = 0.15f;

        [Header("Heavy haul (ignored for tools)")]
        [SerializeField] private float haulSpring = 600f;
        [SerializeField] private float haulDamper = 40f;
        [SerializeField] private float maxHaulForce = 2500f;
        [Tooltip("Let go automatically once the object is dragged further than this from the hand.")]
        [SerializeField] private float haulBreakDistance = 3f;

        public string DisplayName => displayName;
        public GrabKind Kind => kind;
        public string ToolId => toolId;
        public bool IsMetal => isMetal;

        /// <summary>
        /// Used, and not yet through the steriliser. A dirty instrument fails procedure steps -
        /// this is the ward's washing up, and it is the chore that keeps everybody moving.
        /// </summary>
        public bool IsDirty => _dirty.Value;

        private readonly NetworkVariable<bool> _dirty = new();

        public void Soil()
        {
            if (IsServer && !string.IsNullOrEmpty(toolId)) _dirty.Value = true;
        }

        public void Clean()
        {
            if (IsServer) _dirty.Value = false;
        }

        /// <summary>Tools only: the client holding this, or ulong.MaxValue.</summary>
        public ulong HeldBy => _heldBy.Value;

        /// <summary>
        /// Whoever most recently had hands on this. Survives them letting go, because the
        /// consequences of shoving something usually arrive after you have let go of it.
        /// </summary>
        public ulong LastHandledBy { get; private set; } = ulong.MaxValue;
        public float Encumbrance => encumbrance;
        public float HaulBreakDistance => haulBreakDistance;

        /// <summary>Tools only: who is holding this, or Nobody.</summary>
        private readonly NetworkVariable<ulong> _heldBy = new(Nobody);

        public bool IsHeld => _heldBy.Value != Nobody;
        public bool IsHeldBy(ulong clientId) => _heldBy.Value == clientId;

        private Rigidbody _rb;

        /// <summary>Host-only. One entry per intern currently hauling this.</summary>
        private readonly List<Haul> _hauls = new();

        private readonly struct Haul
        {
            public readonly ulong ClientId;
            public readonly PlayerCarry Carry;
            public readonly Vector3 LocalPoint;

            public Haul(ulong clientId, PlayerCarry carry, Vector3 localPoint)
            {
                ClientId = clientId;
                Carry = carry;
                LocalPoint = localPoint;
            }
        }

        private void Awake() => _rb = GetComponent<Rigidbody>();

        /// <summary>
        /// While somebody else holds this, their NetworkTransform is the authority on where it
        /// is. Leaving it dynamic here means the local solver and the network state both write
        /// the same transform every tick, which reads as jitter.
        /// </summary>
        private void RefreshRemoteSimulation()
        {
            if (_rb == null || kind != GrabKind.Tool) return;

            bool someoneElseHasIt = IsHeld && !IsOwner;
            if (_rb.isKinematic == someoneElseHasIt) return;

            _rb.isKinematic = someoneElseHasIt;
        }

        /// <summary>
        /// Everything grabbable in the scene, so anybody asking "what is that intern holding?"
        /// does not have to search it. Same pattern as Patient.All and Gurney.All.
        /// </summary>
        public static readonly System.Collections.Generic.List<Grabbable> All = new();

        /// <summary>What this client is holding, if anything. Held state replicates, so this
        /// answers for remote players too - which is what makes another intern's hands readable.</summary>
        public static Grabbable HeldByClient(ulong clientId)
        {
            foreach (var grabbable in All)
                if (grabbable != null && grabbable.IsHeldBy(clientId)) return grabbable;

            return null;
        }

        public override void OnNetworkSpawn()
        {
            All.Add(this);

            _heldBy.OnValueChanged += (_, __) => RefreshRemoteSimulation();
            RefreshRemoteSimulation();

            // A client that drops mid-grab would otherwise leave a tool permanently "held" by
            // nobody, or a phantom spring pulling from where they used to be standing.
            if (IsServer) NetworkManager.OnClientDisconnectCallback += ForceRelease;
        }

        public override void OnNetworkDespawn()
        {
            All.Remove(this);

            if (IsServer && NetworkManager != null)
                NetworkManager.OnClientDisconnectCallback -= ForceRelease;
        }

        // ---------------------------------------------------------------- IInteractable

        public string Prompt => kind == GrabKind.Tool ? $"Pick up {displayName}" : $"Grab {displayName}";

        public bool CanInteract(PlayerInteractor interactor)
        {
            var carry = interactor != null ? interactor.Carry : null;
            if (carry == null || carry.IsCarrying) return false;

            // A tool is one pair of hands at a time. A heavy object is not.
            return kind != GrabKind.Tool || !IsHeld;
        }

        public void Interact(PlayerInteractor interactor) => interactor.Carry.TryGrab(this);

        // ---------------------------------------------------------------- grab / release

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestGrabRpc(Vector3 localPoint, RpcParams rpc = default)
        {
            ulong clientId = rpc.Receive.SenderClientId;

            if (kind == GrabKind.Tool)
            {
                if (IsHeld) return;                       // somebody beat them to it
                _heldBy.Value = clientId;
                LastHandledBy = clientId;
                NetworkObject.ChangeOwnership(clientId);
                IncidentLog.Record(clientId, $"picked up the {displayName}");
                return;
            }

            if (IndexOfHaul(clientId) >= 0) return;

            var carry = CarryOf(clientId);
            if (carry == null) return;

            LastHandledBy = clientId;
            _hauls.Add(new Haul(clientId, carry, localPoint));
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        public void RequestReleaseRpc(RpcParams rpc = default)
        {
            ulong clientId = rpc.Receive.SenderClientId;

            if (kind == GrabKind.Tool)
            {
                if (!IsHeldBy(clientId)) return;
                _heldBy.Value = Nobody;

                // Back to the host, so an unheld tool has a stable authority like any other
                // world object rather than belonging to whoever touched it last.
                NetworkObject.RemoveOwnership();
                return;
            }

            int index = IndexOfHaul(clientId);
            if (index >= 0) _hauls.RemoveAt(index);
        }

        /// <summary>Host-side cleanup so a disconnect does not leave a phantom grab.</summary>
        public void ForceRelease(ulong clientId)
        {
            if (!IsServer) return;

            if (kind == GrabKind.Tool)
            {
                if (IsHeldBy(clientId))
                {
                    _heldBy.Value = Nobody;
                    NetworkObject.RemoveOwnership();
                }
                return;
            }

            int index = IndexOfHaul(clientId);
            if (index >= 0) _hauls.RemoveAt(index);
        }

        // ---------------------------------------------------------------- heavy physics

        private void FixedUpdate()
        {
            // Heavy objects are simulated by the host and nobody else. Tools are moved by their
            // owner's joint, which needs nothing here.
            if (!IsServer || kind != GrabKind.Heavy || _hauls.Count == 0) return;

            for (int i = _hauls.Count - 1; i >= 0; i--)
            {
                Haul haul = _hauls[i];
                if (haul.Carry == null)
                {
                    _hauls.RemoveAt(i);
                    continue;
                }

                Vector3 hand = haul.Carry.HandPosition;
                Vector3 point = transform.TransformPoint(haul.LocalPoint);
                Vector3 delta = hand - point;

                // Both sides check this independently rather than the host telling the client.
                // The client's PlayerCarry is disabled on every machine but its owner's, and
                // firing an RPC into a disabled behaviour is not a thing to rely on.
                if (delta.sqrMagnitude > haulBreakDistance * haulBreakDistance)
                {
                    _hauls.RemoveAt(i);
                    continue;
                }

                // A damped spring from the grab point to the hand. Several of these at once is
                // what makes two people carrying one gurney work, and what makes pulling in
                // opposite directions fight you.
                Vector3 force = delta * haulSpring - _rb.GetPointVelocity(point) * haulDamper;
                _rb.AddForceAtPosition(Vector3.ClampMagnitude(force, maxHaulForce), point);
            }
        }

        // ---------------------------------------------------------------- helpers

        private int IndexOfHaul(ulong clientId)
        {
            for (int i = 0; i < _hauls.Count; i++)
                if (_hauls[i].ClientId == clientId) return i;
            return -1;
        }

        private static PlayerCarry CarryOf(ulong clientId)
        {
            // Server-side only, which is the one context where this lookup is reliable.
            var player = NetworkManager.Singleton.SpawnManager.GetPlayerNetworkObject(clientId);
            return player != null ? player.GetComponent<PlayerCarry>() : null;
        }
    }
}
