using Probation.Interaction;
using Unity.Netcode;
using UnityEngine;

namespace Probation.Game
{
    /// <summary>
    /// The washing up.
    ///
    /// Overcooked's kitchen is not fun because of the cooking, it is fun because of the dirty
    /// plates: a chore that never stops, forces people across each other's paths, and means the
    /// thing you need is always somewhere else. This is that, for a hospital.
    ///
    /// It is also why "somebody grabbed the wrong scalpel" becomes something that happens on its
    /// own rather than something the game has to punish.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class Steriliser : MonoBehaviour
    {
        [Tooltip("Seconds an instrument has to sit in here before it is usable again.")]
        [SerializeField] private float cycleSeconds = 4f;

        private readonly System.Collections.Generic.Dictionary<Grabbable, float> _inside = new();
        private readonly System.Collections.Generic.List<Grabbable> _finished = new();

        private void Reset()
        {
            var collider = GetComponent<Collider>();
            if (collider != null) collider.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!IsHost()) return;

            var grabbable = other.GetComponentInParent<Grabbable>();
            if (grabbable == null || !grabbable.IsDirty) return;

            _inside[grabbable] = Time.time + cycleSeconds;
        }

        private void OnTriggerExit(Collider other)
        {
            var grabbable = other.GetComponentInParent<Grabbable>();
            if (grabbable != null) _inside.Remove(grabbable);
        }

        private void Update()
        {
            if (!IsHost() || _inside.Count == 0) return;

            _finished.Clear();
            foreach (var pair in _inside)
                if (pair.Key == null || Time.time >= pair.Value) _finished.Add(pair.Key);

            foreach (var grabbable in _finished)
            {
                _inside.Remove(grabbable);
                if (grabbable == null) continue;

                grabbable.Clean();
                ShiftDirector.Instance?.Announce($"{grabbable.DisplayName} sterilised.");
            }
        }

        private static bool IsHost() =>
            NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
    }
}
