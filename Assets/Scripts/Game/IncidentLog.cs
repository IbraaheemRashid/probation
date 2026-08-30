using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Probation.Game
{
    /// <summary>One thing that happened, and who it happened because of.</summary>
    public readonly struct Incident
    {
        public readonly ulong ClientId;
        public readonly string Actor;
        public readonly string What;
        public readonly float At;

        public Incident(ulong clientId, string actor, string what, float at)
        {
            ClientId = clientId;
            Actor = actor;
            What = what;
            At = at;
        }

        public override string ToString() => $"[{At:0.0}s] {Actor}: {What}";
    }

    /// <summary>
    /// Host-side record of who did what, read out by the end-of-shift review.
    ///
    /// This exists in phase 2 rather than phase 6 on purpose. Attribution is cross-cutting:
    /// every system that can produce a review line has to record one as it goes, and
    /// retrofitting that through five systems later is how a schedule buffer disappears.
    ///
    /// Host only. A client-reported incident would make the payoff scene corruptible.
    /// </summary>
    public static class IncidentLog
    {
        private static readonly List<Incident> Recorded = new();

        public static IReadOnlyList<Incident> Entries => Recorded;

        public static void Record(ulong clientId, string what)
        {
            var net = NetworkManager.Singleton;
            if (net == null || !net.IsServer) return;

            Recorded.Add(new Incident(clientId, NameOf(clientId), what, Time.time));
            Debug.Log($"[Incident] {NameOf(clientId)}: {what}");
        }

        public static void Clear() => Recorded.Clear();

        /// <summary>
        /// Placeholder until interns have names. Steam display names arrive with the lobby
        /// work; the review screen should read "Yousef left a clamp inside", not "client 2".
        /// </summary>
        private static string NameOf(ulong clientId) => $"Intern {clientId}";
    }
}
