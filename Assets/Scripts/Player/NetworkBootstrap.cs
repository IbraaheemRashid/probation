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
                GUILayout.Label($"Connected: {net.ConnectedClientsIds.Count}");

                GUILayout.Space(6f);
                if (GUILayout.Button("Disconnect", GUILayout.Height(24f)))
                    net.Shutdown();
            }

            GUILayout.EndArea();
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

            // A host binds to every interface, not to the address clients type in. Binding the
            // server to 127.0.0.1 makes it unreachable from any other machine on the network,
            // which is the difference between "works in the Editor" and "works with friends".
            if (asHost) transport.SetConnectionData(address, port, "0.0.0.0");
            else transport.SetConnectionData(address, port);

            Debug.Log($"[Probation] {(asHost ? "Hosting on" : "Connecting to")} {address}:{port}");
            return true;
        }
    }
}
