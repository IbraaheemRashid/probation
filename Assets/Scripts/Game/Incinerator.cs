using Probation.Surgery;
using Unity.Netcode;
using UnityEngine;

namespace Probation.Game
{
    /// <summary>
    /// Where the living things go.
    ///
    /// The airlock takes bodies: one gesture, instant, no ceremony. This is deliberately not
    /// that. A parasite goes in, the door shuts, and it <b>burns for six seconds</b> - and for
    /// those six seconds you are standing in a small room with one door beside something you
    /// sedated a little while ago and cannot see any more.
    ///
    /// That wait is the entire reason this is a room rather than a second hole in the hull. Take
    /// too long getting here and the cycle is where you find out: a parasite that wakes up mid-burn
    /// stops the machine and is loose, in here, with you, between you and the way out.
    ///
    /// Host-only, like the steriliser it is modelled on. Nothing about disposal may be
    /// client-reported - the end-of-night sweep counts what is still on the ship, and that
    /// number decides whether the week ends.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class Incinerator : MonoBehaviour
    {
        [Tooltip("Seconds a specimen burns for. Long enough that a late arrival is a real gamble against the sedation running out.")]
        [SerializeField] private float cycleSeconds = 6f;

        private readonly System.Collections.Generic.Dictionary<Parasite, float> _burning = new();
        private readonly System.Collections.Generic.List<Parasite> _done = new();

        /// <summary>True while something is in the chamber. Drives the glow, and the noise.</summary>
        public bool Running => _burning.Count > 0;

        private void Reset()
        {
            var collider = GetComponent<Collider>();
            if (collider != null) collider.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsHost()) return;

            var parasite = other.GetComponentInParent<Parasite>();
            if (parasite == null) return;

            // Only something already under. You cannot wrestle an awake one into a furnace, and
            // a loose one has no collider to be detected by anyway - which is the same rule
            // stated twice, and worth stating twice.
            if (!parasite.IsSedated) return;
            if (_burning.ContainsKey(parasite)) return;

            _burning[parasite] = Time.time + cycleSeconds;
            ShiftDirector.Instance?.Announce("The incinerator is running.");
        }

        private void OnTriggerExit(Collider other)
        {
            var parasite = other.GetComponentInParent<Parasite>();
            if (parasite != null) _burning.Remove(parasite);
        }

        private void Update()
        {
            if (!IsHost() || _burning.Count == 0) return;

            _done.Clear();

            foreach (var pair in _burning)
            {
                if (pair.Key == null) { _done.Add(pair.Key); continue; }

                // It woke up in the chamber. The cycle stops, and it is now loose in a room with
                // one door - which is the whole reason this is a room.
                if (!pair.Key.IsSedated)
                {
                    _done.Add(pair.Key);
                    ShiftDirector.Instance?.Announce("It came round. The burn has stopped.");
                    continue;
                }

                if (Time.time >= pair.Value) _done.Add(pair.Key);
            }

            foreach (var parasite in _done)
            {
                bool burned = parasite != null && parasite.IsSedated;
                _burning.Remove(parasite);

                if (!burned) continue;

                IncidentLog.Record(parasite.Blame, "burned one of them");
                parasite.Incinerate();
            }
        }

        private static bool IsHost() =>
            NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
    }
}
