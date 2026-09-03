using System.Collections.Generic;
using UnityEngine;

namespace Probation.Surgery
{
    /// <summary>
    /// Somewhere the blade went that it should not have.
    ///
    /// The important property is not that it bleeds - it is that it bleeds *somewhere*. Bleeding
    /// as a single number on a patient (which is what Patient._bleedRate is today) makes stopping
    /// it a chore anybody can do from anywhere. A wound at a point makes somebody walk over and
    /// put a hand on THAT ONE, which is what turns a clock into a job worth shouting about.
    ///
    /// Nothing here can kill anything. An unattended wound gets worse and stays worse, and that
    /// is the whole stake for now - a blood clock is an abstraction over wounds, and it belongs
    /// with the body model rather than stacked on top of this in the same change.
    /// </summary>
    public class Wound : MonoBehaviour
    {
        [Tooltip("How fast an unattended wound worsens, in severity per second. Under pressure it stops.")]
        [SerializeField] private float worsenPerSecond = 0.06f;
        [Tooltip("Radius of the visible wound at severity 1.")]
        [SerializeField] private float maxRadius = 0.055f;
        [Tooltip("Bright on purpose. This is the one thing that has to be unmissable - if you cannot see where you went wrong, the mechanic may as well not exist.")]
        [SerializeField] private Color colour = new(0.72f, 0.04f, 0.06f, 1f);

        /// <summary>Every wound in the scene, open or not.</summary>
        public static readonly List<Wound> All = new();

        /// <summary>If merging is working you never come close to this. If you hit it, it is not.</summary>
        private const int HardCap = 64;

        private static Transform _container;

        private static Transform Container()
        {
            if (_container != null) return _container;

            var existing = GameObject.Find("Wounds");
            _container = existing != null ? existing.transform : new GameObject("Wounds").transform;
            return _container;
        }

        public bool IsOpen { get; private set; }

        /// <summary>0 to 1. Deepens when cut again and while left alone.</summary>
        public float Severity { get; private set; }

        /// <summary>
        /// True while a hand is on it. Held by timestamp rather than a flag somebody has to
        /// remember to clear, so a holder who walks off, drops dead or disconnects releases it
        /// on their own.
        /// </summary>
        public bool UnderPressure => Time.time - _heldAt < 0.15f;

        private float _heldAt = float.NegativeInfinity;
        private Transform _visual;
        private ParticleSystem _blood;
        private ParticleSystem.EmissionModule _emission;

        public static int OpenCount
        {
            get
            {
                int n = 0;
                foreach (var w in All) if (w != null && w.IsOpen) n++;
                return n;
            }
        }

        private void Awake()
        {
            BuildVisual();
            BuildBlood();
            Close();
        }

        private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        private void OnDisable() => All.Remove(this);

        // ---------------------------------------------------------------- opening

        /// <summary>
        /// Cut something at this point.
        ///
        /// Merges into a nearby wound rather than making a new one. Straying is measured per
        /// frame, so without this a bad second would leave sixty wounds in a line - which reads
        /// as chaos rather than as one mistake.
        /// </summary>
        public static Wound OpenAt(Vector3 point, Vector3 normal, float amount, float mergeRadius)
        {
            Wound nearest = null;
            float best = mergeRadius;

            foreach (var wound in All)
            {
                if (wound == null || !wound.IsOpen) continue;

                float distance = Vector3.Distance(wound.transform.position, point);
                if (distance > best) continue;

                best = distance;
                nearest = wound;
            }

            if (nearest != null)
            {
                nearest.Deepen(amount);
                return nearest;
            }

            foreach (var wound in All)
            {
                if (wound == null || wound.IsOpen) continue;

                wound.Begin(point, normal, amount);
                return wound;
            }

            // Nothing free. Grow the pool rather than dropping the consequence on the floor.
            //
            // A scene authored before wounds existed has none at all, and the failure mode is the
            // worst kind: you cut badly, nothing happens, and there is no error anywhere to say
            // the pool was empty. Wounds are not networked, so making one costs nothing but a
            // GameObject - and a consequence that silently does not happen is not a consequence.
            if (All.Count < HardCap)
            {
                var go = new GameObject($"Wound {All.Count}");
                go.transform.SetParent(Container(), false);

                var made = go.AddComponent<Wound>();
                made.Begin(point, normal, amount);
                return made;
            }

            // Genuinely saturated. Deepening the nearest is a better failure than nothing at all.
            Wound fallback = Closest(point);
            fallback?.Deepen(amount);
            return fallback;
        }

