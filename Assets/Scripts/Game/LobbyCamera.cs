using Probation.Player;
using UnityEngine;

namespace Probation.Game
{
    /// <summary>
    /// Renders the scene before you have a body.
    ///
    /// The only Camera in the game lives on the Player prefab, and NetworkManager does not
    /// spawn that until you host or join. Without this the build boots to a black screen with
    /// a Host button floating on it, which looks exactly like a broken build.
    ///
    /// Switches itself off the moment a local player exists, so there is never a second live
    /// camera or a second audio listener.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class LobbyCamera : MonoBehaviour
    {
        private Camera _camera;
        private AudioListener _listener;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
            _listener = GetComponent<AudioListener>();
        }

        private void LateUpdate()
        {
            bool haveBody = HasLocalPlayer();
            if (_camera.enabled == !haveBody) return;

            _camera.enabled = !haveBody;
            if (_listener != null) _listener.enabled = !haveBody;
        }

        private static bool HasLocalPlayer() => PlayerNetworkSetup.Local != null;
    }
}
