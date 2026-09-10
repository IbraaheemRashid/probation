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

        [Tooltip("Reads the FreeCursor action. Found on this object if left empty.")]
        [SerializeField] private PlayerInputReader input;

        /// <summary>
        /// True while the player has deliberately let go of the cursor to click something -
        /// the Steam panel, the diagnostics overlay, anything drawn over a running shift.
        ///
        /// Separate from the holder count rather than folded into it, because the two answer
        /// different questions. Holders are about which player objects exist; this is about what
        /// the person at the keyboard is currently doing, and it has to win over both the holders
        /// and <see cref="ForceRelease"/>/<see cref="Restore"/>, or the menu closing would take
        /// the cursor back off somebody mid-click.
        /// </summary>
        public static bool Freed { get; private set; }

        private void Awake()
        {
            if (input == null) input = GetComponent<PlayerInputReader>();
        }

        private void OnEnable()
        {
            _holders++;
            Apply();
        }

        private void OnDisable()
        {
            _holders = Mathf.Max(0, _holders - 1);

            // A player that despawns while holding the cursor loose would otherwise leave every
            // later spawn free-cursored with nothing on screen explaining it.
            if (_holders == 0) Freed = false;

            Apply();
        }

        private void Update()
        {
            if (input == null || !input.FreeCursorPressed) return;
            Toggle();
        }

        /// <summary>
        /// A freed cursor looks identical to a broken one - the crosshair is still there, the
        /// view has simply stopped answering the mouse. One line saying which it is costs
        /// nothing and saves someone reporting look as broken.
        /// </summary>
        private void OnGUI()
        {
            if (!Freed) return;

            const string hint = "CURSOR FREE  -  F1 to play";
            var size = GUI.skin.label.CalcSize(new GUIContent(hint));
            var rect = new Rect(Screen.width * 0.5f - size.x * 0.5f, 8f, size.x, size.y);

            Color was = GUI.color;
            GUI.color = new Color(0.75f, 0.80f, 0.86f, 0.85f);
            GUI.Label(rect, hint);
            GUI.color = was;
        }

        /// <summary>Swap between playing and pointing at things.</summary>
        public static void Toggle()
        {
            Freed = !Freed;
            Apply();
        }

        private static void Apply()
        {
            bool locked = _holders > 0 && !Freed;
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
