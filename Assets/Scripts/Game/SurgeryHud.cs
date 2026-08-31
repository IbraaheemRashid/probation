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
    /// Specialism grants information nobody else has, and that asymmetry is the whole reason
    /// this is a proximity chat game: the only way through a surgery is to say out loud what
    /// only you can read.
    /// </summary>
    public class SurgeryHud : MonoBehaviour
    {
        [SerializeField] private float patientRange = 4f;
        [SerializeField] private float noticeSeconds = 5f;
        [SerializeField] private float scannerRange = 9f;

        private GUIStyle _label;
        private GUIStyle _big;
        private GUIStyle _prompt;
        private GUIStyle _notice;

        private void OnGUI()
        {
            _label ??= new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true };
            _big ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 20, richText = true, alignment = TextAnchor.MiddleCenter
            };
            _prompt ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 15, richText = true, alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
            _notice ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 17, richText = true, alignment = TextAnchor.MiddleCenter
            };

            DrawShiftBanner();
            DrawReview();
            DrawLocker();
            DrawOperationPanel();
            DrawScannerPanel();
            DrawCrosshairAndPrompt();
            DrawNotices();
        }

        // ---------------------------------------------------------------- shift

        private void DrawShiftBanner()
        {
            var director = ShiftDirector.Instance;
            if (director == null) return;

            GUILayout.BeginArea(new Rect(Screen.width * 0.5f - 150f, 8f, 300f, 34f), GUI.skin.box);

            string text = director.Phase switch
            {
                ShiftPhase.WeekOver => "<b>WEEK OVER</b>",
                ShiftPhase.Review => $"<b>PERFORMANCE REVIEW</b>  {director.TimeLeft:0}s",
                _ => $"<b>DAY {director.Day}/{director.ShiftsPerWeek}</b>   " +
                     $"{Mathf.FloorToInt(director.TimeLeft / 60f)}:{Mathf.FloorToInt(director.TimeLeft % 60f):00}   " +
                     $"losses {director.Deaths}/{director.DeathLimit}",
            };

            GUILayout.Label(text, _big);
            GUILayout.EndArea();
        }

        private void DrawReview()
        {
            var director = ShiftDirector.Instance;
            if (director == null || director.Phase != ShiftPhase.Review) return;
            if (director.ReviewLines.Count == 0) return;

            float height = Mathf.Min(360f, 40f + director.ReviewLines.Count * 18f);
            GUILayout.BeginArea(new Rect(Screen.width * 0.5f - 230f, 60f, 460f, height), GUI.skin.box);
            GUILayout.Label("<b>The supervisor reads the shift back to you.</b>", _label);
            GUILayout.Space(6f);

            foreach (string line in director.ReviewLines)
                GUILayout.Label(line, _label);

            GUILayout.EndArea();
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
                prompt = $"[E] {interactor.Focused.Prompt}";

            if (prompt == null) return;

            var size = _prompt.CalcSize(new GUIContent(prompt));
            GUI.Label(new Rect(cx - size.x * 0.5f, cy + 22f, size.x, size.y), prompt, _prompt);
        }

        // ---------------------------------------------------------------- notices

        /// <summary>Recent events, fading out. What just happened, in words, for now.</summary>
        private void DrawNotices()
        {
            var director = ShiftDirector.Instance;
            if (director == null || director.Notices.Count == 0) return;

            float y = Screen.height * 0.5f - 120f;

            foreach (var notice in director.Notices)
            {
                float age = Time.time - notice.At;
                if (age > noticeSeconds) continue;

                float alpha = Mathf.Clamp01((noticeSeconds - age) / 1.5f);
                GUI.color = new Color(1f, 1f, 1f, alpha);

                var size = _notice.CalcSize(new GUIContent(notice.Text));
                GUI.Label(new Rect(Screen.width * 0.5f - size.x * 0.5f, y, size.x, size.y),
                          notice.Text, _notice);
                y += 22f;
            }

            GUI.color = Color.white;
        }

        // ---------------------------------------------------------------- locker

        /// <summary>
        /// Pick your specialism. Per shift, never permanent, never assigned by the game, and two
        /// people may pick the same one - a permanent class would let a friend grief you out of
        /// your favourite toy for the whole week, and would kill the "we have no anaesthetist"
        /// panic that makes this scene fun.
        /// </summary>
        private void DrawLocker()
        {
            var local = PlayerNetworkSetup.Local;
            if (local == null) return;

            var role = local.GetComponent<PlayerRole>();
            if (role == null) return;

            GUILayout.BeginArea(new Rect(12f, Screen.height - 148f, 200f, 136f), GUI.skin.box);
            GUILayout.Label($"<b>LOCKER</b>  {role.Specialism}", _label);

            foreach (Specialism option in _specialisms)
            {
                if (option == role.Specialism) continue;
                if (GUILayout.Button(option.ToString(), GUILayout.Height(20f)))
                    role.Choose(option);
            }

            GUILayout.EndArea();
        }

        private static readonly Specialism[] _specialisms =
        {
            Specialism.Vascular,
            Specialism.Anaesthesia,
            Specialism.Exostructure,
            Specialism.Xenobiology,
        };

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
            var rect = new Rect(Screen.width * 0.5f - 200f, Screen.height - 210f, 400f, 108f);

            if (target == null)
            {
                GUILayout.BeginArea(rect, GUI.skin.box);
                GUILayout.Label("<b>SCANNER</b>   <i>point it at a patient</i>", _label);
                GUILayout.EndArea();
                return;
            }

            var role = local.GetComponent<PlayerRole>();
            Specialism specialism = role != null ? role.Specialism : Specialism.None;

            GUILayout.BeginArea(rect, GUI.skin.box);
            GUILayout.Label($"<b>SCANNER</b>   {target.State}", _label);
            GUILayout.Label($"heart rate   {target.HeartRate:0} bpm", _label);

            GUILayout.Label(specialism == Specialism.Anaesthesia
                ? $"pain state   <b>{(target.IsConscious ? "AWAKE - it can feel this" : "under")}</b>"
                : "pain state   <i>anaesthesia only</i>", _label);

            string diagnosis = target.Species != null ? target.Species.diagnosisText : "unidentified";
            GUILayout.Label(specialism == Specialism.Xenobiology
                ? $"diagnosis    <b>{diagnosis}</b>"
                : "diagnosis    <i>xenobiology only</i>", _label);

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
