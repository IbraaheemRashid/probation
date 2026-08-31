using UnityEngine;

namespace Probation.Game
{
    /// <summary>
    /// Shift presentation: the ward clock, the card that opens and closes each night, and the
    /// review. Still IMGUI, but drawn rather than laid out - a clock face reads as a place in
    /// a way that a countdown label never does.
    ///
    /// Surgery-specific readouts live in SurgeryHud. This is the shell of the loop.
    /// </summary>
    public class ShiftHud : MonoBehaviour
    {
        [Header("Clock")]
        [SerializeField] private float clockRadius = 46f;
        [SerializeField] private Color faceColour = new(0.06f, 0.09f, 0.09f, 0.85f);
        [SerializeField] private Color rimColour = new(0.36f, 0.78f, 0.72f, 0.9f);
        [SerializeField] private Color handColour = new(0.90f, 0.95f, 0.93f, 0.95f);
        [SerializeField] private Color sweepColour = new(0.36f, 0.78f, 0.72f, 0.30f);
        [SerializeField] private Color okColour = new(0.45f, 0.88f, 0.55f, 1f);
        [SerializeField] private Color dueColour = new(0.95f, 0.72f, 0.35f, 1f);

        [Header("Cards")]
        [SerializeField] private float cardSeconds = 3.5f;

        private Texture2D _disc;
        private GUIStyle _time;
        private GUIStyle _phase;
        private GUIStyle _cardTitle;
        private GUIStyle _cardSub;
        private GUIStyle _line;
        private GUIStyle _quota;

        private ShiftPhase _lastPhase = (ShiftPhase)(-1);
        private string _cardTitleText;
        private string _cardSubText;
        private float _cardAt = -99f;

        private void Update()
        {
            var director = ShiftDirector.Instance;
            if (director == null || director.Phase == _lastPhase) return;

            _lastPhase = director.Phase;
            ShowCard(director);
        }

        private void ShowCard(ShiftDirector director)
        {
            (_cardTitleText, _cardSubText) = director.Phase switch
            {
                ShiftPhase.Shift => ($"NIGHT {director.Day}", $"Discharge {director.Quota} of them alive."),
                ShiftPhase.CoverUp => director.QuotaMet
                    ? ("QUOTA MET", $"{director.Discharged} discharged. Tidy up.")
                    : ("QUOTA MISSED", $"{director.Discharged} of {director.Quota}. Strike {director.Strikes}."),
                ShiftPhase.Review => ("PERFORMANCE REVIEW", null),
                ShiftPhase.WeekOver => director.SurvivedWeek
                    ? ("CONTRACT SIGNED", "You survived the week.")
                    : ("DISMISSED", "Clear your locker."),
                _ => ((string)null, (string)null),
            };
            _cardAt = Time.time;
        }

        private void OnGUI()
        {
            EnsureStyles();

            var director = ShiftDirector.Instance;
            if (director == null) return;

            DrawClock(director);
            DrawVerdict(director);
            DrawReview(director);
            DrawNotices(director);
            DrawCard();
        }

        // ---------------------------------------------------------------- clock

