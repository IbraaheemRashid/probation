using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Probation.EditorTools
{
    /// <summary>
    /// Playtest builds, and the one file Steam needs beside them.
    ///
    /// The Steam overlay cannot be injected into the Unity Editor - it has to be in place before
    /// the renderer initialises, and scripts only run after that. So "Invite friends", which is
    /// an overlay call, does nothing in Play mode and can only ever be tested from a build. That
    /// is the whole reason this file exists.
    ///
    /// A build also has to be told which app it is. Steam answers that from steam_appid.txt in
    /// the working directory, which for a player is the folder holding the .exe - not the project
    /// root, where the editor found it. Copying it is easy to forget once and then confusing for
    /// an hour, so <see cref="CopySteamAppId"/> does it on every build automatically, including
    /// builds started from File > Build Settings rather than the menu items here.
    /// </summary>
    public static class ProbationBuild
    {
        internal const string AppIdFile = "steam_appid.txt";
        private const string OutputDir = "Builds/Playtest";
        private const string ExeName = "probation.exe";

        /// <summary>
        /// The scene that has the title card on it. Map is the game; Greybox is a test scene and
        /// happens to sit at index 0 in Build Settings, so a playtest build is pinned to Map here
        /// rather than left to whatever the build list is ordered as today.
        /// </summary>
        private const string EntryScene = "Assets/Scenes/Map.unity";

        // ------------------------------------------------------------------ menu

        [MenuItem("Probation/Build/Playtest Build (Win64)", priority = 100)]
        public static void BuildPlaytest() => Build(development: false);

        [MenuItem("Probation/Build/Playtest Build (Win64, Development)", priority = 101)]
        public static void BuildPlaytestDevelopment() => Build(development: true);

        [MenuItem("Probation/Build/Reveal Build Folder", priority = 120)]
        public static void RevealBuildFolder()
        {
            string dir = Path.GetFullPath(OutputDir);
            if (!Directory.Exists(dir))
            {
                Debug.LogWarning($"[Probation] No build yet at {dir}. Run a playtest build first.");
                return;
            }

            EditorUtility.RevealInFinder(Path.Combine(dir, ExeName));
        }

        // ------------------------------------------------------------------ build

        private static void Build(bool development)
        {
            if (!File.Exists(AppIdFile))
            {
                Debug.LogError($"[Probation] No {AppIdFile} in the project root. Steam cannot identify " +
                               "the app without it and the build will not see the overlay.");
                return;
            }

            // BuildPlayer answers a failed compile with result Unknown and totalErrors 0, which
            // reads as "something went wrong, no idea what" and sends you looking in the wrong
            // place. The console already has the real errors; say so and stop.
            if (EditorUtility.scriptCompilationFailed)
            {
                Debug.LogError("[Probation] Scripts do not compile - fix the errors above first. " +
                               "No build was attempted.");
                return;
            }

            string[] scenes = Scenes();
            if (scenes.Length == 0)
            {
                Debug.LogError($"[Probation] {EntryScene} is missing and no scenes are enabled in Build Settings.");
                return;
            }

            Directory.CreateDirectory(OutputDir);

            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = Path.Combine(OutputDir, ExeName),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = development
                    ? BuildOptions.Development | BuildOptions.AllowDebugging
                    : BuildOptions.None,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;

            if (summary.result != BuildResult.Succeeded)
            {
                Debug.LogError($"[Probation] Build {summary.result}. {summary.totalErrors} error(s). " +
                               "A result of Unknown with no errors almost always means a script " +
                               "failed to compile - check the console above.");
                return;
            }

            Debug.Log($"[Probation] Built to {Path.GetFullPath(options.locationPathName)} " +
                      $"in {summary.totalTime.TotalSeconds:F0}s. Launch it on both machines with " +
                      "Steam running, host on one, then Invite friends.");

            EditorUtility.RevealInFinder(options.locationPathName);
        }

        /// <summary>
        /// Entry scene first, then whatever else is enabled in Build Settings. Additive scene
        /// loads are not used yet, but shipping the rest costs almost nothing and means a build
        /// does not silently lose a scene somebody adds later.
        /// </summary>
        private static string[] Scenes()
        {
            var rest = EditorBuildSettings.scenes
                .Where(s => s.enabled && s.path != EntryScene)
                .Select(s => s.path);

            return File.Exists(EntryScene)
                ? new[] { EntryScene }.Concat(rest).ToArray()
                : rest.ToArray();
        }

    }

    /// <summary>
    /// Copies steam_appid.txt beside the built executable. Runs for every standalone build,
    /// however it was started - including File > Build Settings, which is the case worth
    /// covering, because that is the path you take when you are not thinking about Steam.
    /// </summary>
    public class CopySteamAppId : IPostprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPostprocessBuild(BuildReport report)
        {
            string exe = report.summary.outputPath;
            if (string.IsNullOrEmpty(exe)) return;

            string buildDir = Path.GetDirectoryName(exe);
            if (string.IsNullOrEmpty(buildDir) || !Directory.Exists(buildDir)) return;

            string source = ProbationBuild.AppIdFile;
            if (!File.Exists(source))
            {
                Debug.LogWarning($"[Probation] No {source} in the project root, so none was copied " +
                                 "to the build. Steam will not identify the app.");
                return;
            }

            string destination = Path.Combine(buildDir, source);

            // Building into the project root would make this a copy onto itself.
            if (Path.GetFullPath(source) == Path.GetFullPath(destination)) return;

            File.Copy(source, destination, overwrite: true);

            Debug.Log($"[Probation] Copied {source} ({File.ReadAllText(source).Trim()}) to {destination}.");
        }
    }
}
