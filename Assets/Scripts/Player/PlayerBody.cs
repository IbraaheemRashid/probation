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
        // These poses are authored for how somebody else reads you, not for your own screen.
        //
        // One hand object has to serve two jobs that want opposite things. Third person wants
        // anatomy - arms down when free, so an intern across the theatre reads as available.
        // First person wants information, and "my own hands are empty" is not information: you
        // already know. Compromising between the two gives a pose that is wrong for other people
        // AND barely on your screen, which is exactly what the previous version did.
        //
        // So the pose stays anatomical, and the owner's hands are simply hidden while idle. You
        // see your hands when they are doing something and not otherwise; everybody else sees
        // arms that go up and down.
        [Header("Hand pose, relative to the camera pivot")]
        [Tooltip("Empty hands, at your sides. Deliberately outside your own view - hidden for the owner anyway.")]
        [SerializeField] private Vector3 restHand = new(0.34f, -0.5f, 0.1f);
        [Tooltip("How far either side of the held object each hand sits. The grip position itself comes from the HandAnchor.")]
        [SerializeField] private float graspSpread = 0.15f;
        [SerializeField] private float handEase = 12f;

        private Transform _pivot;
        private Transform _handAnchor;
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
            _handAnchor = _pivot != null ? _pivot.Find("HandAnchor") : null;

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
            _isOwner = _net.IsOwner;
            if (!_isOwner) return;

            SetVisible(_torso, false);
            SetVisible(_skull, false);
            ShowOwnHands(false);
        }

        private void ShowOwnHands(bool visible)
        {
            _handsShown = visible;
            SetVisible(_leftHand, visible);
            SetVisible(_rightHand, visible);
        }

        private static void SetVisible(Transform part, bool visible)
        {
            if (part == null) return;

            var renderer = part.GetComponent<Renderer>();
            if (renderer != null) renderer.enabled = visible;
        }

        private bool _dressed;
        private bool _isOwner;
        private bool _handsShown = true;

        private void Paint(Color colour)
        {
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColour, colour);
            block.SetColor(LegacyColour, colour);

            foreach (var renderer in GetComponentsInChildren<Renderer>(true))
                renderer.SetPropertyBlock(block);
        }

        private static readonly int BaseColour = Shader.PropertyToID("_BaseColor");
        private static readonly int LegacyColour = Shader.PropertyToID("_Color");

        // ---------------------------------------------------------------- hands

        private void LateUpdate()
        {
            if (!_dressed && _net != null && _net.IsSpawned) Dress();

            HandsFull = IsCarrying();

            // Your own hands appear only while they are doing something. Idle hands would be a
            // viewmodel telling you a fact you already have, in exchange for two blocks sitting
            // in the corners of the screen all night. Everybody else sees them the whole time.
            if (_isOwner && HandsFull != _handsShown) ShowOwnHands(HandsFull);

            // LateUpdate so look and locomotion have already moved the pivot this frame.
            float t = 1f - Mathf.Exp(-handEase * Time.deltaTime);

            Ease(_rightHand, HandsFull ? Grasp(+1f) : restHand, t);
            Ease(_leftHand, HandsFull ? Grasp(-1f) : new Vector3(-restHand.x, restHand.y, restHand.z), t);
        }

        private static void Ease(Transform hand, Vector3 target, float t)
        {
            if (hand != null) hand.localPosition = Vector3.Lerp(hand.localPosition, target, t);
        }

        /// <summary>
        /// Where a hand goes when it is full: to either side of whatever is actually being held.
        ///
        /// Derived from the HandAnchor rather than authored next to it, because the two drifting
        /// apart is exactly the bug this replaces - the anchor sat out at x 0.25 while the hands
        /// bracketed the centre line at 0.17, so a carried tool floated past the outside of the
        /// right hand instead of between them. One source of truth means that cannot recur, and
        /// moving the anchor now moves the grip with it.
        /// </summary>
        private Vector3 Grasp(float side)
        {
            Vector3 held = _handAnchor != null ? _handAnchor.localPosition : FallbackGrasp;
            return held + new Vector3(side * graspSpread, 0f, 0f);
        }

        private static readonly Vector3 FallbackGrasp = new(0.08f, -0.22f, 0.44f);

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
