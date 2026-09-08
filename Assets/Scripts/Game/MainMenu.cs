using Probation.Player;
using Unity.Netcode;
using UnityEngine;

namespace Probation.Game
{
    /// <summary>
    /// The title screen, and the only real interface this game has.
    ///
    /// Everything in-game is deliberately almost nothing - a crosshair, a prompt, a clock, and
    /// readouts that only exist while you are holding the tool they belong to. Menus are the
    /// exception and get to be an actual screen, because there is nothing diegetic about deciding
    /// to play.
    ///
    /// It also owns the moment before the night, which matters more than it looks: a shift that
    /// starts the instant the scene loads means the first thirty seconds are always spent alone
    /// while people are still joining, and the opening is the one part of a night that should be
    /// four people standing in a dock together.
    ///
    /// IMGUI, like every other readout here. There is no canvas anywhere in this project and this
    /// is not the change that should introduce one.
    /// </summary>
    public class MainMenu : MonoBehaviour
    {
        [SerializeField] private string title = "PROBATION";
        [SerializeField] private string tagline = "You are not qualified for this.";

        private GUIStyle _title, _tag, _button, _note;
        private SteamLobbyBootstrap _lobby;
        private NetworkBootstrap _direct;

        private void Awake()
        {
            _lobby = GetComponent<SteamLobbyBootstrap>();
            _direct = GetComponent<NetworkBootstrap>();
        }

        private void Update()
        {
            // Both of the other panels are debug affordances. They are welcome once the night is
            // running; they are not welcome on top of a title card.
            bool hide = ShouldDraw;
            if (_lobby != null) _lobby.PanelSuppressed = hide;
            if (_direct != null) _direct.PanelSuppressed = hide;

            // A menu you cannot click is not a menu. CursorLock is reference counted across every
            // player object on this machine, so nothing here can simply disable a component and
            // hope - it has to be overridden while the card is up and handed back after.
            if (hide == _releasedCursor) return;

            _releasedCursor = hide;
            if (hide) CursorLock.ForceRelease();
            else CursorLock.Restore();
        }

        private bool _releasedCursor;

        private void OnDisable()
        {
            if (_lobby != null) _lobby.PanelSuppressed = false;
            if (_direct != null) _direct.PanelSuppressed = false;

            if (_releasedCursor) CursorLock.Restore();
            _releasedCursor = false;
        }

        private bool ShouldDraw
        {
            get
            {
                var net = NetworkManager.Singleton;
                if (net == null || !net.IsListening) return true;          // not in a game yet

                var director = ShiftDirector.Instance;
                return director != null && director.Phase == ShiftPhase.Lobby;
            }
        }

        private void OnGUI()
        {
            if (!ShouldDraw) return;

            Styles();

            // A flat wash rather than a texture. The ward behind it is a greybox and letting it
            // show through slightly is more atmosphere than any background art would be yet.
            var full = new Rect(0f, 0f, Screen.width, Screen.height);
            Color was = GUI.color;
            GUI.color = new Color(0.02f, 0.03f, 0.05f, 0.88f);
            GUI.DrawTexture(full, Texture2D.whiteTexture);
            GUI.color = was;

            float w = 340f;
            var panel = new Rect(Screen.width * 0.5f - w * 0.5f, Screen.height * 0.28f, w, 300f);

            GUILayout.BeginArea(panel);

            GUILayout.Label(title, _title);
            GUILayout.Label(tagline, _tag);
            GUILayout.Space(28f);

            var net = NetworkManager.Singleton;
            bool listening = net != null && net.IsListening;

            if (!listening) DrawNotHosting();
            else DrawWaitingRoom(net);

            GUILayout.EndArea();
        }

        private void DrawNotHosting()
        {
            if (GUILayout.Button("HOST A SHIFT", _button, GUILayout.Height(38f)))
                _lobby?.HostLobby();

            GUILayout.Space(6f);
            GUILayout.Label("To join, accept a Steam invite from whoever is hosting.", _note);

            GUILayout.Space(20f);
            if (GUILayout.Button("QUIT", _button, GUILayout.Height(30f))) Quit();
        }

        private void DrawWaitingRoom(NetworkManager net)
        {
            int here = net.ConnectedClientsIds.Count;
            GUILayout.Label(here == 1 ? "1 intern on board" : $"{here} interns on board", _tag);
            GUILayout.Space(14f);

            if (!net.IsServer)
            {
                GUILayout.Label("Waiting for the host to open the doors.", _note);
                return;
            }

            if (GUILayout.Button("START THE NIGHT", _button, GUILayout.Height(38f)))
                ShiftDirector.Instance?.BeginNight();

            GUILayout.Space(6f);
            if (GUILayout.Button("INVITE FRIENDS", _button, GUILayout.Height(28f)))
                _lobby?.InviteFriends();

            GUILayout.Space(16f);
            GUILayout.Label("Nobody joins once the doors are open, so wait for everybody.", _note);
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void Styles()
        {
            if (_title != null) return;

            _title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 46,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
            };
            _title.normal.textColor = new Color(0.88f, 0.90f, 0.93f);

            _tag = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleCenter,
                wordWrap = true,
            };
            _tag.normal.textColor = new Color(0.60f, 0.66f, 0.72f);

            _note = new GUIStyle(_tag) { fontSize = 11 };
            _note.normal.textColor = new Color(0.44f, 0.49f, 0.55f);

            _button = new GUIStyle(GUI.skin.button) { fontSize = 14, fontStyle = FontStyle.Bold };
        }
    }
}