        public static Wound Closest(Vector3 point)
        {
            Wound best = null;
            float distance = float.PositiveInfinity;

            foreach (var wound in All)
            {
                if (wound == null || !wound.IsOpen) continue;

                float d = Vector3.Distance(wound.transform.position, point);
                if (d >= distance) continue;

                distance = d;
                best = wound;
            }

            return best;
        }

        /// <summary>Nearest open wound to a point, within range. For a hand looking for something to hold.</summary>
        public static Wound NearestWithin(Vector3 point, float range)
        {
            Wound best = Closest(point);
            if (best == null) return null;

            return Vector3.Distance(best.transform.position, point) <= range ? best : null;
        }

        private void Begin(Vector3 point, Vector3 normal, float amount)
        {
            transform.position = point;
            if (normal.sqrMagnitude > 1e-6f) transform.rotation = Quaternion.LookRotation(normal);

            IsOpen = true;
            Severity = Mathf.Clamp01(amount);
            _heldAt = float.NegativeInfinity;

            Show();
        }

        public void Deepen(float amount)
        {
            if (!IsOpen || amount <= 0f) return;

            Severity = Mathf.Clamp01(Severity + amount);
            Show();
        }

        /// <summary>A hand is on it. Called every frame it is held.</summary>
        public void HoldPressure() => _heldAt = Time.time;

        public void Close()
        {
            IsOpen = false;
            Severity = 0f;
            _heldAt = float.NegativeInfinity;
            Show();
        }

        public static void CloseAll()
        {
            foreach (var wound in All) if (wound != null) wound.Close();
        }

        // ---------------------------------------------------------------- tick

        private void Update()
        {
            if (!IsOpen) return;

            // Left alone it gets worse; a hand on it holds it where it is. No clock, no death -
            // just a reason to deal with it that you can see getting bigger.
            if (!UnderPressure && worsenPerSecond > 0f)
            {
                Severity = Mathf.Clamp01(Severity + worsenPerSecond * Time.deltaTime);
                Show();
            }

            if (_blood != null)
            {
                // Bleeding stops the moment somebody is holding it. That is the entire feedback
                // for whether the job is being done, and it has to be instant to read.
                _emission.rateOverTime = UnderPressure ? 0f : Mathf.Lerp(25f, 110f, Severity);
            }
        }

        private void Show()
        {
            if (_visual != null)
            {
                _visual.gameObject.SetActive(IsOpen);
                float r = Mathf.Lerp(maxRadius * 0.35f, maxRadius, Severity);
                _visual.localScale = new Vector3(r * 2f, r * 2f, r * 0.6f);
            }

            if (_blood == null) return;

            if (IsOpen && !_blood.isPlaying) _blood.Play();
            else if (!IsOpen && _blood.isPlaying) _blood.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        // ---------------------------------------------------------------- built in code

        private void BuildVisual()
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Mark";
            Destroy(quad.GetComponent<Collider>());

            quad.transform.SetParent(transform, false);

            // PROUD of the surface, not into it. Begin() aims local +Z down the surface normal,
            // so a negative offset here buries the mark inside the body and nothing renders.
            quad.transform.localPosition = new Vector3(0f, 0f, 0.003f);
            quad.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

            var renderer = quad.GetComponent<MeshRenderer>();
            renderer.material = new Material(Shader.Find("Sprites/Default")) { color = colour };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            _visual = quad.transform;
        }

        private void BuildBlood()
        {
            _blood = GetComponent<ParticleSystem>();
            if (_blood == null) _blood = gameObject.AddComponent<ParticleSystem>();

            var main = _blood.main;
            main.startLifetime = 1.1f;
            main.startSpeed = 0.7f;
            main.startSize = 0.022f;
            main.startColor = colour;
            main.gravityModifier = 1.1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;
            main.maxParticles = 200;

            var shape = _blood.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 22f;
            shape.radius = 0.004f;

            _emission = _blood.emission;
            _emission.rateOverTime = 0f;

            var renderer = GetComponent<ParticleSystemRenderer>();
            renderer.material = new Material(Shader.Find("Sprites/Default")) { color = colour };
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }
    }
}