        private void DrawClock(ShiftDirector director)
        {
            var centre = new Vector2(Screen.width * 0.5f, 18f + clockRadius);

            // Face
            Disc(centre, clockRadius + 3f, rimColour);
            Disc(centre, clockRadius, faceColour);

            // The night elapsed so far, as a filled sweep. Reads at a glance without numbers.
            float progress = director.Phase == ShiftPhase.Shift ? director.PhaseProgress : 1f;
            DrawSweep(centre, clockRadius - 5f, progress);

            // Twelve marks
            for (int i = 0; i < 12; i++)
            {
                float angle = i * 30f;
                bool major = i % 3 == 0;
                Line(centre, angle, clockRadius - (major ? 12f : 7f), clockRadius - 3f,
                     major ? 2.5f : 1.5f, rimColour);
            }

            // Hands. The shift sweeps eight fictional hours, so the hour hand barely moves and
            // the minute hand is the one you actually watch.
            float hours = ParseHours(director.WardTime);
            Line(centre, hours % 12f * 30f, 0f, clockRadius * 0.52f, 3.5f, handColour);
            Line(centre, hours % 1f * 360f, 0f, clockRadius * 0.78f, 2f, handColour);
            Disc(centre, 3.5f, handColour);

            // Readouts
            var timeRect = new Rect(centre.x - 70f, centre.y + clockRadius + 4f, 140f, 22f);
            GUI.Label(timeRect, director.WardTime, _time);

            string phase = director.Phase switch
            {
                ShiftPhase.Shift => $"NIGHT {director.Day}/{director.ShiftsPerWeek}",
                ShiftPhase.CoverUp => "COVER UP",
                ShiftPhase.Review => "REVIEW",
                _ => "WEEK OVER",
            };
            GUI.Label(new Rect(centre.x - 140f, timeRect.yMax - 2f, 280f, 18f), phase, _phase);

            if (director.Phase == ShiftPhase.WeekOver) return;

            // The quota, in the loudest colour available. Everything else on this clock is
            // context; this is the only number that decides whether the night was a success.
            var quotaRect = new Rect(centre.x - 140f, timeRect.yMax + 14f, 280f, 22f);
            _quota.normal.textColor = director.QuotaMet ? okColour : dueColour;
            GUI.Label(quotaRect, $"DISCHARGED  {director.Discharged} / {director.Quota}", _quota);

            string strikes = director.Strikes > 0
                ? $"strikes {director.Strikes}/{director.MaxStrikes}   losses {director.Deaths}/{director.DeathLimit}"
                : $"losses {director.Deaths}/{director.DeathLimit}";
            GUI.Label(new Rect(centre.x - 140f, quotaRect.yMax - 2f, 280f, 16f), strikes, _phase);
        }

        private static float ParseHours(string wardTime)
        {
            string[] parts = wardTime.Split(':');
            return parts.Length == 2 && int.TryParse(parts[0], out int h) && int.TryParse(parts[1], out int m)
                ? h + m / 60f
                : 0f;
        }

        /// <summary>Filled arc, drawn as wedges. Crude, but it is a handful of quads.</summary>
        private void DrawSweep(Vector2 centre, float radius, float progress)
        {
            int steps = Mathf.CeilToInt(Mathf.Clamp01(progress) * 48f);
            for (int i = 0; i < steps; i++)
            {
                float angle = i / 48f * 360f;
                Line(centre, angle, radius * 0.45f, radius, 6f, sweepColour);
            }
        }

        // ---------------------------------------------------------------- cards

        private void DrawCard()
        {
            float age = Time.time - _cardAt;
            if (age > cardSeconds || string.IsNullOrEmpty(_cardTitleText)) return;

            // Quick in, hold, slow out.
            float alpha = Mathf.Min(Mathf.Clamp01(age / 0.35f),
                                    Mathf.Clamp01((cardSeconds - age) / 1.2f));

            float y = Screen.height * 0.38f;
            Fill(new Rect(0f, y - 12f, Screen.width, 96f), new Color(0f, 0f, 0f, 0.55f * alpha));
            Fill(new Rect(0f, y - 13f, Screen.width, 1f), new Color(rimColour.r, rimColour.g, rimColour.b, alpha));
            Fill(new Rect(0f, y + 84f, Screen.width, 1f), new Color(rimColour.r, rimColour.g, rimColour.b, alpha));

            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.Label(new Rect(0f, y, Screen.width, 52f), _cardTitleText, _cardTitle);
            if (!string.IsNullOrEmpty(_cardSubText))
                GUI.Label(new Rect(0f, y + 50f, Screen.width, 26f), _cardSubText, _cardSub);
            GUI.color = Color.white;
        }

        // ---------------------------------------------------------------- verdict

