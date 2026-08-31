using System.Collections.Generic;
using Probation.Game;
using Probation.Interaction;
using Unity.Netcode;
using UnityEngine;

namespace Probation.Surgery
{
    /// <summary>
    /// Runs one <see cref="Procedure"/> on one <see cref="Patient"/>.
    ///
    /// Evaluated on the host only. Clients own their tools and therefore ship tool transforms;
    /// the host reads them and decides when a step completes. Anything client-reported would
    /// make the end-of-shift review corruptible, and the review is the payoff scene.
    ///
    /// Every completion test is a tolerance band - near enough, for long enough. The host is
    /// always judging slightly stale tool positions, so exact contact tests would feel
    /// unreliable to everybody who is not hosting.
    /// </summary>
    [RequireComponent(typeof(Patient))]
    public class Operation : NetworkBehaviour
    {
        [SerializeField] private Procedure procedure;
        [Tooltip("Seconds before a wrong tool at the site can hurt the patient again.")]
        [SerializeField] private float wrongToolCooldown = 1.25f;

        private readonly NetworkVariable<int> _stepIndex = new();
        private readonly NetworkVariable<float> _progress = new();
        private readonly NetworkVariable<bool> _finished = new();

        public Procedure Procedure => procedure;
        public int StepIndex => _stepIndex.Value;
        public float Progress => _progress.Value;
        public bool Finished => _finished.Value;

        public ProcedureStep CurrentStep =>
            procedure != null && _stepIndex.Value < procedure.steps.Count
                ? procedure.steps[_stepIndex.Value]
                : null;

        private Patient _patient;
        private SurgerySite[] _sites;

        private readonly Collider[] _overlap = new Collider[32];
        private readonly HashSet<ulong> _holders = new();
        private readonly List<Grabbable> _usedThisStep = new();
        private float _nextHarmAllowedAt;

        private void Awake()
        {
            _patient = GetComponent<Patient>();
            _sites = GetComponentsInChildren<SurgerySite>(true);
        }

        private void FixedUpdate()
        {
            if (!IsServer || _finished.Value || _patient.IsDead) return;
            if (procedure == null || procedure.steps.Count == 0) return;

            Evaluate(CurrentStep);
        }

        private void Evaluate(ProcedureStep step)
        {
            if (step == null) return;

            Transform site = SiteFor(step.targetSite);
            if (site == null) return;

            _holders.Clear();
            bool wrongToolPresent = false;

            int count = Physics.OverlapSphereNonAlloc(
                site.position, step.tolerance, _overlap, ~0, QueryTriggerInteraction.Collide);

            for (int i = 0; i < count; i++)
            {
                var grabbable = _overlap[i].GetComponentInParent<Grabbable>();
                if (grabbable == null || !grabbable.IsHeld) continue;

                if (grabbable.ToolId == step.requiredToolId)
                {
                    // A dirty instrument is the wrong instrument. Somebody has to have taken it
                    // to the steriliser, and in a busy ward somebody usually has not.
                    if (grabbable.IsDirty)
                    {
                        wrongToolPresent = true;
                        continue;
                    }

                    _holders.Add(grabbable.HeldBy);
                    if (!_usedThisStep.Contains(grabbable)) _usedThisStep.Add(grabbable);

                    // A species rule mutating an existing procedure - the cheapest content in
                    // the design, and nothing about the procedure had to change to allow it.
                    if (_patient.ObjectsToMetal && grabbable.IsMetal && Time.time >= _nextHarmAllowedAt)
                    {
                        _nextHarmAllowedAt = Time.time + wrongToolCooldown;
                        _patient.ApplyHarm(0.08f, grabbable.HeldBy,
                            $"used metal on a species that cannot take it");
                    }
                }
                else if (!string.IsNullOrEmpty(grabbable.ToolId))
                {
                    wrongToolPresent = true;
                    if (Time.time >= _nextHarmAllowedAt)
                    {
                        _nextHarmAllowedAt = Time.time + wrongToolCooldown;
                        _patient.ApplyHarm(step.wrongToolHarm, grabbable.HeldBy,
                            $"used the {grabbable.DisplayName} on the {step.targetSite}");
                    }
                }
            }

            // Operating on someone who is awake is allowed. It just hurts them, and everyone
            // in the room finds out - which is the anaesthetist's liability made audible.
            if (step.requiresUnconscious && _patient.IsConscious && _holders.Count > 0
                && Time.time >= _nextHarmAllowedAt)
            {
                _nextHarmAllowedAt = Time.time + wrongToolCooldown;
                foreach (ulong holder in _holders)
                    _patient.ApplyHarm(0.1f, holder, "operated on a patient who was still awake");
                ShiftDirector.Instance?.Announce("IT IS AWAKE.");
            }

            // Wrong tool undoes progress rather than blocking input. The step never refuses you;
            // it just gets further away while you do the wrong thing.
            if (wrongToolPresent)
            {
                _progress.Value = Mathf.MoveTowards(_progress.Value, 0f, Time.fixedDeltaTime / step.holdSeconds);
                return;
            }

            if (_holders.Count < step.handsRequired)
            {
                // You walked away. Progress holds, so half-finished work can be picked back up
                // by whoever gets here next - this is what lets one intern run four beds instead
                // of standing at one. A patient who is falling apart undoes it slowly anyway,
                // so leaving a bleeder is still a decision with a cost.
                bool deteriorating = _patient.State is PatientState.Bleeding or PatientState.Critical;
                if (deteriorating)
                    _progress.Value = Mathf.MoveTowards(_progress.Value, 0f, Time.fixedDeltaTime / (step.holdSeconds * 4f));
                return;
            }

            _progress.Value += Time.fixedDeltaTime / Mathf.Max(0.05f, step.holdSeconds);
            if (_progress.Value >= 1f) CompleteStep(step);
        }

        private void CompleteStep(ProcedureStep step)
        {
            _progress.Value = 0f;

            foreach (var used in _usedThisStep) used.Soil();
            _usedThisStep.Clear();

            if (step.opensBleed) _patient.StartBleeding(step.bleedRatePerSecond);
            if (step.closesBleed) _patient.StopBleeding();
            if (step.sedates) _patient.SetConscious(false);

            foreach (ulong holder in _holders)
                IncidentLog.Record(holder, $"completed '{step.displayName}'");

            _stepIndex.Value++;

            if (_stepIndex.Value < procedure.steps.Count)
            {
                ShiftDirector.Instance?.Announce($"{step.displayName} - done");
                return;
            }

            _finished.Value = true;
            _patient.Stabilise();
            ShiftDirector.Instance?.Announce($"Patient stabilised. {procedure.displayName} complete.");

            foreach (ulong holder in _holders)
                IncidentLog.Record(holder, $"completed the {procedure.displayName} - patient survived");
        }

        /// <summary>Start the procedure over for a newly admitted patient.</summary>
        public void Restart()
        {
            if (!IsServer) return;
            _stepIndex.Value = 0;
            _progress.Value = 0f;
            _finished.Value = false;
        }

        private Transform SiteFor(string siteId)
        {
            if (_sites == null) return null;
            foreach (var site in _sites)
                if (site != null && site.siteId == siteId) return site.transform;
            return null;
        }
    }
}
