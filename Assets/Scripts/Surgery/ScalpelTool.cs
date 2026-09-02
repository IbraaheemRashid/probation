using Probation.Interaction;
using Probation.Player;
using UnityEngine;

namespace Probation.Surgery
{
    /// <summary>
    /// Cut. The first of the four verbs, and the one everything else is measured against.
    ///
    /// Two rules, and the whole skill is in the gap between them:
    ///   - you cut what you drag over, not what you hold still on. Progress is coverage, not a
    ///     timer, the way PowerWash Simulator measures a clean panel. A progress bar is what you
    ///     ship when there is no mechanic; this is the mechanic.
    ///   - speed tears. There is no button for "carefully" - going slowly IS going carefully.
    ///
    /// The instrument is never teleported to the cursor. PlayerBrace moves the hand anchor and
    /// PlayerCarry's velocity tracking drags the rigidbody after it against a mass-derived speed
    /// ceiling, so a heavy instrument overshoots when you snap the mouse. Tearing is therefore not
    /// simulated anywhere - it falls out of the carry physics that already existed.
    ///
    /// Local-only in this PR. Nothing here is networked yet: see PR 2.
    /// </summary>
    [RequireComponent(typeof(Grabbable))]
    public class ScalpelTool : MonoBehaviour
    {
        [Header("Cut")]
        [Tooltip("How far ACROSS the seam the tip may stray and still be opening it rather than damaging what is underneath.")]
        [SerializeField] private float cutRadius = 0.03f;
        [Tooltip("How far the tip may sit off the surface and still be in contact. Measured along the work plane's normal, so holding the blade near the body is forgiving while wandering sideways off the seam is not.")]
        [SerializeField] private float contactDepth = 0.06f;
        [Tooltip("Tip speed at which a drag stops opening and starts tearing. The difficulty knob.")]
        [SerializeField] private float tearSpeed = 0.35f;
        [Tooltip("Fraction of tearSpeed where the resistance starts to be audible. Below 1, always - you must be able to hear the tear coming.")]
        [Range(0.1f, 0.95f)] [SerializeField] private float resistanceOnset = 0.6f;
        [Tooltip("Ignore the seam entirely beyond this. Stops a tip halfway across the room grabbing a seam.")]
        [SerializeField] private float seamSearchRadius = 0.35f;

        [Header("Feel")]
        [SerializeField] private float resistanceVolume = 0.55f;
        [SerializeField] private float minPitch = 0.7f;
        [SerializeField] private float maxPitch = 1.7f;

        [Header("Spike")]
        [Tooltip("Tuning readout. Off in the real ward - every readout belongs on the instrument, never on a HUD.")]
        [SerializeField] private bool showDebug;

        /// <summary>Tip speed last frame, m/s. Read by the debug overlay while tuning.</summary>
        public float TipSpeed { get; private set; }
        public int Tears { get; private set; }
        public int StraySlices { get; private set; }

        private Grabbable _grabbable;
        private Transform _tip;
        private AudioSource _audio;

        private PlayerBrace _brace;
        private PlayerInputReader _input;

        private Vector3 _lastTip;
        private bool _tracking;
        private float _loudness;

        private void Awake()
        {
            _grabbable = GetComponent<Grabbable>();

            var tip = GetComponentInChildren<ToolTip>();
            _tip = tip != null ? tip.transform : transform;

            _audio = GetComponent<AudioSource>();
            if (_audio == null) _audio = gameObject.AddComponent<AudioSource>();

            _audio.playOnAwake = false;
            _audio.loop = true;
            _audio.spatialBlend = 1f;
            _audio.volume = 0f;
            _audio.minDistance = 0.5f;
            _audio.maxDistance = 8f;

            // Synthesised rather than authored, the same way VitalsMonitor makes its beeps. A
            // dragged blade is broadband, so this is smoothed noise rather than a tone.
            _audio.clip = Resistance();
            _audio.Play();
        }

        private void Update()
        {
            bool cutting = Resolve() && _brace.IsBraced && _brace.Instrument == _grabbable;

            if (!cutting)
            {
                _tracking = false;
                Fade(0f);
                return;
            }

            Vector3 tip = _tip.position;

            // First frame of a brace has no travel to measure, and using a stale position from
            // before you leaned in would read as one enormous instantaneous drag.
            if (!_tracking)
            {
                _lastTip = tip;
                _tracking = true;
                return;
            }

            float dt = Time.deltaTime;
            Vector3 travel = tip - _lastTip;
            _lastTip = tip;

            TipSpeed = dt > 0f ? travel.magnitude / dt : 0f;

            // Resistance is audible whether or not you are pressing, so you can learn how fast is
            // too fast by moving the blade around before you ever commit to a cut.
            Fade(Mathf.InverseLerp(tearSpeed * resistanceOnset, tearSpeed, TipSpeed));

            if (_input == null || !_input.Attack) return;

            Cut(tip, travel);
        }

