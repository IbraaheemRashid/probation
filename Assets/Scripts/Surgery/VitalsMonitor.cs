using Probation.Game;
using UnityEngine;

namespace Probation.Surgery
{
    /// <summary>
    /// A monitor cart. Silent until it is connected to somebody.
    ///
    /// It is a separate object you have to bring to a patient rather than a noise the patient
    /// emits. Connecting it is a real prep action with a real payoff - the room gains its
    /// shared clock - and forgetting to is a mistake you can make.
    ///
    /// Positional with a short range, so a monitor belongs to its operating room. You can hear
    /// that OR 2 is going badly without being able to see why.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class VitalsMonitor : MonoBehaviour
    {
        [Tooltip("How close the cart has to be to a patient to pick up a trace.")]
        [SerializeField] private float connectRange = 2.2f;
        [SerializeField] private float maxAudibleDistance = 12f;
        [SerializeField] private float volume = 0.35f;

        public Patient Connected { get; private set; }

        private AudioSource _source;
        private AudioClip _beep;
        private AudioClip _flatline;
        private float _nextBeat;
        private bool _flatlined;

        private void Awake()
        {
            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 1f;
            _source.rolloffMode = AudioRolloffMode.Linear;
            _source.minDistance = 1.5f;
            _source.maxDistance = maxAudibleDistance;
            _source.volume = volume;

            _beep = Blip(760f, 0.045f);
            _flatline = Blip(320f, 1.1f, decay: 1.2f);
        }

        private void Update()
        {
            UpdateConnection();
            if (Connected == null) return;

            if (Connected.IsDead)
            {
                if (_flatlined) return;
                _flatlined = true;
                _source.pitch = 1f;
                _source.PlayOneShot(_flatline);
                return;
            }

            _flatlined = false;

            float rate = Connected.HeartRate;
            if (rate <= 1f || Time.time < _nextBeat) return;

            _nextBeat = Time.time + 60f / rate;
            _source.pitch = Mathf.Lerp(1f, 1.25f, Mathf.InverseLerp(60f, 190f, rate));
            _source.PlayOneShot(_beep);
        }

        private void UpdateConnection()
        {
            Patient nearest = null;
            float best = connectRange * connectRange;

            foreach (var patient in Patient.All)
            {
                if (patient == null) continue;
                float d = (patient.transform.position - transform.position).sqrMagnitude;
                if (d >= best) continue;
                best = d;
                nearest = patient;
            }

            if (nearest == Connected) return;

            Connected = nearest;
            _nextBeat = 0f;
            _flatlined = false;

            if (Connected != null) ShiftDirector.Instance?.Announce("Monitor connected.");
        }

        /// <summary>
        /// A short blip with an exponential decay. Quiet, low, and over quickly - the previous
        /// version was a full-amplitude sine window and it was genuinely unpleasant.
        /// </summary>
        private static AudioClip Blip(float frequency, float seconds, float decay = 28f)
        {
            const int sampleRate = 44100;
            int samples = Mathf.Max(1, Mathf.RoundToInt(sampleRate * seconds));
            var data = new float[samples];

            for (int i = 0; i < samples; i++)
            {
                float t = (float)i / sampleRate;

                // Fade the attack in over 2 ms so it does not click, then decay away.
                float attack = Mathf.Clamp01(t / 0.002f);
                float envelope = attack * Mathf.Exp(-decay * t);

                data[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * envelope * 0.5f;
            }

            var clip = AudioClip.Create($"blip_{frequency:0}", samples, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
