using System;
using System.Collections.Generic;
using System.Text;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Probation.Game
{
    /// <summary>
    /// The beats of a shift. Matches the loop in the design doc: clock in, take patients in,
    /// operate, deal with what goes wrong, cover it up, get read back to.
    /// </summary>
    public enum ShiftPhase
    {
        /// <summary>The working night. Patients arrive, things go wrong.</summary>
        Shift,

        /// <summary>
        /// The window between the last patient and the supervisor. Repair what you broke, move
        /// what should not be found, decide whose name is on the chart.
        /// </summary>
        CoverUp,

        /// <summary>The shift read back to you, by name.</summary>
        Review,

        WeekOver,
    }

    /// <summary>
    /// Runs the week. Seven nights, each with its own beats, and a verdict at the end.
    ///
    /// The run-ending condition is the <em>hospital's</em> body count, never an individual's.
    /// Interns are written up, never removed - benching somebody for the remaining half hour is
    /// how you lose the group, not how you raise the stakes.
    /// </summary>
    public class ShiftDirector : NetworkBehaviour
    {
        [Header("Phase lengths (seconds)")]
        [Tooltip("Short while the loop is being tuned - seven of these is about half an hour.")]
        [SerializeField] private float shiftSeconds = 210f;
        [SerializeField] private float coverUpSeconds = 20f;
        [SerializeField] private float reviewSeconds = 20f;

        [Header("Quota")]
        [Tooltip("Patients discharged alive required on night one.")]
        [SerializeField] private int baseQuota = 3;
        [Tooltip("Added to the quota each night. This is the pressure curve.")]
        [SerializeField] private int quotaGrowth = 1;
        [Tooltip("Nights you can miss the quota before the ward is closed.")]
        [SerializeField] private int maxStrikes = 3;

        [Header("Week")]
        [SerializeField] private int shiftsPerWeek = 7;
        [Tooltip("Patients lost across the whole week before the ward is closed.")]
        [SerializeField] private int hospitalDeathLimit = 8;

        [Header("Ward clock")]
        [Tooltip("Fictional hour the shift starts. It is a night shift.")]
        [SerializeField] private int startHour = 20;
        [SerializeField] private int shiftHours = 8;

        private readonly NetworkVariable<int> _day = new(1);
        private readonly NetworkVariable<float> _timeLeft = new();
        private readonly NetworkVariable<ShiftPhase> _phase = new(ShiftPhase.Shift);
        private readonly NetworkVariable<int> _deaths = new();
        private readonly NetworkVariable<int> _discharged = new();
        private readonly NetworkVariable<int> _strikes = new();
        private readonly NetworkVariable<bool> _survivedWeek = new();

        public static ShiftDirector Instance { get; private set; }

        public int Day => _day.Value;
        public int ShiftsPerWeek => shiftsPerWeek;
        public float TimeLeft => _timeLeft.Value;
        public ShiftPhase Phase => _phase.Value;
        public int Deaths => _deaths.Value;
        public int DeathLimit => hospitalDeathLimit;

        /// <summary>Discharged alive tonight. The only number that counts towards the quota.</summary>
        public int Discharged => _discharged.Value;

        /// <summary>Tonight's target. Rises every night - the clock only matters because of this.</summary>
        public int Quota => baseQuota + (_day.Value - 1) * quotaGrowth;

        public int Strikes => _strikes.Value;
        public int MaxStrikes => maxStrikes;
        public bool QuotaMet => _discharged.Value >= Quota;
        public bool SurvivedWeek => _survivedWeek.Value;

        /// <summary>0 at the start of the current phase, 1 at its end.</summary>
        public float PhaseProgress =>
            DurationOf(_phase.Value) <= 0f ? 1f
                : Mathf.Clamp01(1f - _timeLeft.Value / DurationOf(_phase.Value));

        /// <summary>Fictional ward time, e.g. "23:40". Sweeps once across the shift.</summary>
        public string WardTime
        {
            get
            {
                float progress = _phase.Value == ShiftPhase.Shift ? PhaseProgress : 1f;

                float hours = startHour + progress * shiftHours;
                int h = Mathf.FloorToInt(hours) % 24;
                int m = Mathf.FloorToInt(hours % 1f * 60f);
                return $"{h:00}:{m:00}";
            }
        }

        /// <summary>Fires on every client when the phase changes. Drives the title cards.</summary>
        public event Action<ShiftPhase> PhaseChanged;

        /// <summary>Recent on-screen messages, newest last.</summary>
        public readonly List<(string Text, float At)> Notices = new();

        public override void OnNetworkSpawn()
        {
            Instance = this;
            _phase.OnValueChanged += (_, next) => PhaseChanged?.Invoke(next);

            if (!IsServer) return;
            _timeLeft.Value = shiftSeconds;
            IncidentLog.Clear();
        }

        public override void OnNetworkDespawn()
        {
            if (Instance == this) Instance = null;
        }

        // ---------------------------------------------------------------- notices

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

        public void RecordDischarge()
        {
            if (!IsServer) return;

            _discharged.Value++;
            IncidentLog.Record(ulong.MaxValue, "a patient walked out alive");
        }

        // ---------------------------------------------------------------- phases

        private float DurationOf(ShiftPhase phase) => phase switch
        {
            ShiftPhase.Shift => shiftSeconds,
            ShiftPhase.CoverUp => coverUpSeconds,
            ShiftPhase.Review => reviewSeconds,
            _ => 0f,
        };

        private void Update()
        {
            if (!IsServer) return;

            if (_phase.Value == ShiftPhase.WeekOver)
            {
                // Restarting without leaving play mode. Testing a loop you have to re-host to
                // replay is testing it about a third as often.
                var keyboard = Keyboard.current;
                if (keyboard != null && keyboard.rKey.wasPressedThisFrame) RestartWeek();
                return;
            }

            _timeLeft.Value -= Time.deltaTime;
            if (_timeLeft.Value > 0f) return;

            Advance();
        }

        private void Advance()
        {
            switch (_phase.Value)
            {
                case ShiftPhase.Shift:
                    Enter(ShiftPhase.CoverUp);

                    if (QuotaMet)
                    {
                        Announce($"Quota met. {_discharged.Value}/{Quota} discharged.");
                    }
                    else
                    {
                        _strikes.Value++;
                        Announce($"Quota missed: {_discharged.Value}/{Quota}. Strike {_strikes.Value}/{maxStrikes}.");
                    }
                    break;

                case ShiftPhase.CoverUp:
                    Enter(ShiftPhase.Review);
                    PublishReviewRpc(string.Join("\n", BuildReview()));
                    break;

                case ShiftPhase.Review:
                    bool sacked = _strikes.Value >= maxStrikes || _deaths.Value >= hospitalDeathLimit;
                    if (sacked || _day.Value >= shiftsPerWeek)
                    {
                        _survivedWeek.Value = !sacked;
                        Enter(ShiftPhase.WeekOver);
                        break;
                    }

                    _day.Value++;
                    _discharged.Value = 0;
                    Enter(ShiftPhase.Shift);
                    IncidentLog.Clear();
                    Announce($"Night {_day.Value}. The doors are open. Quota {Quota}.");
                    break;
            }
        }

        private void Enter(ShiftPhase phase)
        {
            _phase.Value = phase;
            _timeLeft.Value = DurationOf(phase);
        }

        /// <summary>Wipe the week and open the doors again. Host only.</summary>
        public void RestartWeek()
        {
            if (!IsServer) return;

            _day.Value = 1;
            _deaths.Value = 0;
            _discharged.Value = 0;
            _strikes.Value = 0;
            _survivedWeek.Value = false;

            foreach (var patient in Surgery.Patient.All)
                if (patient != null) patient.SendAway();

            IncidentLog.Clear();
            Enter(ShiftPhase.Shift);
            Announce($"Night 1. The doors are open. Quota {Quota}.");
        }

        // ---------------------------------------------------------------- review

        public IReadOnlyList<string> ReviewLines => _reviewLines;
        private readonly List<string> _reviewLines = new();

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
                return new[] { "Nothing was attempted tonight. That is its own kind of result." };

            var lines = new List<string>();
            var sb = new StringBuilder();

            foreach (var pair in byIntern)
            {
                lines.Add(sb.Clear().Append(pair.Key).Append(':').ToString());

                var counts = new Dictionary<string, int>();
                foreach (string what in pair.Value)
                    counts[what] = counts.TryGetValue(what, out int n) ? n + 1 : 1;

                foreach (var entry in counts)
                    lines.Add(entry.Value > 1 ? $"    {entry.Key} (x{entry.Value})" : $"    {entry.Key}");
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
