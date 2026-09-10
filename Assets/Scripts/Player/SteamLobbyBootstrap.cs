using System;
using System.Collections.Generic;
using System.Linq;
using Netcode.Transports.Facepunch;
using Steamworks;
using Steamworks.Data;
using Unity.Netcode;
using UnityEngine;

// Steamworks.Data has a Color of its own, and this file needs Unity's for the panel backing.
using Color = UnityEngine.Color;

namespace Probation.Player
{
    /// <summary>
    /// Steam lobby hosting and joining for playtests.
    ///
    /// Almost all of this is plain Facepunch.Steamworks and would survive a change of transport.
    /// The only coupling to FacepunchTransport is two lines in <see cref="StartClientTo"/> -
    /// which is deliberate, because that transport is the one component here whose NGO 2.x
    /// support is unverified.
    ///
    /// There are three ways into a lobby, on purpose. The Steam overlay is the nice one and the
    /// least reliable: it has to be injected into the process before the renderer starts, which
    /// does not happen in the Unity Editor at all and does not reliably happen for a build you
    /// launched by double-clicking rather than through Steam. When it is not there,
    /// OpenGameInviteOverlay returns nothing and reports nothing, which is indistinguishable from
    /// a dead button. So the overlay is offered but never relied on: a direct invite needs no
    /// overlay, and a typed lobby ID needs no invite either.
    /// </summary>
    public class SteamLobbyBootstrap : MonoBehaviour
    {
        [SerializeField] private int maxPlayers = 4;
        [SerializeField] private bool friendsOnly = true;

        private Lobby? _lobby;
        private string _status = "idle";

        private bool _invitePanelOpen;
        private List<Friend> _friends = new List<Friend>();
        private Vector2 _friendScroll;
        private string _joinIdText = string.Empty;

        // The built-in label style does not wrap, and every explanatory line on the invite panel
        // is longer than the panel is wide.
        private GUIStyle _wrap;

        private void OnEnable()
        {
            SteamMatchmaking.OnLobbyEntered += OnLobbyEntered;
            SteamMatchmaking.OnLobbyMemberJoined += OnMemberJoined;

            // Fires when someone accepts an invite or uses "Join Game" from the friends list.
            SteamFriends.OnGameLobbyJoinRequested += OnJoinRequested;
        }

        private void OnDisable()
        {
            SteamMatchmaking.OnLobbyEntered -= OnLobbyEntered;
            SteamMatchmaking.OnLobbyMemberJoined -= OnMemberJoined;
            SteamFriends.OnGameLobbyJoinRequested -= OnJoinRequested;
        }

        // ------------------------------------------------------------------ ui

        /// <summary>
        /// Set by MainMenu while a title card is up. One component should be in charge of the
        /// screen at a time, and a debug panel behind a title screen is nobody's idea of a menu.
        /// </summary>
        public bool PanelSuppressed { get; set; }

        /// <summary>
        /// Open the invite picker. Called by the menu's INVITE FRIENDS button.
        ///
        /// This used to call OpenGameInviteOverlay directly and do nothing at all when the
        /// overlay was not present, with no way to tell from inside the game. Now the overlay is
        /// one button on a panel rather than the whole feature.
        /// </summary>
        public void InviteFriends()
        {
            // Open the panel even with no lobby, and let it say so. Returning early here would
            // reproduce the original bug exactly: _status is only drawn on the debug panel, which
            // is suppressed behind the title card - which is the one place this is pressed from.
            // A button that reports its failure somewhere you cannot see is a button that does
            // nothing.
            _invitePanelOpen = true;

            if (!_lobby.HasValue)
            {
                _status = "no lobby to invite to - host first";
                Debug.LogWarning("[Probation] Invite requested with no Steam lobby. Either hosting " +
                                 "went through the direct-IP path, or CreateLobbyAsync failed.");
                return;
            }

            RefreshFriends();
        }

        private void RefreshFriends()
        {
            if (!SteamManager.Ready)
            {
                _friends.Clear();
                return;
            }

            // Cached rather than enumerated in OnGUI, which runs several times a frame.
            _friends = SteamFriends.GetFriends()
                .Where(f => f.IsOnline || f.IsPlayingThisGame)
                .OrderByDescending(f => f.IsPlayingThisGame)
                .ThenBy(f => f.Name)
                .ToList();
        }

