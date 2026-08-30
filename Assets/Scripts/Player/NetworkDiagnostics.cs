using Probation.Interaction;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Probation.Player
{
    /// <summary>
    /// On-screen state for slice playtests. Sits on the NetworkManager and is deleted before
    /// anything ships.
    ///
    /// FOCUS is first on the list deliberately. An unfocused build with Run In Background off
    /// stops simulating, which presents as "the other window froze" and sends people hunting
    /// through netcode for a problem that is a Player Setting.
    /// </summary>
    public class NetworkDiagnostics : MonoBehaviour
    {
        [SerializeField] private bool show = true;

        private GUIStyle _style;

        private void Update()
        {
            // Active Input Handling is "Input System Package (New)", so legacy UnityEngine.Input
            // throws rather than returning false. Everything must go through Keyboard.current.
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f3Key.wasPressedThisFrame) show = !show;
        }

        private void OnGUI()
        {
            if (!show) return;

            _style ??= new GUIStyle(GUI.skin.label) { fontSize = 12, richText = true };

            var net = NetworkManager.Singleton;
            var local = PlayerNetworkSetup.Local;

            GUILayout.BeginArea(new Rect(Screen.width - 272f, 12f, 260f, 260f), GUI.skin.box);

            GUILayout.Label($"FOCUS      {Flag(Application.isFocused)}", _style);
            GUILayout.Label($"BACKGROUND {Flag(Application.runInBackground)}", _style);
            GUILayout.Space(4f);

            if (net == null)
            {
                GUILayout.Label("<color=#ff8080>no NetworkManager</color>", _style);
            }
            else
            {
                string role = !net.IsListening ? "offline"
                            : net.IsHost ? "host"
                            : net.IsServer ? "server" : "client";
                GUILayout.Label($"role       {role}", _style);
                GUILayout.Label($"clientId   {net.LocalClientId}", _style);
                GUILayout.Label($"connected  {(net.IsListening ? net.ConnectedClientsIds.Count : 0)}", _style);
                GUILayout.Label($"spawned    {(net.SpawnManager != null ? net.SpawnManager.SpawnedObjects.Count : 0)}", _style);
            }

            GUILayout.Space(4f);
            if (local == null)
            {
                GUILayout.Label("<color=#ffc080>no local player</color>", _style);
            }
            else
            {
                var loco = local.GetComponent<PlayerLocomotion>();
                var reader = local.GetComponent<PlayerInputReader>();

                GUILayout.Label($"owner      {Flag(local.IsOwner)}", _style);
                GUILayout.Label($"pos        {local.transform.position.ToString("F1")}", _style);
                if (loco != null)
                {
                    GUILayout.Label($"grounded   {Flag(loco.IsGrounded)}", _style);
                    GUILayout.Label($"speed      {new Vector2(loco.Velocity.x, loco.Velocity.z).magnitude:F2} m/s", _style);
                }
                if (reader != null)
                {
                    GUILayout.Label($"actions    {Flag(reader.ActionsEnabled)}", _style);
                    GUILayout.Label($"move       {reader.Move.ToString("F2")}", _style);
                }

                var carry = local.GetComponent<PlayerCarry>();
                if (carry != null)
                {
                    string held = carry.Carried != null
                        ? $"{carry.Carried.DisplayName} ({carry.Carried.Kind})"
                        : "-";
                    GUILayout.Label($"carrying   {held}", _style);
                }
                if (loco != null)
                    GUILayout.Label($"encumber   {loco.Encumbrance:0.00}", _style);
            }

            GUILayout.EndArea();
        }

        private static string Flag(bool value) =>
            value ? "<color=#80e080>yes</color>" : "<color=#ff8080>no</color>";
    }
}
