using System.Collections.Generic;
using UnityEngine;

namespace Probation.Surgery
{
    /// <summary>
    /// A line along which a body opens cleanly. The eventual replacement for SurgerySite, which
    /// was a named <em>point</em> - and a point cannot express the one thing a cut needs to be:
    /// a path you drag along, with somewhere to go wrong on either side of it.
    ///
    /// The seam is the safe route, not a track. Nothing stops a scalpel cutting anywhere else on
    /// the body; the seam is just the only place it opens instead of damaging what is underneath.
    /// That is what makes an unfamiliar species genuinely harder without a single new mechanic -
    /// you do not know where the line is yet.
    ///
    /// Local-only in this PR. Nothing here is networked yet: see PR 2.
    /// </summary>
    [ExecuteAlways]
    public class Seam : MonoBehaviour
    {
        [Tooltip("The path, in order. Authored as child transforms so it can be dragged in the scene view.")]
        [SerializeField] private Transform[] points;

        [Header("Look")]
        [SerializeField] private Color closedColour = new(0.45f, 0.13f, 0.16f, 1f);
        [SerializeField] private Color openColour = new(0.95f, 0.32f, 0.30f, 1f);
        [SerializeField] private Color tornColour = new(0.85f, 0.72f, 0.15f, 1f);
        [SerializeField] private float closedWidth = 0.004f;
        [SerializeField] private float openWidth = 0.03f;

        /// <summary>Every seam in the scene. Cheaper than searching, and the convention the ward already uses.</summary>
        public static readonly List<Seam> All = new();

        /// <summary>How far through each segment is cut, 0 to 1. One entry per gap between points.</summary>
        public float[] CutProgress { get; private set; } = System.Array.Empty<float>();

        /// <summary>Segments that were opened too fast. They never open properly again.</summary>
        public bool[] Torn { get; private set; } = System.Array.Empty<bool>();

        public int SegmentCount => points == null ? 0 : Mathf.Max(0, points.Length - 1);

        /// <summary>Mean cut across the whole seam. 1 is wide open.</summary>
        public float Openness
        {
            get
            {
                if (CutProgress.Length == 0) return 0f;
                float total = 0f;
                foreach (float p in CutProgress) total += p;
                return total / CutProgress.Length;
            }
        }

        /// <summary>Openness across every seam in the scene. For the spike readout only.</summary>
        public static float TotalOpenness
        {
            get
            {
                if (All.Count == 0) return 0f;
                float total = 0f;
                foreach (var seam in All) total += seam.Openness;
                return total / All.Count;
            }
        }

        private LineRenderer _line;
        private bool _dirty = true;

        private void OnEnable()
        {
            EnsureBuffers();
            _line = GetComponent<LineRenderer>();
            _dirty = true;
            if (!All.Contains(this)) All.Add(this);
        }

        private void OnDisable() => All.Remove(this);

        private void EnsureBuffers()
        {
            if (CutProgress.Length != SegmentCount) CutProgress = new float[SegmentCount];
            if (Torn.Length != SegmentCount) Torn = new bool[SegmentCount];
        }

        // ---------------------------------------------------------------- cutting

        /// <summary>
        /// Open a segment further. Returns how much actually landed, which is less than asked for
        /// once the segment is already open - so sawing back and forth over one spot stops paying,
        /// and covering new ground is the only way forward.
        /// </summary>
        public float Cut(int segment, float amount)
        {
            EnsureBuffers();
            if (segment < 0 || segment >= CutProgress.Length || amount <= 0f) return 0f;
            if (Torn[segment]) return 0f;

            float before = CutProgress[segment];
            CutProgress[segment] = Mathf.Clamp01(before + amount);
            _dirty = true;
            return CutProgress[segment] - before;
        }

        /// <summary>
        /// Ruin a segment. It keeps whatever it had opened and can never gain any more, so a torn
        /// seam is permanently visible as the one stretch that will not close over.
        /// </summary>
        public void Tear(int segment)
        {
            EnsureBuffers();
            if (segment < 0 || segment >= Torn.Length) return;

            Torn[segment] = true;
            _dirty = true;
        }

        /// <summary>Wipe the seam shut. Deliberately not called Reset - Unity claims that name.</summary>
        public void Close()
        {
            EnsureBuffers();
            for (int i = 0; i < CutProgress.Length; i++)
            {
                CutProgress[i] = 0f;
                Torn[i] = false;
            }
            _dirty = true;
        }

        // ---------------------------------------------------------------- queries

        /// <summary>
        /// The nearest seam to a world point, within range. Null when the tip is nowhere near one.
        /// </summary>
        public static Seam Nearest(Vector3 world, float maxDistance, out int segment,
                                   out Vector3 onSeam, out Vector3 tangent, out float deviation)
        {
            segment = -1;
            onSeam = world;
            tangent = Vector3.forward;
            deviation = float.PositiveInfinity;

            Seam best = null;

            foreach (var seam in All)
            {
                if (seam == null) continue;
                if (!seam.NearestPoint(world, out int s, out Vector3 p, out Vector3 t, out float d)) continue;
                if (d > maxDistance || d >= deviation) continue;

                best = seam;
                segment = s;
                onSeam = p;
                tangent = t;
                deviation = d;
            }

            return best;
        }

