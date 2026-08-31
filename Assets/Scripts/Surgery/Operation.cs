using System.Collections.Generic;
using Probation.Game;
using Probation.Interaction;
using Probation.Player;
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
                    if (Qualifies(grabbable.HeldBy, step)) _holders.Add(grabbable.HeldBy);
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

            // Wrong tool undoes progress rather than blocking input. The step never refuses you;
            // it just gets further away while you do the wrong thing.
            if (wrongToolPresent || _holders.Count < step.handsRequired)
            {
                _progress.Value = Mathf.MoveTowards(_progress.Value, 0f, Time.fixedDeltaTime / step.holdSeconds);
                return;
            }

            _progress.Value += Time.fixedDeltaTime / Mathf.Max(0.05f, step.holdSeconds);
            if (_progress.Value >= 1f) CompleteStep(step);
        }

        /// <summary>A step can be gated on a specialism - Exostructure alone opens a carapace.</summary>
        private bool Qualifies(ulong clientId, ProcedureStep step)
        {
            if (step.requiredSpecialism == Specialism.None) return true;

            var player = NetworkManager.SpawnManager.GetPlayerNetworkObject(clientId);
            var role = player != null ? player.GetComponent<PlayerRole>() : null;
            return role != null && role.Specialism == step.requiredSpecialism;
        }

        private void CompleteStep(ProcedureStep step)
        {
            _progress.Value = 0f;

            if (step.opensBleed) _patient.StartBleeding(step.bleedRatePerSecond);
            if (step.closesBleed) _patient.StopBleeding();

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

        private Transform SiteFor(string siteId)
        {
            if (_sites == null) return null;
            foreach (var site in _sites)
                if (site != null && site.siteId == siteId) return site.transform;
            return null;
        }
    }
}