        private void OnGUI()
        {
            // The invite picker has to outrank the title card - the waiting room is drawn by
            // MainMenu, and that is exactly where you invite people from. Lower depth is on top.
            GUI.depth = _invitePanelOpen ? -100 : 0;

            if (_invitePanelOpen) DrawInvitePanel();

            if (PanelSuppressed) return;

            GUILayout.BeginArea(new Rect(12f, 220f, 260f, 260f), GUI.skin.box);
            GUILayout.Label("STEAM");

            if (!SteamManager.Ready)
            {
                GUILayout.Label($"unavailable:\n{SteamManager.Problem}");
                GUILayout.Label("Use the direct-IP panel above.");
                GUILayout.EndArea();
                return;
            }

            GUILayout.Label($"{SteamManager.LocalName}");
            GUILayout.Label($"status: {_status}");
            GUILayout.Space(4f);

            var net = NetworkManager.Singleton;
            bool live = net != null && (net.IsClient || net.IsServer);

            if (!live)
            {
                if (GUILayout.Button("Host lobby", GUILayout.Height(26f))) HostLobby();

                GUILayout.Space(6f);
                GUILayout.Label("Join by lobby ID");
                _joinIdText = GUILayout.TextField(_joinIdText);
                if (GUILayout.Button("Join", GUILayout.Height(22f))) JoinById(_joinIdText);
            }
            else
            {
                if (_lobby.HasValue && net.IsHost &&
                    GUILayout.Button("Invite friends", GUILayout.Height(26f)))
                    InviteFriends();

                if (GUILayout.Button("Leave", GUILayout.Height(24f))) Leave();
            }

            GUILayout.EndArea();
        }

        /// <summary>
        /// The invite picker: every way of getting somebody in that does not depend on the Steam
        /// overlay being injected into this process.
        /// </summary>
        private void DrawInvitePanel()
        {
            _wrap ??= new GUIStyle(GUI.skin.label) { wordWrap = true };

            const float w = 360f;
            const float h = 380f;
            var area = new Rect(Screen.width * 0.5f - w * 0.5f, Screen.height * 0.5f - h * 0.5f, w, h);

            // Its own opaque backing, because it sits over MainMenu's wash.
            Color was = GUI.color;
            GUI.color = new Color(0.05f, 0.06f, 0.09f, 0.97f);
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = was;

            GUILayout.BeginArea(area, GUI.skin.box);

            GUILayout.Label("INVITE");

            if (!_lobby.HasValue)
            {
                // Everything needed to work out why, on screen, rather than in a log file the
                // person playing the build will never open.
                GUILayout.Label("There is no Steam lobby to invite anybody to.", _wrap);
                GUILayout.Space(6f);
                GUILayout.Label(SteamManager.Ready
                    ? $"Steam is fine ({SteamManager.LocalName})."
                    : $"Steam is NOT available: {SteamManager.Problem}", _wrap);
                GUILayout.Label($"status: {_status}");
                GUILayout.Space(6f);
                GUILayout.Label("You are hosting, but not through Steam - so this shift was " +
                                "started on the direct-IP transport, which nobody can reach from " +
                                "outside your machine. Leave, then use HOST A SHIFT.", _wrap);

                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Close", GUILayout.Height(24f))) _invitePanelOpen = false;
                GUILayout.EndArea();
                return;
            }

            ulong id = _lobby.Value.Id.Value;

            GUILayout.Label($"Lobby ID: {id}");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Copy ID", GUILayout.Height(22f)))
            {
                GUIUtility.systemCopyBuffer = id.ToString();
                _status = "lobby id copied";
            }

            // Worth keeping even though it is the unreliable one: when the overlay IS there it is
            // the nicest of the three, and it costs one button to find out.
            if (GUILayout.Button("Steam overlay", GUILayout.Height(22f)))
            {
                SteamFriends.OpenGameInviteOverlay(_lobby.Value.Id);
                _status = "asked for the overlay - if nothing appeared, it is not injected";
            }
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.Label("Send an invite directly - no overlay needed:", _wrap);

            if (_friends.Count == 0)
                GUILayout.Label("No friends online. Refresh once they are.");

            _friendScroll = GUILayout.BeginScrollView(_friendScroll, GUILayout.Height(200f));
            foreach (Friend friend in _friends)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(friend.IsPlayingThisGame ? $"* {friend.Name}" : friend.Name);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Invite", GUILayout.Width(64f), GUILayout.Height(20f)))
                    Invite(friend);
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();

            GUILayout.Label("* already running the game - these can be invited with no risk " +
                            "of Steam trying to launch Spacewar instead.", _wrap);

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh", GUILayout.Height(24f))) RefreshFriends();
            if (GUILayout.Button("Close", GUILayout.Height(24f))) _invitePanelOpen = false;
            GUILayout.EndHorizontal();

            GUILayout.EndArea();
        }

        private void Invite(Friend friend)
        {
            bool sent = _lobby.Value.InviteFriend(friend.Id);
            _status = sent ? $"invited {friend.Name}" : $"invite to {friend.Name} FAILED";

            if (sent) Debug.Log($"[Probation] Invite sent to {friend.Name} ({friend.Id}).");
            else Debug.LogError($"[Probation] InviteFriend returned false for {friend.Name}.");
        }

        // ------------------------------------------------------------------ host

