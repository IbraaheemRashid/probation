using Probation.Interaction;
using Unity.Netcode;
using UnityEngine;

namespace Probation.Player
{
    /// <summary>
    /// Something to look at, and more importantly something to read.
    ///
    /// Until this existed the Player prefab had no renderer anywhere on it - four interns in a
    /// ward and not one of them could see another. In a game whose entire co-op design is
    /// "a whole player is standing there with both hands full and cannot come and help you",
    /// that is not a missing art pass, it is the mechanic being invisible.
    ///
    /// So this is a readability component rather than a character. Three things, in order of how
    /// much they matter:
    ///
    ///   1. Where somebody is - a coloured capsule, one colour per intern, matching the names
    ///      the end-of-shift review reads out.
    ///   2. Where they are looking - the head pitches with their camera, so you can tell across
    ///      a theatre whether somebody is watching the patient or the door.
    ///   3. Whether their hands are free - the single bit the whole design turns on.
    ///
    /// Built from primitives at runtime rather than authored, deliberately. The eventual model
    /// gets designed against what this turns out to need, not before.
    /// </summary>
    [RequireComponent(typeof(PlayerNetworkSetup))]
    public class PlayerBody : MonoBehaviour
    {
        // The camera sits exactly on the CameraPivot, so these are offsets from your own eye and
        // both of them have to stay inside a 70 degree frustum or you cannot see your own hands.
        // Arms genuinely at your sides is out of view by a mile: at (0.34, -0.5, 0.12) a hand is
        // 70 degrees off-axis horizontally and 76 vertically, which is why the first version of
        // this was invisible. HandAnchor at (0.25, -0.2, 0.45) is the proven in-view reference.
        [Header("Hand pose, relative to the camera pivot")]
        [Tooltip("Empty hands. Low and wide, but still in front of you - a real arms-down pose cannot be seen in first person.")]
        [SerializeField] private Vector3 restHand = new(0.30f, -0.24f, 0.42f);
        [Tooltip("Hands full. Drawn in around whatever is at the HandAnchor.")]
        [SerializeField] private Vector3 busyHand = new(0.17f, -0.19f, 0.48f);
        [SerializeField] private float handEase = 12f;

        private Transform _pivot;
        private Transform _torso;
        private Transform _skull;
        private Transform _leftHand;
        private Transform _rightHand;

        private NetworkObject _net;
        private PlayerCarry _carry;
        private PlayerHands _hands;

        /// <summary>
        /// True while this intern cannot pick anything else up.
        ///
        /// Derived rather than replicated: what somebody is holding is already a NetworkVariable
        /// on the Grabbable itself, so this reads correctly for remote players without adding a
        /// second copy of the same fact to the wire.
        /// </summary>
        public bool HandsFull { get; private set; }

        private void Awake()
        {
            _net = GetComponent<NetworkObject>();
            _carry = GetComponent<PlayerCarry>();
            _hands = GetComponent<PlayerHands>();
            _pivot = transform.Find("CameraPivot");

            Build();
        }

        // ---------------------------------------------------------------- the body

        private void Build()
        {
            var capsule = GetComponent<CapsuleCollider>();
            float height = capsule != null ? capsule.height : 1.8f;
            float radius = capsule != null ? capsule.radius : 0.3f;
            float centre = capsule != null ? capsule.center.y : 0f;

            // A Unity capsule primitive is 2 units tall and 1 across, so half the height.
            _torso = Part(PrimitiveType.Capsule, "Torso", transform,
                          new Vector3(0f, centre, 0f),
                          new Vector3(radius * 2f, height * 0.5f, radius * 2f));

            var headParent = _pivot != null ? _pivot : transform;

            // On the pivot, so it pitches with the camera. Somebody leaning over a patient reads
            // as leaning over a patient rather than as standing perfectly upright looking down.
            _skull = Part(PrimitiveType.Cube, "Head", headParent,
                          new Vector3(0f, 0f, 0.06f), Vector3.one * (radius * 1.05f));

            _leftHand = Part(PrimitiveType.Cube, "Hand L", headParent,
                             new Vector3(-restHand.x, restHand.y, restHand.z), Vector3.one * 0.12f);
            _rightHand = Part(PrimitiveType.Cube, "Hand R", headParent,
                              restHand, Vector3.one * 0.12f);
        }