        private void Cut(Vector3 tip, Vector3 travel)
        {
            Seam seam = Seam.Nearest(tip, seamSearchRadius, out int segment, out Vector3 onSeam,
                                     out Vector3 tangent, out float deviation);
            if (seam == null) return;

            // Split the miss into "off the surface" and "off the line". They are different
            // mistakes and only one of them is a mistake: an instrument hovering a centimetre
            // proud of the body is just an instrument being held, whereas one that has wandered
            // sideways is in the wrong place, and the two must not be measured with one radius.
            Vector3 delta = tip - onSeam;
            Vector3 normal = _brace.PlaneNormal;

            float off = Vector3.Dot(delta, normal);
            if (Mathf.Abs(off) > contactDepth) return;          // not touching them at all

            deviation = (delta - normal * off).magnitude;

            if (deviation > cutRadius)
            {
                // You are cutting the body, not the seam. In this PR that is a mark and a noise;
                // once there is a body model underneath it is whatever was in the way.
                StraySlices++;
                Stray(onSeam);
                return;
            }

            if (TipSpeed > tearSpeed)
            {
                Tears++;
                seam.Tear(segment);
                Tear(tip);
                return;
            }

            float length = seam.SegmentLength(segment);
            if (length <= 0f) return;

            // Only travel ALONG the seam counts. Wobbling across it does nothing, which is what
            // makes a steady hand read as a steady hand.
            float along = Mathf.Abs(Vector3.Dot(travel, tangent));
            seam.Cut(segment, along / length);
        }

        // ---------------------------------------------------------------- feel

        private void Fade(float target)
        {
            _loudness = Mathf.MoveTowards(_loudness, Mathf.Clamp01(target), Time.deltaTime * 6f);
            if (_audio == null) return;

            _audio.volume = _loudness * resistanceVolume;
            _audio.pitch = Mathf.Lerp(minPitch, maxPitch, _loudness);
        }

        private void Tear(Vector3 at)
        {
            if (_audio != null) _audio.PlayOneShot(_audio.clip, 0.9f);
            Debug.DrawRay(at, Vector3.up * 0.08f, Color.red, 3f);
        }

        private void Stray(Vector3 at)
        {
            Debug.DrawRay(at, Vector3.up * 0.04f, new Color(1f, 0.6f, 0f), 1.5f);
        }

        /// <summary>One second of smoothed noise, looped. Blade against tissue, near enough to tune against.</summary>
        private static AudioClip Resistance()
        {
            const int rate = 44100;
            var samples = new float[rate];

            var random = new System.Random(4);
            float previous = 0f;

            for (int i = 0; i < samples.Length; i++)
            {
                float white = (float)(random.NextDouble() * 2.0 - 1.0);

                // A one-pole low pass. Raw white noise is a hiss; this is a rasp.
                previous = Mathf.Lerp(previous, white, 0.18f);
                samples[i] = previous * 1.8f;
            }

            // Crossfade the tail into the head so the loop has no click in it.
            int blend = rate / 20;
            for (int i = 0; i < blend; i++)
            {
                float t = i / (float)blend;
                samples[i] = Mathf.Lerp(samples[samples.Length - blend + i], samples[i], t);
            }

            var clip = AudioClip.Create("ScalpelResistance", samples.Length, 1, rate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        // ---------------------------------------------------------------- plumbing

        private bool Resolve()
        {
            if (_brace != null && _input != null) return true;

            var local = PlayerNetworkSetup.Local;
            if (local == null) return false;

            _brace = local.GetComponent<PlayerBrace>();
            _input = local.GetComponent<PlayerInputReader>();
            return _brace != null && _input != null;
        }

        private void OnGUI()
        {
            // Only the instrument actually in your hand draws. A bench with three scalpels on it
            // would otherwise stack three readouts in the same corner.
            if (!showDebug || _brace == null || _brace.Instrument != _grabbable) return;

            GUILayout.BeginArea(new Rect(Screen.width - 232f, 12f, 220f, 130f), GUI.skin.box);
            GUILayout.Label($"{_grabbable.DisplayName}");
            GUILayout.Label($"tip      {TipSpeed:0.00} m/s");
            GUILayout.Label($"tear at  {tearSpeed:0.00} m/s");
            GUILayout.Label($"openness {Seam.TotalOpenness:0.00}");
            GUILayout.Label($"tears    {Tears}");
            GUILayout.Label($"strays   {StraySlices}");
            GUILayout.EndArea();
        }
    }
}