        public async void HostLobby()
        {
            _status = "creating lobby...";
            try
            {
                var created = await SteamMatchmaking.CreateLobbyAsync(maxPlayers);
                if (!created.HasValue)
                {
                    _status = "lobby creation failed";
                    Debug.LogError("[Probation] CreateLobbyAsync returned null.");
                    return;
                }

                _lobby = created.Value;
                if (friendsOnly) _lobby.Value.SetFriendsOnly();
                else _lobby.Value.SetPublic();

                _lobby.Value.SetJoinable(true);

                // Joiners read this to know who to open a connection to. Lobby.Owner is not
                // reliable the instant a member enters, so publish it explicitly.
                _lobby.Value.SetData("host", SteamClient.SteamId.Value.ToString());

                // Lights up "Join Game" on your entry in a friend's friends list, which is a
                // fourth route in that needs neither an invite nor the overlay. Steam launches
                // the game with "+connect_lobby <id>" when it is not already running, which we
                // do not read yet - so this only helps friends who already have it open.
                SteamFriends.SetRichPresence("connect", $"+connect_lobby {_lobby.Value.Id.Value}");

                UseSteamTransport();
                if (NetworkManager.Singleton.StartHost())
                {
                    _status = $"hosting ({_lobby.Value.Id})";
                    Debug.Log($"[Probation] Hosting Steam lobby {_lobby.Value.Id}");
                }
                else
                {
                    _status = "StartHost failed";
                }
            }
            catch (Exception e)
            {
                _status = "error";
                Debug.LogError($"[Probation] Host failed: {e}");
            }
        }

        // ------------------------------------------------------------------ join

        /// <summary>
        /// Join a lobby whose ID somebody read out to you. The one path that involves neither the
        /// overlay nor an invite, so it still works when both are broken.
        /// </summary>
        public async void JoinById(string raw)
        {
            if (!ulong.TryParse((raw ?? string.Empty).Trim(), out ulong parsed) || parsed == 0)
            {
                _status = "that is not a lobby id";
                return;
            }

            _status = $"joining {parsed}...";
            try
            {
                var joined = await SteamMatchmaking.JoinLobbyAsync(parsed);
                if (!joined.HasValue)
                {
                    _status = "join failed - wrong id, or not joinable";
                    Debug.LogError($"[Probation] JoinLobbyAsync({parsed}) returned null.");
                }

                // On success OnLobbyEntered does the rest.
            }
            catch (Exception e)
            {
                _status = "join error";
                Debug.LogError($"[Probation] Join by id failed: {e}");
            }
        }

        private async void OnJoinRequested(Lobby lobby, SteamId _)
        {
            _status = "joining...";
            var result = await lobby.Join();
            if (result != RoomEnter.Success)
            {
                _status = $"join failed: {result}";
                Debug.LogError($"[Probation] Could not enter lobby: {result}");
            }
        }

        private void OnLobbyEntered(Lobby lobby)
        {
            _lobby = lobby;
            _invitePanelOpen = false;

            // The host enters its own lobby too, and is already running a server.
            if (NetworkManager.Singleton.IsServer) return;

            SteamId host = ResolveHost(lobby);
            if (host.Value == 0)
            {
                _status = "no host in lobby data";
                Debug.LogError("[Probation] Lobby had no usable host id.");
                return;
            }

            StartClientTo(host);
        }

        private static SteamId ResolveHost(Lobby lobby)
        {
            string raw = lobby.GetData("host");
            if (!string.IsNullOrEmpty(raw) && ulong.TryParse(raw, out ulong parsed))
                return parsed;

            return lobby.Owner.Id;
        }

        private void StartClientTo(SteamId host)
        {
            var transport = UseSteamTransport();
            if (transport == null) return;

            // --- the only transport-specific lines in this file ---
            transport.targetSteamId = host;

            if (NetworkManager.Singleton.StartClient())
            {
                _status = $"connected to {host}";
                Debug.Log($"[Probation] Joining Steam host {host}");
            }
            else
            {
                _status = "StartClient failed";
            }
        }

        // ------------------------------------------------------------------ plumbing

        /// <summary>
        /// Point NetworkConfig at the Facepunch transport. The direct-IP panel points it at
        /// UnityTransport, so whichever button you press last decides how you connect.
        /// </summary>
        private static FacepunchTransport UseSteamTransport()
        {
            var net = NetworkManager.Singleton;
            var transport = net.GetComponent<FacepunchTransport>();
            if (transport == null)
            {
                Debug.LogError("[Probation] No FacepunchTransport on NetworkManager. " +
                               "Add the component, then press Host again.");
                return null;
            }

            net.NetworkConfig.NetworkTransport = transport;
            return transport;
        }

        private void OnMemberJoined(Lobby lobby, Friend friend) =>
            Debug.Log($"[Probation] {friend.Name} entered the lobby.");

        private void Leave()
        {
            if (_lobby.HasValue) _lobby.Value.Leave();
            _lobby = null;
            _invitePanelOpen = false;
            SteamFriends.ClearRichPresence();
            NetworkManager.Singleton.Shutdown();
            _status = "idle";
        }
    }
}
