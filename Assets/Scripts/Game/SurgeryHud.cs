using Probation.Interaction;
using Probation.Player;
using Probation.Surgery;
using UnityEngine;

namespace Probation.Game
{
    /// <summary>
    /// Greybox HUD. IMGUI on purpose - it needs no canvas work and all of it gets replaced
    /// before anything ships.
    ///
    /// The important part is not the layout, it is <em>what each intern is allowed to see</em>.
    /// The information asymmetry comes from instruments, not from roles. There is one scanner;
    /// whoever is holding it can read the patient and is not holding a scalpel, so the reading
    /// has to be said out loud to be worth anything. That is why this is a voice game.
    /// </summary>
    public class SurgeryHud : MonoBehaviour
    {
        [SerializeField] private float patientRange = 4f;
        [SerializeField] private float scannerRange = 9f;

        private GUIStyle _label;
        private GUIStyle _prompt;
        private float _staminaFade;

        private void OnGUI()
        {
            _label ??= new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true };
            _prompt ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 15, richText = true, alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };

            DrawOperationPanel();
            DrawScannerPanel();
            DrawCrosshairAndPrompt();
        }

        // ---------------------------------------------------------------- crosshair

        /// <summary>
        /// A dot, and what pressing E would do. Placeholder for real feedback - until there are
        /// animations and sounds, this is the only thing telling you the game noticed you.
        /// </summary>
        private void DrawCrosshairAndPrompt()
        {
            var local = PlayerNetworkSetup.Local;
            if (local == null) return;

            float cx = Screen.width * 0.5f;
            float cy = Screen.height * 0.5f;

            GUI.color = new Color(1f, 1f, 1f, 0.65f);
            GUI.DrawTexture(new Rect(cx - 2f, cy - 2f, 4f, 4f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            var carry = local.GetComponent<PlayerCarry>();
            var interactor = local.GetComponent<PlayerInteractor>();

            string prompt = null;
            if (carry != null && carry.IsCarrying)
                prompt = $"[E] drop the {carry.Carried.DisplayName}";
            else if (interactor != null && interactor.Focused != null)
                prompt = $"[E] {interactor.Focused.Prompt}   <i>(tap to keep, hold to carry)</i>";

            DrawStamina(local, cx, cy);

            if (prompt == null) return;

            var size = _prompt.CalcSize(new GUIContent(prompt));
            GUI.Label(new Rect(cx - size.x * 0.5f, cy + 44f, size.x, size.y), prompt, _prompt);
        }

        /// <summary>
        /// The one readout you get for free, because it is your own body and you would feel it.
        ///
        /// Everything about a <em>patient</em> costs an action to learn - you pick up the
        /// scanner, you point it, and your hands are full while you do. Stamina is the
        /// exception on purpose, and it is the only exception.
        ///
        /// Fades in when spent rather than popping, and is absent entirely at full, so walking
        /// never feels rationed.
        /// </summary>
        private void DrawStamina(PlayerNetworkSetup local, float cx, float cy)
        {
            var loco = local.GetComponent<PlayerLocomotion>();
            if (loco == null) return;

            float wanted = loco.Stamina >= 0.999f ? 0f : 1f;
            _staminaFade = Mathf.MoveTowards(_staminaFade, wanted, Time.deltaTime * 3f);
            if (_staminaFade <= 0.01f) return;

            const float width = 148f;
            const float height = 6f;
            var back = new Rect(cx - width * 0.5f, cy + 26f, width, height);

            // Winded reads as a slow pulse, so you notice it without a word of text.
            float pulse = loco.Winded ? 0.72f + Mathf.Sin(Time.time * 7f) * 0.28f : 1f;

            GUI.color = new Color(0f, 0f, 0f, 0.5f * _staminaFade);
            GUI.DrawTexture(new Rect(back.x - 1f, back.y - 1f, back.width + 2f, back.height + 2f),
                            Texture2D.whiteTexture);

            Color fill = loco.Winded ? new Color(0.88f, 0.33f, 0.28f)
                       : loco.Stamina < 0.35f ? new Color(0.92f, 0.72f, 0.34f)
                       : new Color(0.86f, 0.90f, 0.88f);

            GUI.color = new Color(fill.r, fill.g, fill.b, 0.9f * _staminaFade * pulse);
            GUI.DrawTexture(new Rect(back.x, back.y, back.width * Mathf.Clamp01(loco.Stamina), back.height),
                            Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        // ---------------------------------------------------------------- scanner

        /// <summary>
        /// The patient readout. You only get it while holding the scanner, and you have to
        /// point it at somebody.
        ///
        /// That is the whole point: the readout is a physical instrument one person carries,
        /// not ambient knowledge. Whoever has the scanner is not holding a scalpel, and what
        /// the scanner tells them depends on what they specialised in - so the reading has to
        /// be said out loud to be worth anything.
        /// </summary>
        private void DrawScannerPanel()
        {
            var local = PlayerNetworkSetup.Local;
            if (local == null) return;

            var carry = local.GetComponent<PlayerCarry>();
            bool holdingScanner = carry != null && carry.Carried != null && carry.Carried.ToolId == "scanner";
            if (!holdingScanner) return;

            Patient target = PatientInSights(local);
            var rect = new Rect(Screen.width * 0.5f - 200f, Screen.height - 272f, 400f, 170f);

            if (target == null)
            {
                GUILayout.BeginArea(rect, GUI.skin.box);
                GUILayout.Label("<b>SCANNER</b>   <i>point it at a patient</i>", _label);
                GUILayout.EndArea();
                return;
            }

            // Signs, and never the answer.
            //
            // This used to print Species.diagnosisText, which named the condition outright - so
            // the scanner was a lookup and there was nothing to work out. It now reports what an
            // instrument can actually see, plus the species, and leaves the two to be put
            // together by somebody. That last step is the game.
            string species = target.Species != null ? target.Species.displayName : "unidentified";
            var condition = target.Condition;

            GUILayout.BeginArea(rect, GUI.skin.box);
            GUILayout.Label($"<b>SCANNER</b>   {target.State}", _label);
            GUILayout.Label($"species      <b>{species}</b>", _label);
            GUILayout.Label($"heart rate   {target.HeartRate:0} bpm", _label);
            GUILayout.Label($"pain state   <b>{(target.IsConscious ? "AWAKE - it can feel this" : "under")}</b>", _label);

            if (condition == null || condition.scannerLines.Length == 0)
            {
                GUILayout.Label("signs        nothing the scanner can see", _label);
            }
            else
            {
                for (int i = 0; i < condition.scannerLines.Length; i++)
                    GUILayout.Label(i == 0
                        ? $"signs        {condition.scannerLines[i]}"
                        : $"             {condition.scannerLines[i]}", _label);
            }

            GUILayout.EndArea();
        }

        /// <summary>Whatever the local intern is pointing at, out to scanner range.</summary>
        private Patient PatientInSights(PlayerNetworkSetup local)
        {
            var interactor = local.GetComponent<PlayerInteractor>();
            Transform view = interactor != null ? interactor.ViewSource : null;
            if (view == null) return null;

            // Generously wide - this is a readout, not a precision test.
            return Physics.SphereCast(view.position, 0.4f, view.forward,
                                      out RaycastHit hit, scannerRange, ~0, QueryTriggerInteraction.Ignore)
                ? hit.collider.GetComponentInParent<Patient>()
                : null;
        }

        // ---------------------------------------------------------------- operation

        /// <summary>
        /// What the procedure wants next. Deliberately <em>not</em> gated on the scanner - you
        /// need to know which tool goes where while your hands are full of that tool.
        /// </summary>
        private void DrawOperationPanel()
        {
            var local = PlayerNetworkSetup.Local;
            if (local == null) return;

            Patient patient = NearestPatient(local.transform.position);
            if (patient == null) return;

            var operation = patient.GetComponent<Operation>();
            if (operation == null || operation.Procedure == null) return;

            GUILayout.BeginArea(new Rect(Screen.width * 0.5f - 200f, Screen.height - 96f, 400f, 88f), GUI.skin.box);

            if (operation.Finished)
            {
                GUILayout.Label($"<b>{operation.Procedure.displayName} complete.</b>", _label);
                GUILayout.EndArea();
                return;
            }

            var step = operation.CurrentStep;
            if (step == null) { GUILayout.EndArea(); return; }

            string hands = step.handsRequired > 1 ? $"   <b>({step.handsRequired} pairs of hands)</b>" : "";
            GUILayout.Label($"step {operation.StepIndex + 1}/{operation.Procedure.steps.Count}: " +
                            $"<b>{step.displayName}</b>{hands}", _label);
            GUILayout.Label($"{step.requiredToolId} at the {step.targetSite}", _label);

            var bar = GUILayoutUtility.GetRect(380f, 10f);
            GUI.Box(bar, GUIContent.none);
            GUI.Box(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(operation.Progress), bar.height),
                    GUIContent.none);

            GUILayout.EndArea();
        }

        private Patient NearestPatient(Vector3 from)
        {
            Patient best = null;
            float bestDistance = patientRange * patientRange;

            foreach (var patient in Patient.All)
            {
                if (patient == null) continue;
                float d = (patient.transform.position - from).sqrMagnitude;
                if (d >= bestDistance) continue;
                bestDistance = d;
                best = patient;
            }

            return best;
        }
    }
}
