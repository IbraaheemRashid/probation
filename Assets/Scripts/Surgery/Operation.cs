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
        [Tooltip("Seconds before a wrong tool at the site can hurt the patient again.")]
        [SerializeField] private float wrongToolCooldown = 1.25f;

        [Header("What leaves them fragile")]
        [Tooltip("Added each time a step is completed on somebody who is still awake.")]
        [SerializeField] private float fragilityPerAwakeStep = 0.12f;
        [Tooltip("Fraction of whatever harm is left at the end that becomes fragility.")]
        [SerializeField] private float fragilityFromResidualHarm = 0.5f;

        private readonly NetworkVariable<int> _stepIndex = new();
        private readonly NetworkVariable<float> _progress = new();
        private readonly NetworkVariable<bool> _finished = new();

        // Which procedure this bed is running, as a casebook index. There is deliberately no
        // serialized default: a procedure authored into the scene would silently outrank the
        // one somebody charted, and the resulting patient would be treated for whatever the
        // level designer last thought.
        private readonly NetworkVariable<int> _procedureIndex = new(-1);

        /// <summary>
        /// What the ward has decided to do. Replicated - the operation HUD reads the step list
        /// on every client, not just the host.
        /// </summary>
        public Procedure Procedure => Casebook.Active?.ProcedureAt(_procedureIndex.Value);

        /// <summary>Whether anybody has committed this patient to a procedure yet.</summary>
        public bool HasProcedure => Procedure != null;

        public int StepIndex => _stepIndex.Value;
        public float Progress => _progress.Value;
        public bool Finished => _finished.Value;

        /// <summary>Whether this patient is currently somewhere you are allowed to work.</summary>
        public bool InBay { get; private set; }

        /// <summary>
        /// Whether the ward is treating what is actually wrong with this patient.
        ///
        /// Host-side and private, and it has to stay that way. Condition, species and procedure
        /// all replicate, so a client could work this out for itself - but the moment it reaches
        /// a HUD, diagnosis is finished and every patient becomes a label to read. It never
        /// leaves this class, and nothing it drives is ever announced.
        ///
        /// A patient with no condition authored at all is treated as correct rather than as
        /// wrong, so a hand-placed patient in a test scene still behaves.
        /// </summary>
        private bool IsCorrect
        {
            get
            {
                var condition = _patient.Condition;
                return condition == null || Procedure == condition.TreatmentFor(_patient.Species);
            }
        }

        /// <summary>
        /// Whoever wrote the chart, falling back to a pair of hands when nobody did.
        ///
        /// Wrong-procedure harm belongs to the person who made the call. Wrong-tool, awake and
        /// impact harm stay with the hands, as they always have.
        /// </summary>
        private ulong CharterOrHolder()
        {
            var chart = _patient.Chart;
            if (chart != null && chart.ChartedBy != ulong.MaxValue) return chart.ChartedBy;

            foreach (ulong holder in _holders) return holder;
            return ulong.MaxValue;
        }

        /// <summary>Relief for a condition with no authored answer. The old Stabilise constant.</summary>
        private const float DefaultRelief = 0.35f;

        public ProcedureStep CurrentStep
        {
            get
            {
                var running = Procedure;
                return running != null && _stepIndex.Value < running.steps.Count
                    ? running.steps[_stepIndex.Value]
                    : null;
            }
        }

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

            // Nothing happens until somebody has written the chart. A patient nobody has
            // committed to a procedure is not one the ward may start cutting.
            var running = Procedure;
            if (running == null || running.steps.Count == 0) return;

            // You cannot operate in a corridor. This is what makes wheeling somebody to a bay a
            // job rather than a formality, and it is the reason the map has rooms in it.
            InBay = Probation.Game.OperatingBay.Holding(_patient) != null;
            if (!InBay) return;

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
                {
                    _patient.ApplyHarm(0.1f, holder, "operated on a patient who was still awake");

                    // Cutting somebody who can feel it does lasting damage as well as immediate
                    // damage. They come off the table looking no worse and holding together
                    // rather less well.
                    _patient.AddFragility(fragilityPerAwakeStep, holder, null);
                }

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

            // Treating the wrong thing hurts them a little more with every step, and the room is
            // never told. An announcement here would be a way to brute-force the diagnosis:
            // start a procedure, wait for the shout, undo. The only feedback is the rate
            // climbing and the flesh going yellow - the channels somebody was supposed to be
            // watching. If nobody wheeled the monitor over, nobody finds out.
            if (!IsCorrect)
            {
                var misread = _patient.Condition?.AnswerFor(_patient.Species);
                if (misread != null && misread.harmPerWrongStep > 0f)
                    _patient.ApplyHarm(misread.harmPerWrongStep, CharterOrHolder(), null);
            }

            if (step.opensBleed) _patient.StartBleeding(step.bleedRatePerSecond);
            if (step.closesBleed) _patient.StopBleeding();
            if (step.sedates) _patient.SetConscious(false);

            foreach (ulong holder in _holders)
                IncidentLog.Record(holder, $"completed '{step.displayName}'");

            _stepIndex.Value++;

            var running = Procedure;
            if (running == null) return;

            if (_stepIndex.Value < running.steps.Count)
            {
                ShiftDirector.Instance?.Announce($"{step.displayName} - done");
                return;
            }

            _finished.Value = true;

            // The wrong procedure still finishes. Never finishing would be a hard block wearing
            // a costume - it would strand the patient on a bed with no way out and no way to
            // find out why. It completes, the room is told it completed, and the difference
            // shows up in the patient rather than in the announcement.
            _patient.SetConscious(false);
            ShiftDirector.Instance?.Announce($"Patient stabilised. {running.displayName} complete.");

            var answer = _patient.Condition?.AnswerFor(_patient.Species);

            // Whatever harm you failed to get back off them before closing follows them out of
            // theatre as fragility. Blamed on nobody: it is the state they were in, not an act.
            _patient.AddFragility(_patient.Harm * fragilityFromResidualHarm, ulong.MaxValue, null);

            if (IsCorrect)
            {
                _patient.ResolveCondition();
                _patient.StopBleeding();
                _patient.Heal(answer != null ? answer.reliefIfCorrect : DefaultRelief);

                foreach (ulong holder in _holders)
                    IncidentLog.Record(holder, $"completed the {running.displayName} - patient survived");

                if (answer != null && !string.IsNullOrEmpty(answer.reviewLineRight))
                    IncidentLog.Record(CharterOrHolder(), answer.reviewLineRight);

                return;
            }

            // Nothing is healed and the condition is never marked resolved, so the thing that
            // was actually wrong goes on working. The suture step still closes the bleed it
            // opened - stitching is stitching, whatever you were stitching for - so the patient
            // sits up looking finished and deteriorates all the way to the door. The team
            // believe they succeeded; the supervisor tells them otherwise.
            //
            // Blamed on whoever wrote the chart, not on whoever was holding the forceps - the
            // surgeon did exactly as they were told, and blaming them would poison the one
            // screen this whole game exists to produce.
            if (answer == null) return;

            _patient.AddFragility(answer.fragilityIfWrong, CharterOrHolder(), null);

            if (answer.harmIfOperated > 0f)
                _patient.ApplyHarm(answer.harmIfOperated, CharterOrHolder(), answer.reviewLineWrong);
            else if (!string.IsNullOrEmpty(answer.reviewLineWrong))
                IncidentLog.Record(CharterOrHolder(), answer.reviewLineWrong);
        }

        /// <summary>Start the procedure over for a newly admitted patient.</summary>
        public void Restart()
        {
            if (!IsServer) return;
            _stepIndex.Value = 0;
            _progress.Value = 0f;
            _finished.Value = false;
            _procedureIndex.Value = -1;

            // A bed is reused all night, so anything not cleared here leaks into the next
            // occupant. _usedThisStep still holds the last patient's instruments, which means
            // their first completed step soils tools nobody has touched; the harm cooldown
            // carries over, so the first mistake on a fresh patient can be free.
            _usedThisStep.Clear();
            _nextHarmAllowedAt = 0f;
        }

        /// <summary>
        /// Commit this bed to a procedure.
        ///
        /// Called by the chart and by nothing else. In particular it is never called by anything
        /// that knows what is actually wrong with the patient - the moment the game assigns the
        /// correct procedure by itself, diagnosis stops being a decision anybody makes.
        ///
        /// Changing your mind throws away step progress, deliberately. A chart you can swap for
        /// free on the last step is a way to try every procedure in turn until one sticks.
        /// </summary>
        public void Assign(Procedure next)
        {
            if (!IsServer) return;

            var book = Casebook.Active;
            int index = book != null && next != null ? book.IndexOf(next) : -1;
            if (index == _procedureIndex.Value) return;

            _procedureIndex.Value = index;
            _stepIndex.Value = 0;
            _progress.Value = 0f;
            _finished.Value = false;
            _usedThisStep.Clear();
            _nextHarmAllowedAt = 0f;
        }

        private Transform SiteFor(string siteId)
        {
            if (_sites != null)
                foreach (var site in _sites)
                    if (site != null && site.siteId == siteId) return site.transform;

            // Otherwise this fails completely silently: Evaluate just returns, the patient sits
            // there untreatable, and the HUD goes on cheerfully naming a step that can never
            // complete. Warn once per missing site, so a typo in a procedure asset costs a line
            // in the console rather than a playtest.
            if (_warnedSites.Add(siteId))
                Debug.LogWarning($"[Operation] {name}: no SurgerySite '{siteId}' on this patient. " +
                                 "That step can never complete.", this);

            return null;
        }

        private readonly HashSet<string> _warnedSites = new();
    }
}
