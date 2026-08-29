using System;
using Steamworks;
using UnityEngine;

namespace Probation.Player
{
    /// <summary>
    /// Owns the Steam API lifetime. One of these, alive for the whole session.
    ///
    /// Failure to initialise is not fatal on purpose: Steam not running, or not logged in, is
    /// the normal case while iterating solo. The direct-IP path in NetworkBootstrap keeps
    /// working and only the Steam panel disables itself.
    /// </summary>
    public class SteamManager : MonoBehaviour
    {
        [Tooltip("480 is Valve's Spacewar test app. Replace with the real app ID once you have one.")]
        [SerializeField] private uint appId = 480;

        public static SteamManager Instance { get; private set; }

        /// <summary>True when the Steam API came up and is usable.</summary>
        public static bool Ready => Instance != null && Instance._initialised && SteamClient.IsValid;

        public static SteamId LocalId => SteamClient.SteamId;
        public static string LocalName => SteamClient.Name;

        /// <summary>Why Steam is unavailable, for the diagnostics overlay. Empty when fine.</summary>
        public static string Problem { get; private set; } = "not initialised";

        private bool _initialised;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            try
            {
                // asyncCallbacks:false so callbacks are pumped from Update, on Unity's main
                // thread, in a known order. Steam events then arrive where Unity expects them.
                SteamClient.Init(appId, false);
                _initialised = true;
                Problem = string.Empty;
                Debug.Log($"[Probation] Steam ready - {SteamClient.Name} ({SteamClient.SteamId}) app {appId}");
            }
            catch (Exception e)
            {
                _initialised = false;
                Problem = e.Message;
                Debug.LogWarning($"[Probation] Steam unavailable: {e.Message}. " +
                                 "Direct-IP hosting still works. Is Steam running and logged in?");
            }
        }

        private void Update()
        {
            if (_initialised) SteamClient.RunCallbacks();
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            if (!_initialised) return;

            SteamClient.Shutdown();
            _initialised = false;
        }
    }
}