        private static Transform Part(PrimitiveType shape, string name, Transform parent,
                                      Vector3 localPosition, Vector3 localScale)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = name;

            // The player already has one collider and wants exactly one. These are decoration.
            var collider = go.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = Shared();

            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;
            return go.transform;
        }

        /// <summary>
        /// One material for every part of every player.
        ///
        /// CreatePrimitive hands out the built-in render pipeline's default material, which in a
        /// URP project has no valid shader and renders as flat magenta. A MaterialPropertyBlock
        /// cannot rescue that - there is no shader left to set a property on - so the colour work
        /// below would have silently done nothing on top of an unmissable pink capsule.
        /// </summary>
        private static Material Shared()
        {
            if (_shared != null) return _shared;

            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");

            _shared = new Material(shader) { name = "PlayerBody (runtime)", hideFlags = HideFlags.DontSave };
            return _shared;
        }

        private static Material _shared;

        // ---------------------------------------------------------------- who is who

        /// <summary>
        /// One colour per intern, so "Intern 2" on the review screen is somebody you can picture.
        /// Deliberately cold and desaturated - the ward is meant to be the flat, dead-looking
        /// thing and the patients the only warm objects in it.
        /// </summary>
        private static readonly Color[] Interns =
        {
            new(0.45f, 0.62f, 0.78f),
            new(0.78f, 0.72f, 0.45f),
            new(0.55f, 0.75f, 0.60f),
            new(0.72f, 0.52f, 0.62f),
        };

        /// <summary>
        /// Colour and hide, once this player actually knows who owns it.
        ///
        /// Deliberately not in Start. A player prefab is instantiated and then spawned, and Start
        /// can run before the spawn does - at which point IsOwner is still false and OwnerClientId
        /// is meaningless. Doing this on the first frame after spawn instead is the difference
        /// between hiding your own head and spending the whole game looking at the inside of it.
        /// </summary>
        private void Dress()
        {
            _dressed = true;

            Paint(Interns[(int)(_net.OwnerClientId % (ulong)Interns.Length)]);

            // Your own torso and head sit inside your own camera. The hands stay visible: seeing
            // them go out in front of you as you pick something up is the cheapest possible
            // confirmation that the game registered it.
            if (!_net.IsOwner) return;

            Hide(_torso);
            Hide(_skull);
        }

        private bool _dressed;

        private void Paint(Color colour)
        {
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColour, colour);
            block.SetColor(LegacyColour, colour);

            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
                renderer.SetPropertyBlock(block);
        }

        private static void Hide(Transform part)
        {
            if (part == null) return;

            var renderer = part.GetComponent<Renderer>();
            if (renderer != null) renderer.enabled = false;
        }

        private static readonly int BaseColour = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColour = Shader.PropertyToID("_Color");

        // ---------------------------------------------------------------- hands

        private void LateUpdate()
        {
            if (!_dressed && _net != null && _net.IsSpawned) Dress();

            HandsFull = IsCarrying();

            // LateUpdate so look and locomotion have already moved the pivot this frame.
            Vector3 target = HandsFull ? busyHand : restHand;
            float t = 1f - Mathf.Exp(-handEase * Time.deltaTime);

            if (_rightHand != null)
                _rightHand.localPosition = Vector3.Lerp(_rightHand.localPosition, target, t);

            if (_leftHand != null)
            {
                var mirrored = new Vector3(-target.x, target.y, target.z);
                _leftHand.localPosition = Vector3.Lerp(_leftHand.localPosition, mirrored, t);
            }
        }

        /// <summary>
        /// Whether this intern's hands are occupied.
        ///
        /// The owner can just ask its own carry component. For everybody else it comes from the
        /// replicated held-by state on the Grabbable, which is why another player's hands read
        /// correctly on your screen at all.
        ///
        /// Holding pressure on a wound does not show up here yet, and it should - it is the
        /// purest example of a player who is busy and making no progress. Wounds are not
        /// networked, so there is currently nothing to read on a remote client.
        /// </summary>
        private bool IsCarrying()
        {
            if (_net == null) return false;

            if (_net.IsOwner)
            {
                if (_carry != null && _carry.IsCarrying) return true;
                if (_hands != null && _hands.Pressing != null) return true;
                return false;
            }

            return Grabbable.HeldByClient(_net.OwnerClientId) != null;
        }
    }
}
