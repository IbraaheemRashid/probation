using UnityEngine;

namespace Probation.Player
{
    /// <summary>
    /// Holds the hardware cursor locked while at least one instance is enabled.
    ///
    /// Reference counted on purpose. Once four players exist, three of them are remote objects
    /// on your machine with their player components switched off. If unlocking were tied to any
    /// single component being disabled, spawning a remote player would release *your* cursor.
    /// Counting holders makes the order of spawns and disables irrelevant.
    /// </summary>
    public class CursorLock : MonoBehaviour
    {
        private static int _holders;

        private void OnEnable()
        {
            _holders++;
            Apply();
        }

        private void OnDisable()
        {
            _holders = Mathf.Max(0, _holders - 1);
            Apply();
        }

        private static void Apply()
        {
            bool locked = _holders > 0;
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        /// <summary>Release the cursor regardless of holders, e.g. for a pause menu.</summary>
        public static void ForceRelease()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        /// <summary>Restore the lock after a menu closes.</summary>
        public static void Restore() => Apply();
    }
}
