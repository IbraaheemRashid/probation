using UnityEngine;
using UnityEngine.InputSystem;

namespace Probation.Surgery
{
    /// <summary>
    /// Scene-only furniture for the surgery testbed. Not part of the game and not shipped in the
    /// ward - it exists so that judging how a cut feels does not require restarting play mode
    /// every time you ruin a seam.
    ///
    /// R closes every seam in the scene. That is the whole feature, and it matters more than it
    /// sounds: the question this scene exists to answer is "does a careful drag feel different
    /// from a hurried one", and you cannot compare the two if the first attempt is permanent.
    /// </summary>
    public class SurgeryTestbed : MonoBehaviour
    {
        [TextArea]
        [SerializeField] private string instructions =
            "E  take an instrument\n" +
            "RMB  brace against the surface you are looking at\n" +
            "LMB  cut, while braced\n" +
            "R  close every seam";

        [SerializeField] private bool showCard = true;

        private int _resets;

        private void Update()
        {
            // activeInputHandler is 1 (new Input System only), so UnityEngine.Input is dead here.
            var keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.rKey.wasPressedThisFrame) return;

            foreach (var seam in Seam.All)
                if (seam != null) seam.Close();

            _resets++;
        }

        private void OnGUI()
        {
            if (!showCard) return;

            // Bottom left. NetworkBootstrap owns the top left and the instrument readout owns
            // the top right.
            var area = new Rect(12f, Screen.height - 132f, 340f, 120f);

            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label("SURGERY TESTBED");
            GUILayout.Space(4f);
            GUILayout.Label(instructions);
            if (_resets > 0) GUILayout.Label($"seams closed {_resets}x");
            GUILayout.EndArea();
        }
    }
}
