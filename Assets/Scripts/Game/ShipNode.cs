using System.Collections.Generic;
using UnityEngine;

namespace Probation.Game
{
    /// <summary>
    /// A place on the ship something can walk to, and the graph they walk along.
    ///
    /// There is no NavMesh here and deliberately so: the ship is generated from code, and baking
    /// a mesh every time somebody nudges a wall is a worse problem than the one it solves. A
    /// dozen nodes at room centres and doorways describes this ship completely, and a parasite
    /// pathing along it uses the doors and the loops rather than walking into a wall and vibrating.
    ///
    /// Links are worked out at runtime by line of sight rather than authored, so a map somebody
    /// builds by hand works as long as they drop nodes in the rooms. Nothing about this is tied to
    /// the generated layout.
    /// </summary>
    public class ShipNode : MonoBehaviour
    {
        [Tooltip("Two nodes further apart than this never link, however clear the line between them.")]
        [SerializeField] private float linkRange = 16f;

        public static readonly List<ShipNode> All = new();

        /// <summary>Nodes reachable from this one in a straight line.</summary>
        public readonly List<ShipNode> Links = new();

        private void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
            _linked = false;
        }

        private void OnDisable()
        {
            All.Remove(this);
            _linked = false;
        }

        private static bool _linked;

        /// <summary>
        /// Work out who can see whom. Cheap enough to do once, and re-done if the set changes.
        ///
        /// Raised a metre off the floor so the cast clears floor markings and door sills, and
        /// ignoring triggers so the intake, discharge and operating volumes - which fill entire
        /// rooms - do not read as walls.
        /// </summary>
        public static void EnsureLinked()
        {
            if (_linked) return;
            _linked = true;

            foreach (var node in All) node.Links.Clear();

            for (int i = 0; i < All.Count; i++)
            {
                for (int j = i + 1; j < All.Count; j++)
                {
                    ShipNode a = All[i], b = All[j];
                    if (a == null || b == null) continue;

                    float range = Mathf.Min(a.linkRange, b.linkRange);
                    Vector3 from = a.transform.position + Vector3.up;
                    Vector3 to = b.transform.position + Vector3.up;

                    if ((to - from).sqrMagnitude > range * range) continue;
                    if (Physics.Linecast(from, to, ~0, QueryTriggerInteraction.Ignore)) continue;

                    a.Links.Add(b);
                    b.Links.Add(a);
                }
            }
        }

        public static ShipNode Nearest(Vector3 point)
        {
            ShipNode best = null;
            float bestDistance = float.PositiveInfinity;

            foreach (var node in All)
            {
                if (node == null) continue;

                float d = (node.transform.position - point).sqrMagnitude;
                if (d >= bestDistance) continue;

                bestDistance = d;
                best = node;
            }

            return best;
        }

        /// <summary>
        /// The next node to walk to, heading from one node towards another.
        ///
        /// Breadth-first rather than A*, because the graph is a dozen nodes and the difference is
        /// unmeasurable. Returns the *step*, not the whole route, so a hunter re-asks every time
        /// it arrives somewhere and follows a target that has moved without any replanning code.
        /// </summary>
        public static ShipNode StepFrom(ShipNode from, ShipNode to)
        {
            if (from == null || to == null || from == to) return to;

            EnsureLinked();

            _cameFrom.Clear();
            _queue.Clear();
            _queue.Enqueue(from);
            _cameFrom[from] = null;

            while (_queue.Count > 0)
            {
                var current = _queue.Dequeue();
                if (current == to) break;

                foreach (var next in current.Links)
                {
                    if (next == null || _cameFrom.ContainsKey(next)) continue;

                    _cameFrom[next] = current;
                    _queue.Enqueue(next);
                }
            }

            if (!_cameFrom.ContainsKey(to)) return null;      // nothing connects them

            // Walk the chain back until the node whose parent is where we started.
            var step = to;
            while (_cameFrom[step] != null && _cameFrom[step] != from) step = _cameFrom[step];
            return step;
        }

        private static readonly Dictionary<ShipNode, ShipNode> _cameFrom = new();
        private static readonly Queue<ShipNode> _queue = new();

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.4f, 0.8f, 0.9f, 0.8f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.2f, 0.25f);

            foreach (var link in Links)
                if (link != null)
                    Gizmos.DrawLine(transform.position + Vector3.up * 0.2f,
                                    link.transform.position + Vector3.up * 0.2f);
        }
    }
}