        /// <summary>Nearest point on this seam to a world position.</summary>
        public bool NearestPoint(Vector3 world, out int segment, out Vector3 onSeam,
                                 out Vector3 tangent, out float distance)
        {
            segment = -1;
            onSeam = world;
            tangent = Vector3.forward;
            distance = float.PositiveInfinity;

            if (points == null || points.Length < 2) return false;

            for (int i = 0; i < points.Length - 1; i++)
            {
                if (points[i] == null || points[i + 1] == null) continue;

                Vector3 a = points[i].position;
                Vector3 ab = points[i + 1].position - a;

                float lengthSq = ab.sqrMagnitude;
                if (lengthSq < 1e-8f) continue;

                float t = Mathf.Clamp01(Vector3.Dot(world - a, ab) / lengthSq);
                Vector3 candidate = a + ab * t;
                float d = Vector3.Distance(world, candidate);

                if (d >= distance) continue;

                distance = d;
                segment = i;
                onSeam = candidate;
                tangent = ab / Mathf.Sqrt(lengthSq);
            }

            return segment >= 0;
        }

        public float SegmentLength(int segment)
        {
            if (points == null || segment < 0 || segment + 1 >= points.Length) return 0f;
            if (points[segment] == null || points[segment + 1] == null) return 0f;
            return Vector3.Distance(points[segment].position, points[segment + 1].position);
        }

        // ---------------------------------------------------------------- look

        private void LateUpdate()
        {
            if (_line == null || points == null || points.Length < 2) return;

            EnsureBuffers();

            _line.useWorldSpace = true;
            if (_line.positionCount != points.Length) _line.positionCount = points.Length;

            for (int i = 0; i < points.Length; i++)
                if (points[i] != null) _line.SetPosition(i, points[i].position);

            // Width and colour only change when somebody cuts, so they are not rebuilt per frame -
            // an AnimationCurve and a Gradient every frame is a surprising amount of garbage for
            // a line that usually is not moving.
            if (!_dirty) return;
            _dirty = false;

            var widths = new AnimationCurve();
            var colours = new GradientColorKey[Mathf.Min(points.Length, 8)];
            var alphas = new GradientAlphaKey[colours.Length];

            for (int i = 0; i < points.Length; i++)
            {
                float t = points.Length > 1 ? i / (float)(points.Length - 1) : 0f;
                widths.AddKey(t, Mathf.Lerp(closedWidth, openWidth, VertexOpenness(i)));
            }

            // Gradients cap at 8 keys, so sample the seam evenly rather than one key per vertex.
            for (int k = 0; k < colours.Length; k++)
            {
                float t = colours.Length > 1 ? k / (float)(colours.Length - 1) : 0f;
                int vertex = Mathf.RoundToInt(t * (points.Length - 1));

                Color c = VertexTorn(vertex)
                    ? tornColour
                    : Color.Lerp(closedColour, openColour, VertexOpenness(vertex));

                colours[k] = new GradientColorKey(c, t);
                alphas[k] = new GradientAlphaKey(1f, t);
            }

            _line.widthCurve = widths;

            var gradient = new Gradient();
            gradient.SetKeys(colours, alphas);
            _line.colorGradient = gradient;
        }

        /// <summary>A vertex is as open as the segments either side of it.</summary>
        private float VertexOpenness(int vertex)
        {
            bool hasLeft = vertex - 1 >= 0 && vertex - 1 < CutProgress.Length;
            bool hasRight = vertex >= 0 && vertex < CutProgress.Length;

            if (hasLeft && hasRight) return (CutProgress[vertex - 1] + CutProgress[vertex]) * 0.5f;
            if (hasLeft) return CutProgress[vertex - 1];
            if (hasRight) return CutProgress[vertex];
            return 0f;
        }

        private bool VertexTorn(int vertex)
        {
            bool left = vertex - 1 >= 0 && vertex - 1 < Torn.Length && Torn[vertex - 1];
            bool right = vertex >= 0 && vertex < Torn.Length && Torn[vertex];
            return left || right;
        }

        private void OnDrawGizmos()
        {
            if (points == null) return;

            for (int i = 0; i < points.Length; i++)
            {
                if (points[i] == null) continue;

                Gizmos.color = new Color(0.95f, 0.32f, 0.30f, 0.9f);
                Gizmos.DrawSphere(points[i].position, 0.008f);

                if (i + 1 >= points.Length || points[i + 1] == null) continue;
                Gizmos.color = new Color(0.95f, 0.32f, 0.30f, 0.35f);
                Gizmos.DrawLine(points[i].position, points[i + 1].position);
            }
        }
    }
}
