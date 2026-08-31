using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;

namespace Probation.Game
{
    public enum ShiftPhase
    {
        Shift,
        Review,
        WeekOver,
    }

    /// <summary>
    /// The week. Seven shifts, a review after each one, and a verdict at the end.
    ///
    /// The run-ending condition is the <em>hospital's</em> body count, never an individual's.
    /// Interns get written up and eventually demoted, but never removed - benching somebody for
    /// the remaining half hour is how you lose the group, not how you raise the stakes.
    /// </summary>
    public class ShiftDirector : NetworkBehaviour
    {
        [SerializeField] private float shiftSeconds = 390f;      // ~6.5 minutes
        [SerializeField] private float reviewSeconds = 20f;
        [SerializeField] private int shiftsPerWeek = 7;
        [Tooltip("Patients lost across the whole week before the ward is closed.")]
        [SerializeField] private int hospitalDeathLimit = 8;

        private readonly NetworkVariable<int> _day = new(1);
        private readonly NetworkVariable<float> _timeLeft = new();
        private readonly NetworkVariable<ShiftPhase> _phase = new(ShiftPhase.Shift);
        private readonly NetworkVariable<int> _deaths = new();

        public int Day => _day.Value;
        public float TimeLeft => _timeLeft.Value;
        public ShiftPhase Phase => _phase.Value;
        public int Deaths => _deaths.Value;
        public int ShiftsPerWeek => shiftsPerWeek;
        public int DeathLimit => hospitalDeathLimit;

        /// <summary>Last review, as display lines. Sent from the host at review time.</summary>
        public IReadOnlyList<string> ReviewLines => _reviewLines;
        private readonly List<string> _reviewLines = new();

        /// <summary>There is one ward. Patients report deaths here without hunting for it.</summary>
        public static ShiftDirector Instance { get; private set; }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            if (!IsServer) return;
            _timeLeft.Value = shiftSeconds;
            IncidentLog.Clear();
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
        }

        // ---------------------------------------------------------------- notices

        /// <summary>Recent on-screen messages, newest last. Purely feedback - not the review.</summary>
        public readonly List<(string Text, float At)> Notices = new();

        /// <summary>
        /// Say something to the whole ward. Host only: these are consequences of host-simulated
        /// state, so letting clients raise them would let them lie about what happened.
        /// </summary>
        public void Announce(string text)
        {
            if (IsServer) AnnounceRpc(text);
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void AnnounceRpc(string text)
        {
            Notices.Add((text, Time.time));
            if (Notices.Count > 6) Notices.RemoveAt(0);
        }

        public void RecordDeath()
        {
            if (IsServer) _deaths.Value++;
        }

        private void Update()
        {
            if (!IsServer || _phase.Value == ShiftPhase.WeekOver) return;

            _timeLeft.Value -= Time.deltaTime;
            if (_timeLeft.Value > 0f) return;

            if (_phase.Value == ShiftPhase.Shift) BeginReview();
            else BeginNextShift();
        }

        private void BeginReview()
        {
            _phase.Value = ShiftPhase.Review;
            _timeLeft.Value = reviewSeconds;
            // NGO serializes string but not string[], so the review travels as one blob.
            PublishReviewRpc(string.Join("\n", BuildReview()));
        }

        private void BeginNextShift()
        {
            if (_day.Value >= shiftsPerWeek || _deaths.Value >= hospitalDeathLimit)
            {
                _phase.Value = ShiftPhase.WeekOver;
                _timeLeft.Value = 0f;
                return;
            }

            _day.Value++;
            _phase.Value = ShiftPhase.Shift;
            _timeLeft.Value = shiftSeconds;
            IncidentLog.Clear();
        }

        /// <summary>
        /// The shift read back by name. This is the payoff scene, and the reason attribution had
        /// to be threaded through every system from phase 2 rather than bolted on here.
        /// </summary>
        private string[] BuildReview()
        {
            var byIntern = new Dictionary<string, List<string>>();

            foreach (var incident in IncidentLog.Entries)
            {
                if (!byIntern.TryGetValue(incident.Actor, out var list))
                    byIntern[incident.Actor] = list = new List<string>();
                list.Add(incident.What);
            }

            if (byIntern.Count == 0)
                return new[] { "Nothing was attempted today. That is its own kind of result." };

            var lines = new List<string>();
            var sb = new StringBuilder();

            foreach (var pair in byIntern)
            {
                sb.Clear().Append(pair.Key).Append(':');
                lines.Add(sb.ToString());

                var counts = new Dictionary<string, int>();
                foreach (string what in pair.Value)
                    counts[what] = counts.TryGetValue(what, out int n) ? n + 1 : 1;

                foreach (var entry in counts)
                    lines.Add(entry.Value > 1
                        ? $"    {entry.Key} (x{entry.Value})"
                        : $"    {entry.Key}");
            }

            return lines.ToArray();
        }

        [Rpc(SendTo.ClientsAndHost)]
        private void PublishReviewRpc(string joined)
        {
            _reviewLines.Clear();
            if (!string.IsNullOrEmpty(joined))
                _reviewLines.AddRange(joined.Split('\n'));
        }
    }
}
