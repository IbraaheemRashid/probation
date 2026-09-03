using UnityEngine;

namespace Probation.Surgery
{
    /// <summary>
    /// Drives the flesh shader from the patient's actual state.
    ///
    /// The point of doing this rather than authoring a looping animation: the glow beats at the
    /// patient's real heart rate, which is already simulated and already networked. A patient in
    /// trouble is visibly racing from the far end of the ward, so the information the monitor
    /// gives you also exists in the world - and unlike the monitor, you cannot lose it or wheel
    /// it into the wrong room.
    ///
    /// It costs one float per patient per frame.
    /// </summary>
    [RequireComponent(typeof(Patient))]
    public class PatientAppearance : MonoBehaviour
    {
        [Tooltip("How sharply each beat decays. Higher is a tighter thump.")]
        [SerializeField] private float beatSharpness = 22f;
        [Tooltip("Gap between the lub and the dub, as a fraction of one beat.")]
        [SerializeField] private float secondBeatAt = 0.17f;
        [SerializeField] private float secondBeatStrength = 0.55f;

        private static readonly int PulseId = Shader.PropertyToID("_Pulse");
        private static readonly int SicknessId = Shader.PropertyToID("_Sickness");

        private Patient _patient;
        private Renderer[] _renderers;
        private MaterialPropertyBlock _block;
        private float _phase;

        private void Awake()
        {
            _patient = GetComponent<Patient>();
            _renderers = GetComponentsInChildren<Renderer>(true);
            _block = new MaterialPropertyBlock();
        }

        private void Update()
        {
            if (_renderers == null || _renderers.Length == 0) return;

            float rate = _patient.HeartRate;
            float pulse = 0f;

            if (!_patient.IsDead && rate > 1f)
            {
                // Advance a 0..1 phase once per beat, so the rhythm follows the real rate
                // rather than a fixed animation speed.
                _phase += Time.deltaTime * (rate / 60f);
                _phase -= Mathf.Floor(_phase);

                pulse = Beat(_phase);
            }

            // Harm reads as the colour draining towards something jaundiced. Deliberately not a
            // bar - you notice a patient looking wrong before you could ever read a number.
            float sickness = Mathf.Clamp01(_patient.Harm);

            foreach (var renderer in _renderers)
            {
                if (renderer == null) continue;

                renderer.GetPropertyBlock(_block);
                _block.SetFloat(PulseId, pulse);
                _block.SetFloat(SicknessId, sickness);
                renderer.SetPropertyBlock(_block);
            }
        }

        /// <summary>
        /// Lub-dub. Two decaying spikes rather than a sine, because a sine reads as breathing
        /// and a heart does not look like that.
        /// </summary>
        private float Beat(float phase)
        {
            float lub = Mathf.Exp(-beatSharpness * phase);
            float dub = phase < secondBeatAt
                ? 0f
                : Mathf.Exp(-beatSharpness * (phase - secondBeatAt)) * secondBeatStrength;

            return Mathf.Clamp01(lub + dub);
        }
    }
}
