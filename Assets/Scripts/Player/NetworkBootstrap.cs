using System.Net.Sockets;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Probation.Player
{
    /// <summary>
    /// Throwaway host/join panel for slice playtests. Deliberately IMGUI: it needs no canvas,
    /// no prefabs and no layout work, and all of it gets deleted before anything ships.
    ///
    /// Direct address only. Relay goes in once the UGS project is linked - keeping this step
    /// free of service dependencies means the netcode spine can be proven on two local builds
    /// before anyone signs into anything.
    /// </summary>
    public class NetworkBootstrap : MonoBehaviour
    {
        [SerializeField] private string address = "127.0.0.1";
        [SerializeField] private ushort port = 7777;

        [Tooltip("Start hosting the moment the scene loads, with no panel. For single-player spike scenes, where clicking Host every time is friction between you and the thing you are trying to feel.")]
        [SerializeField] private bool autoHost;

        private void Start()
        {
            if (!autoHost) return;

            var net = NetworkManager.Singleton;
            if (net == null || net.IsListening) return;

            if (ApplyConnectionData(asHost: true)) net.StartHost();
        }

        private void OnGUI()
        {
            var net = NetworkManager.Singleton;
            if (net == null) return;

            GUILayout.BeginArea(new Rect(12f, 12f, 260f, 200f), GUI.skin.box);

            if (!net.IsClient && !net.IsServer)
            {
                GUILayout.Label("PROBATION - slice test");
                GUILayout.Space(4f);

                GUILayout.Label("Host address");
                address = GUILayout.TextField(address);

                GUILayout.BeginHorizontal();
                GUILayout.Label("port", GUILayout.Width(30f));
                if (ushort.TryParse(GUILayout.TextField(port.ToString()), out ushort typed))
                    port = typed;
                GUILayout.EndHorizontal();

                GUILayout.Space(6f);
                if (GUILayout.Button("Host", GUILayout.Height(28f)) && ApplyConnectionData(asHost: true))
                    net.StartHost();

                if (GUILayout.Button("Join", GUILayout.Height(28f)) && ApplyConnectionData(asHost: false))
                    net.StartClient();
            }
            else
            {
                string role = net.IsHost ? "Host" : net.IsServer ? "Server" : "Client";
                GUILayout.Label($"{role} - client {net.LocalClientId}");
                if (net.IsHost) GUILayout.Label($"port {port}");
                GUILayout.Label($"Connected: {net.ConnectedClientsIds.Count}");

                GUILayout.Space(6f);
                if (GUILayout.Button("Disconnect", GUILayout.Height(24f)))
                    net.Shutdown();
            }

            GUILayout.EndArea();
        }

        private const int PortSearchRange = 10;

        /// <summary>
        /// First port from <paramref name="start"/> that can actually be bound, or 0. Tested by
        /// binding it briefly ourselves - UnityTransport gives no way to ask in advance, and
        /// finding out through StartHost means eating a transport failure and a shutdown cascade.
        /// </summary>
        private static ushort FirstFreePort(ushort start)
        {
            for (int candidate = start; candidate <= start + PortSearchRange && candidate <= ushort.MaxValue; candidate++)
            {
                try
                {
                    using var probe = new UdpClient(candidate);
                    return (ushort)candidate;
                }
                catch (SocketException)
                {
                    // In use. Try the next one.
                }
            }

            return 0;
        }

        private bool ApplyConnectionData(bool asHost)
        {
            var net = NetworkManager.Singleton;

            // Starting on top of a live session leaves the old socket bound and the new bind
            // fails with a bare "transport start failure".
            if (net.IsListening)
            {
                Debug.LogWarning("[Probation] Already listening - shutting the old session down first.");
                net.Shutdown();
                return false;
            }

            var transport = net.GetComponent<UnityTransport>();
            if (transport == null)
            {
                Debug.LogError("[Probation] NetworkManager has no UnityTransport component. " +
                               "Run Probation > Setup > 4 - Network the Player.");
                return false;
            }

            // Having the component is not the same as NetworkConfig pointing at it. When that
            // reference is missing, NGO reports "No transport has been selected!" and then
            // throws from inside StartHost, which reads as a much worse bug than it is.
            if (net.NetworkConfig.NetworkTransport == null)
            {
                net.NetworkConfig.NetworkTransport = transport;
                Debug.LogWarning("[Probation] NetworkConfig.NetworkTransport was unset; using the " +
                                 "UnityTransport on this object. Assign it in the inspector to make it stick.");
            }

            if (asHost)
            {
                // The Editor leaks its UDP socket between play sessions, so the port it used
                // last time is often still held by Unity itself. Rather than making you restart
                // the Editor, walk up until something is actually bindable.
                ushort free = FirstFreePort(port);
                if (free == 0)
                {
                    Debug.LogError($"[Probation] No free UDP port between {port} and {port + PortSearchRange}.");
                    return false;
                }

                if (free != port)
                    Debug.LogWarning($"[Probation] Port {port} was still held (usually a leaked socket " +
                                     $"from a previous play session). Hosting on {free} instead - " +
                                     "joiners need this number.");
                port = free;

                // A host binds every interface, not the address clients type in. Binding the
                // server to 127.0.0.1 makes it unreachable from any other machine, which is the
                // difference between "works in the Editor" and "works with friends".
                transport.SetConnectionData(address, port, "0.0.0.0");
            }
            else
            {
                transport.SetConnectionData(address, port);
            }

            Debug.Log($"[Probation] {(asHost ? "Hosting on" : "Connecting to")} {address}:{port}");
            return true;
        }
    }
}