        /// <summary>
        /// The end of the run, and it stays up. A fade-out card would leave a group sitting in
        /// an empty ward wondering whether they won.
        /// </summary>
        private void DrawVerdict(ShiftDirector director)
        {
            if (director.Phase != ShiftPhase.WeekOver) return;

            var rect = new Rect(Screen.width * 0.5f - 240f, Screen.height * 0.5f - 90f, 480f, 180f);
            Fill(rect, new Color(0.04f, 0.07f, 0.07f, 0.94f));
            Fill(new Rect(rect.x, rect.y, rect.width, 2f), director.SurvivedWeek ? okColour : dueColour);

            GUI.color = director.SurvivedWeek ? okColour : dueColour;
            GUI.Label(new Rect(rect.x, rect.y + 26f, rect.width, 50f),
                      director.SurvivedWeek ? "CONTRACT SIGNED" : "DISMISSED", _cardTitle);
            GUI.color = Color.white;

            string body = director.SurvivedWeek
                ? $"Seven nights. {director.Deaths} lost along the way."
                : director.Strikes >= director.MaxStrikes
                    ? $"You missed quota {director.Strikes} nights running."
                    : $"{director.Deaths} patients died on this ward.";

            GUI.Label(new Rect(rect.x, rect.y + 84f, rect.width, 26f), body, _cardSub);
            GUI.Label(new Rect(rect.x, rect.y + 118f, rect.width, 26f),
                      "Host: press R to start a new week.", _cardSub);
        }

        // ---------------------------------------------------------------- review

        private void DrawReview(ShiftDirector director)
        {
            if (director.Phase != ShiftPhase.Review || director.ReviewLines.Count == 0) return;

            float height = Mathf.Min(380f, 46f + director.ReviewLines.Count * 19f);
            var rect = new Rect(Screen.width * 0.5f - 240f, Screen.height * 0.5f - height * 0.5f, 480f, height);

            Fill(rect, new Color(0.04f, 0.07f, 0.07f, 0.92f));
            Fill(new Rect(rect.x, rect.y, rect.width, 2f), rimColour);

            GUILayout.BeginArea(new Rect(rect.x + 18f, rect.y + 14f, rect.width - 36f, rect.height - 24f));
            GUILayout.Label("<b>The supervisor reads the night back to you.</b>", _line);
            GUILayout.Space(6f);
            foreach (string line in director.ReviewLines) GUILayout.Label(line, _line);
            GUILayout.EndArea();
        }

        private void DrawNotices(ShiftDirector director)
        {
            if (director.Notices.Count == 0) return;

            float y = Screen.height * 0.5f - 130f;
            foreach (var notice in director.Notices)
            {
                float age = Time.time - notice.At;
                if (age > 5f) continue;

                GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01((5f - age) / 1.5f));
                GUI.Label(new Rect(0f, y, Screen.width, 22f), notice.Text, _cardSub);
                GUI.color = Color.white;
                y += 22f;
            }
        }

        // ---------------------------------------------------------------- drawing

        private void Fill(Rect rect, Color colour)
        {
            GUI.color = colour;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        private void Disc(Vector2 centre, float radius, Color colour)
        {
            GUI.color = colour;
            GUI.DrawTexture(new Rect(centre.x - radius, centre.y - radius, radius * 2f, radius * 2f), _disc);
            GUI.color = Color.white;
        }

        /// <summary>A rect rotated about the clock centre - hands, ticks and sweep wedges.</summary>
        private void Line(Vector2 centre, float degrees, float from, float to, float width, Color colour)
        {
            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(degrees, centre);
            Fill(new Rect(centre.x - width * 0.5f, centre.y - to, width, to - from), colour);
            GUI.matrix = matrix;
        }

        private void EnsureStyles()
        {
            if (_disc == null) _disc = BuildDisc(64);

            _time ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 17, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold
            };
            _phase ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 11, alignment = TextAnchor.MiddleCenter
            };
            _cardTitle ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 40, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold
            };
            _cardSub ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 16, alignment = TextAnchor.MiddleCenter
            };
            _line ??= new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true };
            _quota ??= new GUIStyle(GUI.skin.label)
            {
                fontSize = 15, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold
            };
        }

        /// <summary>A soft-edged white disc, tinted at draw time.</summary>
        private static Texture2D BuildDisc(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };

            float r = size * 0.5f;
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                float a = Mathf.Clamp01(r - d);          // one pixel of feather
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 255f));
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }
    }
}
