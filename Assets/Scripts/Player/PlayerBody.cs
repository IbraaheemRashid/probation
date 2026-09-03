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
        [Tooltip("Hands full. Drawn in around whatever is at the HandAnchor, and in view of the owner.")]
        [SerializeField] private Vector3 busyHand = new(0.17f, -0.19f, 0.48f);
        [SerializeField] private float handEase = 12f;
        [Tooltip("How far a hand will follow the thing it is holding before it stops reaching. Keeps a dragging gurney, or a desynced remote, from throwing an arm across the room.")]
        [SerializeField] private float maxReach = 0.95f;

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
                             new Vector3(-restHand.x, restHand.y, restHand.z), Vector3.one * HandSize);
            _rightHand = Part(PrimitiveType.Cube, "Hand R", headParent,
                              restHand, Vector3.one * HandSize);
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

            _held = HeldObject();
            HandsFull = _held != null || PressingSomething();

            // Your own hands appear only while they are doing something. Idle hands would be a
            // viewmodel telling you a fact you already have, in exchange for two blocks sitting
            // in the corners of the screen all night. Everybody else sees them the whole time.
            if (_isOwner && HandsFull != _handsShown) ShowOwnHands(HandsFull);

            // LateUpdate so look and locomotion have already moved the pivot this frame.
            float t = 1f - Mathf.Exp(-handEase * Time.deltaTime);
            Vector3 right = busyHand, left = new(-busyHand.x, busyHand.y, busyHand.z);

            if (TryWorkPoint(out Vector3 work))
            {
                // A tool is one hand, with the other steadying nearby. Anything Heavy is a
                // two-hand job by definition - it is why handsRequired exists at all - so both go
                // on it, spread apart, and a gurney being pushed reads as being pushed.
                bool twoHanded = _held != null && _held.Kind != GrabKind.Tool;

                right = twoHanded ? work + Vector3.right * 0.13f : work;
                left = twoHanded ? work + Vector3.left * 0.13f : new Vector3(-busyHand.x, busyHand.y, busyHand.z);
            }
            else if (!HandsFull)
            {
                right = restHand;
                left = new Vector3(-restHand.x, restHand.y, restHand.z);
            }

            if (_rightHand != null)
                _rightHand.localPosition = Vector3.Lerp(_rightHand.localPosition, right, t);

            if (_leftHand != null)
                _leftHand.localPosition = Vector3.Lerp(_leftHand.localPosition, left, t);
        }

        /// <summary>
        /// Where the hands actually have to be, in pivot-local space.
        ///
        /// Deliberately the object's own transform rather than the HandAnchor it is heading
        /// towards. A carried tool is spring-driven at the anchor, not parented to it, so it lags
        /// and wobbles by design - a hand pinned to the anchor would sit next to the thing it is
        /// supposedly holding rather than on it, and the gap would be worst exactly when somebody
        /// is moving fast and you are most likely to be watching them.
        /// </summary>
        private bool TryWorkPoint(out Vector3 local)
        {
            local = Vector3.zero;
            if (_pivot == null) return false;

            Transform target = null;
            if (_held != null) target = _held.transform;
            else if (_isOwner && _hands != null && _hands.Pressing != null) target = _hands.Pressing.transform;

            if (target == null) return false;

            // Sit the hand ON the thing, not in it.
            //
            // An object's transform is its centre, and a hand parked there is a block buried
            // inside a scalpel - which reads as two things intersecting rather than as a grip.
            // Backing off along the line towards the wrist by the object's own extent puts the
            // hand against the surface facing you, which is where a hand would actually be.
            //
            // It scales with the object for free: a tool gets held near its middle because it is
            // small, and a gurney gets held at the near rail because that is where its surface
            // is. No per-object data, and it stays right if anything is ever resized.
            Vector3 centre = target.position;
            Vector3 toWrist = _pivot.position - centre;

            if (toWrist.sqrMagnitude > 1e-6f)
            {
                float distance = toWrist.magnitude;
                Vector3 dir = toWrist / distance;

                // Never back off further than the thing actually is. A gurney is nearly two
                // metres long, so standing at one end puts its centre closer to you than its own
                // extent - and the unclamped answer would place your hand somewhere behind your
                // own shoulders.
                float backoff = Mathf.Min(SurfaceOffset(target, dir) + HandSize * 0.5f,
                                          distance * 0.85f);

                centre += dir * backoff;
            }

            // Clamped so a heavy object dragging behind you does not pull an arm out of its
            // socket, and so a desynced remote never throws a hand across the room.
            local = Vector3.ClampMagnitude(_pivot.InverseTransformPoint(centre), maxReach);
            return true;
        }

        /// <summary>
        /// How far the object's surface is from its centre, in one direction.
        ///
        /// The standard support function for an axis-aligned box, which is what
        /// <see cref="Renderer.bounds"/> gives - so an elongated tool held end-on returns its
        /// long extent and the same tool held side-on returns its short one, without either
        /// needing to be authored.
        /// </summary>
        private static float SurfaceOffset(Transform target, Vector3 direction)
        {
            var renderer = target.GetComponentInChildren<Renderer>();
            if (renderer == null) return 0f;

            Vector3 extents = renderer.bounds.extents;
            return Mathf.Abs(extents.x * direction.x)
                 + Mathf.Abs(extents.y * direction.y)
                 + Mathf.Abs(extents.z * direction.z);
        }

        private const float HandSize = 0.12f;

        private Grabbable _held;

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
        private Grabbable HeldObject()
        {
            if (_net == null) return null;

            // Your own carry component knows about everything you have hold of, heavy objects
            // included. Everybody else's has to come off the wire.
            if (_isOwner) return _carry != null ? _carry.Carried : null;

            // And the wire only carries tools. Who is hauling a Heavy object lives in a plain
            // server-side List<Haul> on the Grabbable and is never replicated, so a remote intern
            // pushing a gurney still reads as having empty hands. Same gap as wounds, and worth
            // closing at the same time: both are cases of somebody visibly busy that nobody else
            // can see.
            return Grabbable.HeldByClient(_net.OwnerClientId);
        }

        /// <summary>
        /// Whether this intern has a hand on a wound.
        ///
        /// Owner-only, and it should not be - this is the purest case of somebody occupied and
        /// making no progress, which is exactly what the rest of the room needs to see. Wounds
        /// are not networked, so a remote client has nothing to read: neither the wound nor the
        /// hand on it exists on their machine.
        /// </summary>
        private bool PressingSomething() => _isOwner && _hands != null && _hands.Pressing != null;
    }
}
