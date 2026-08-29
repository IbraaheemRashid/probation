using System;
using Netcode.Transports.Facepunch;
using Steamworks;
using Steamworks.Data;
using Unity.Netcode;
using UnityEngine;

namespace Probation.Player
{
    /// <summary>
    /// Steam lobby hosting and joining for playtests.
    ///
    /// Almost all of this is plain Facepunch.Steamworks and would survive a change of transport.
    /// The only coupling to FacepunchTransport is two lines in <see cref="StartClientTo"/> -
    /// which is deliberate, because that transport is the one component here whose NGO 2.x
    /// support is unverified.
    /// </summary>
    public class SteamLobbyBootstrap : MonoBehaviour
    {
        [SerializeField] private int maxPlayers = 4;
        [SerializeField] private bool friendsOnly = true;

        private Lobby? _lobby;
        private string _status = "idle";

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

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(12f, 220f, 260f, 190f), GUI.skin.box);
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
            }
            else
            {
                if (_lobby.HasValue && net.IsHost &&
                    GUILayout.Button("Invite friends", GUILayout.Height(26f)))
                    SteamFriends.OpenGameInviteOverlay(_lobby.Value.Id);

                if (GUILayout.Button("Leave", GUILayout.Height(24f))) Leave();
            }

            GUILayout.EndArea();
        }

        // ------------------------------------------------------------------ host

        private async void HostLobby()
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
            NetworkManager.Singleton.Shutdown();
            _status = "idle";
        }
    }
}
