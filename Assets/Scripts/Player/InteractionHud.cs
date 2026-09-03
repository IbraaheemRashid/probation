using Probation.Interaction;
using Probation.Surgery;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Probation.Player
{
    /// <summary>
    /// A crosshair, a prompt, and - when you ask for it - the state behind both.
    ///
    /// This exists because "I cannot pick anything up" and "I cannot see what I am aiming at"
    /// look identical from behind the keyboard, and so do "the brace refused" and "the brace
    /// component is not on the player". Every line in the diagnostic panel is a question that
    /// otherwise costs a round trip to answer.
    ///
    /// It runs its own spherecast rather than reading PlayerInteractor.Focused, because Focused
    /// is only ever assigned once CanInteract has already passed - so it reports null both when
    /// you are aiming at nothing and when you are aiming straight at something that is refusing
    /// you, and telling those two apart is the entire job here.
    ///
    /// IMGUI, like NetworkBootstrap and the rest of the scaffolding, and equally temporary: the
    /// shipped game puts every readout on the instrument itself. The crosshair and the prompt are
    /// the two things that survive.
    /// </summary>
    public class InteractionHud : MonoBehaviour
    {
        [Tooltip("F1 toggles this at runtime.")]
        [SerializeField] private bool showDiagnostics = true;

        [Header("Crosshair")]
        [SerializeField] private float size = 4f;
        [SerializeField] private Color idle = new(1f, 1f, 1f, 0.35f);
        [SerializeField] private Color ready = new(0.35f, 0.95f, 0.85f, 0.95f);
        [SerializeField] private Color refused = new(0.95f, 0.55f, 0.3f, 0.95f);

        private Texture2D _dot;
        private GUIStyle _centred;

        // Probed once per frame in Update. OnGUI runs at least twice a frame (Layout, Repaint),
        // so casting from there would triple the physics work for no benefit.
        private PlayerInteractor _interactor;
        private PlayerCarry _carry;
        private PlayerBrace _brace;
        private PlayerHands _hands;

        private Collider _aim;
        private Grabbable _aimGrabbable;
        private float _aimDistance;
        private bool _aimCanTake;

        private void Awake()
        {
            _dot = new Texture2D(1, 1);
            _dot.SetPixel(0, 0, Color.white);
            _dot.Apply();
        }

        private void OnDestroy()
        {
            if (_dot != null) Destroy(_dot);
        }

        private void Update()
        {
            // Polled rather than read off Event.current: activeInputHandler is 1 (new Input System
            // only), and IMGUI key events are not dependable in that mode.
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f1Key.wasPressedThisFrame)
                showDiagnostics = !showDiagnostics;

            var local = PlayerNetworkSetup.Local;
            if (local == null)
            {
                _interactor = null;
                _carry = null;
                _brace = null;
                return;
            }

            _interactor = local.GetComponent<PlayerInteractor>();
            _carry = local.GetComponent<PlayerCarry>();
            _brace = local.GetComponent<PlayerBrace>();
            _hands = local.GetComponent<PlayerHands>();

            Probe();
        }

        private void Probe()
        {
            _aim = null;
            _aimGrabbable = null;
            _aimDistance = 0f;
            _aimCanTake = false;

            if (_interactor == null || _interactor.ViewSource == null) return;

            Transform eye = _interactor.ViewSource;
            if (!Physics.SphereCast(eye.position, _interactor.CastRadius, eye.forward,
                                    out RaycastHit hit, _interactor.Reach,
                                    _interactor.InteractMask, QueryTriggerInteraction.Collide))
                return;

            _aim = hit.collider;
            _aimDistance = hit.distance;
            _aimGrabbable = hit.collider.GetComponentInParent<Grabbable>();
            _aimCanTake = _aimGrabbable != null && _aimGrabbable.CanInteract(_interactor);
        }

        // ---------------------------------------------------------------- draw

        private void OnGUI()
        {
            if (_centred == null)
                _centred = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.UpperCenter };

            if (PlayerNetworkSetup.Local == null)
            {
                Panel("no local player - not connected, or NetworkConfig.PlayerPrefab is unset", 60f);
                return;
            }

            DrawCrosshair();
            DrawPrompts();
            if (showDiagnostics) DrawDiagnostics();
        }

        private void DrawCrosshair()
        {
            Color colour = idle;

            if (_brace != null && _brace.IsBraced) colour = refused;
            else if (_aimGrabbable != null) colour = _aimCanTake ? ready : refused;

            float half = size * 0.5f;
            var centre = new Rect(Screen.width * 0.5f - half, Screen.height * 0.5f - half, size, size);

            Color was = GUI.color;
            GUI.color = colour;
            GUI.DrawTexture(centre, _dot);
            GUI.color = was;
        }

        private void DrawPrompts()
        {
            GUILayout.BeginArea(new Rect(Screen.width * 0.5f - 240f, Screen.height * 0.5f + 16f, 480f, 70f));

            // What E would do.
            if (_aimCanTake)
                GUILayout.Label($"E   Pick up {_aimGrabbable.DisplayName}", _centred);
            else if (_carry != null && _carry.IsCarrying)
                GUILayout.Label($"E   Drop {_carry.Carried.DisplayName}", _centred);
            else if (_aimGrabbable != null)
                GUILayout.Label($"cannot take {_aimGrabbable.DisplayName}   " +
                                $"(held {_aimGrabbable.IsHeld})", _centred);

            // Holding pressure is the one job that shows no progress, so it has to say so - a
            // player with a hand on a wound needs to see that it is working.
            if (_hands != null && _hands.Pressing != null)
                GUILayout.Label("HOLDING PRESSURE   -   release to let it bleed", _centred);
            else if (_carry != null && !_carry.IsCarrying && Wound.OpenCount > 0)
                GUILayout.Label("LMB   Hold pressure on a wound", _centred);

            // What RMB would do, and why it would not.
            if (_brace == null)
                GUILayout.Label("no PlayerBrace on the player", _centred);
            else if (_brace.IsBraced)
                GUILayout.Label("BRACED   -   LMB to cut, release to look up", _centred);
            else if (_brace.CanBrace(out string why, out _))
                GUILayout.Label("RMB   Brace", _centred);
            else
                GUILayout.Label($"cannot brace: {why}", _centred);

            GUILayout.EndArea();
        }

        private void DrawDiagnostics()
        {
            GUILayout.BeginArea(new Rect(12f, 232f, 350f, 200f), GUI.skin.box);
            GUILayout.Label("DIAGNOSTICS   (F1 to hide)");
            GUILayout.Space(3f);

            var net = NetworkManager.Singleton;
            GUILayout.Label(net == null
                ? "net      no NetworkManager"
                : $"net      {(net.IsHost ? "host" : net.IsServer ? "server" : net.IsClient ? "client" : "offline")}   client {net.LocalClientId}");

            GUILayout.Label(_carry == null
                ? "hands    NO PlayerCarry - nothing can ever be taken"
                : $"hands    {(_carry.IsCarrying ? _carry.Carried.DisplayName : "empty")}");

            if (_interactor == null)
            {
                GUILayout.Label("aim      NO PlayerInteractor");
            }
            else if (_aim == null)
            {
                GUILayout.Label($"aim      nothing within {_interactor.Reach:0.0} m");
            }
            else
            {
                GUILayout.Label($"aim      {_aim.name}   {_aimDistance:0.00} m");

                if (_aimGrabbable == null)
                {
                    GUILayout.Label("  grab   not a Grabbable");
                }
                else
                {
                    GUILayout.Label($"  can    {_aimCanTake}   {_aimGrabbable.Kind}   held {_aimGrabbable.IsHeld}");

                    // The failure that is invisible from everywhere else: an RPC on an unspawned
                    // NetworkObject does nothing at all and reports nothing anywhere.
                    var netObj = _aimGrabbable.GetComponent<NetworkObject>();
                    GUILayout.Label(netObj == null
                        ? "  net    NO NetworkObject"
                        : $"  net    spawned {netObj.IsSpawned}   owner {netObj.OwnerClientId}");
                }
            }

            if (_brace == null) GUILayout.Label("brace    COMPONENT MISSING");
            else if (_brace.IsBraced) GUILayout.Label($"brace    braced   blend {_brace.Blend:0.00}");
            else GUILayout.Label($"brace    {(_brace.CanBrace(out string why, out _) ? "ready" : why)}");

            // If this climbs by one every frame while straying, merging is broken - that is the
            // number that tells you chaos from one mistake.
            int held = 0;
            foreach (var w in Wound.All) if (w != null && w.IsOpen && w.UnderPressure) held++;
            GUILayout.Label($"wounds   {Wound.OpenCount} open   {held} held" +
                            (_hands != null && _hands.Pressing != null ? "   (you)" : ""));

            GUILayout.EndArea();
        }

        private void Panel(string message, float height)
        {
            GUILayout.BeginArea(new Rect(12f, 232f, 350f, height), GUI.skin.box);
            GUILayout.Label(message);
            GUILayout.EndArea();
        }
    }
}
