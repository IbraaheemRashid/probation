using Probation.Game;
using Probation.Interaction;
using Probation.Surgery;
using Probation.Player;
using Unity.Netcode;
using Unity.Netcode.Components;
using Netcode.Transports.Facepunch;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Probation.EditorTools
{
    /// <summary>
    /// One-shot project setup. Everything here is something you could do by hand in the
    /// inspector; doing it in code just means the values match the README exactly and the
    /// wiring cannot be half-done.
    ///
    /// Run the three menu items in order.
    /// </summary>
    public static class ProbationSetup
    {
        private const string PlayerLayerName = "Player";
        private const string InputAssetPath = "Assets/InputSystem_Actions.inputactions";
        private const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
        private const string GreyboxScenePath = "Assets/Scenes/Greybox.unity";

        // ------------------------------------------------------------------ 1

        [MenuItem("Probation/Setup/1 - Configure Project", priority = 0)]
        public static void ConfigureProject()
        {
            int layer = EnsureLayer(PlayerLayerName);
            if (layer < 0)
            {
                Debug.LogError("No free user layer slot for 'Player'. Free one in Project Settings > Tags and Layers.");
                return;
            }

            // 60 Hz physics. The ride spring is noticeably better behaved than at the 50 Hz
            // default, and at four players it costs nothing.
            //
            // Time.fixedDeltaTime only changes the running value - it does not mark TimeManager
            // dirty, so the setting is lost on restart. It has to go through the asset.
            if (!SetProjectSetting("ProjectSettings/TimeManager.asset", "Fixed Timestep", 1f / 60f))
                Debug.LogWarning("[Probation] Could not write Fixed Timestep. Set it to 0.0166 in Project Settings > Time.");

            // Real gravity is 9.81 and it feels like the moon in first person. Everything -
            // players, dropped instruments, thrown tools, patients landing on beds - reads as
            // floaty until this is roughly two and a half times life.
            if (!SetGravity(GameGravity))
                Debug.LogWarning("[Probation] Could not write gravity. Set Y to -24 in Project Settings > Physics.");

            // Interns should not slide down ramps they are standing still on.
            Physics.defaultSolverIterations = 8;

            // Without this an unfocused build stops simulating entirely - so when you launch a
            // second copy, the first one freezes. It reads as a netcode bug and is not one.
            PlayerSettings.runInBackground = true;

            AssetDatabase.SaveAssets();
            Debug.Log($"[Probation] Layer '{PlayerLayerName}' = {layer}. Fixed timestep = {Time.fixedDeltaTime:0.0000}. Solver iterations = {Physics.defaultSolverIterations}.");
        }

        // ------------------------------------------------------------------ 2

        [MenuItem("Probation/Setup/2 - Create Player Prefab", priority = 1)]
        public static void CreatePlayerPrefab()
        {
            int playerLayer = EnsureLayer(PlayerLayerName);
            if (playerLayer < 0) { Debug.LogError("Run step 1 first."); return; }

            // Everything except the player itself. This is the setting people get wrong: if the
            // player's own capsule is in the ground mask, the probe hits it and the spring
            // fights itself.
            int worldMask = ~(1 << playerLayer);

            var root = new GameObject("Player") { layer = playerLayer };

            var rb = root.AddComponent<Rigidbody>();
            rb.mass = 70f;
            rb.freezeRotation = true;
            rb.linearDamping = 0f;
            rb.angularDamping = 0.05f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            var capsule = root.AddComponent<CapsuleCollider>();
            capsule.height = 1.4f;
            capsule.radius = 0.30f;
            capsule.center = Vector3.zero;

            var pivot = new GameObject("CameraPivot") { layer = playerLayer };
            pivot.transform.SetParent(root.transform, false);
            pivot.transform.localPosition = new Vector3(0f, 0.65f, 0f);

            var cameraGo = new GameObject("Camera") { layer = playerLayer };
            cameraGo.transform.SetParent(pivot.transform, false);
            var cam = cameraGo.AddComponent<Camera>();
            cam.fieldOfView = 70f;
            cam.nearClipPlane = 0.05f;
            cameraGo.AddComponent<AudioListener>();

            var hand = new GameObject("HandAnchor") { layer = playerLayer };
            hand.transform.SetParent(pivot.transform, false);
            hand.transform.localPosition = new Vector3(0.25f, -0.20f, 0.45f);

            var cursorLock = root.AddComponent<CursorLock>();
            var reader = root.AddComponent<PlayerInputReader>();
            var look = pivot.AddComponent<PlayerLook>();
            var locomotion = root.AddComponent<PlayerLocomotion>();
            var interactor = root.AddComponent<PlayerInteractor>();
            var carry = root.AddComponent<PlayerCarry>();

            var inputAsset = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputAssetPath);
            if (inputAsset == null)
                Debug.LogWarning($"[Probation] Could not find {InputAssetPath} - assign it on PlayerInputReader by hand.");

            SetRefs(reader, ("actionAsset", inputAsset));
            SetRefs(look, ("input", reader));
            SetRefs(locomotion,
                ("input", reader),
                ("look", look),
                ("cameraPivot", pivot.transform));
            SetRefs(interactor,
                ("input", reader),
                ("viewSource", cameraGo.transform));
            SetRefs(carry,
                ("input", reader),
                ("interactor", interactor),
                ("locomotion", locomotion),
                ("handAnchor", hand.transform));

            SetMask(locomotion, "groundMask", worldMask);
            SetMask(interactor, "interactMask", worldMask);

            System.IO.Directory.CreateDirectory("Assets/Prefabs");
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            Object.DestroyImmediate(root);

            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(prefab);
            Debug.Log($"[Probation] Player prefab written to {PlayerPrefabPath}.");
        }

        // ------------------------------------------------------------------ 3

        [MenuItem("Probation/Setup/3 - Create Greybox Test Scene", priority = 2)]
        public static void CreateGreyboxScene()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (prefab == null) { Debug.LogError("Run step 2 first."); return; }

            // NewScene does not prompt on its own, and this would silently bin unsaved work.
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            EnsureLobbyCamera();

            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            Box("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(30f, 1f, 30f));

            // Step-over test. The capsule floats ~0.25 m off the floor, so the first two
            // should be walked over with no input and the third should stop you dead.
            Box("Step 0.15", new Vector3(-6f, 0.075f, 3f), new Vector3(2f, 0.15f, 2f));
            Box("Step 0.25", new Vector3(-6f, 0.125f, 6f), new Vector3(2f, 0.25f, 2f));
            Box("Step 0.40 (should block)", new Vector3(-6f, 0.20f, 9f), new Vector3(2f, 0.40f, 2f));

            // Slope test. 20 degrees is walkable, 60 is over maxSlopeAngle and should slide.
            Ramp("Ramp 20deg", new Vector3(6f, 0.9f, 4f), 20f);
            Ramp("Ramp 60deg (should slide)", new Vector3(10f, 2.2f, 4f), 60f);

            // Crouch test: an overhang you cannot stand up under.
            Box("Overhang 1.1m", new Vector3(0f, 1.25f, 8f), new Vector3(4f, 0.3f, 2f));
            Box("Overhang post L", new Vector3(-1.85f, 0.55f, 8f), new Vector3(0.3f, 1.1f, 2f));
            Box("Overhang post R", new Vector3(1.85f, 0.55f, 8f), new Vector3(0.3f, 1.1f, 2f));

            // Loose props: walk into these and they should move, not stop you.
            for (int i = 0; i < 5; i++)
            {
                var crate = Box($"Crate {i}", new Vector3(-2f + i * 1.1f, 0.25f, -4f), Vector3.one * 0.5f);
                var body = crate.AddComponent<Rigidbody>();
                body.mass = 2f + i * 4f;
            }

            // A stand-on-able trolley. Standing on it should push it DOWN - that is the ride
            // spring's counter-force, and it is what will make gurneys feel real.
            var trolley = Box("Trolley", new Vector3(4f, 0.6f, -4f), new Vector3(1.6f, 0.1f, 0.7f));
            var trolleyBody = trolley.AddComponent<Rigidbody>();
            trolleyBody.mass = 40f;

            var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            player.transform.position = new Vector3(0f, 1.0f, 0f);

            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, GreyboxScenePath);
            Debug.Log($"[Probation] Greybox scene written to {GreyboxScenePath}. Press Play.");
        }

        // ------------------------------------------------------------------ 4

        [MenuItem("Probation/Setup/4 - Network the Player", priority = 3)]
        public static void NetworkThePlayer()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (prefab == null) { Debug.LogError("Run step 2 first."); return; }

            // --- prefab side -------------------------------------------------
            var contents = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                if (contents.GetComponent<NetworkObject>() == null)
                    contents.AddComponent<NetworkObject>();

                var setup = contents.GetComponent<PlayerNetworkSetup>();
                if (setup == null) setup = contents.AddComponent<PlayerNetworkSetup>();

                // Prefabs built before CursorLock existed are missing it, and without it the
                // cursor unlocks the moment a remote player spawns.
                var carry = contents.GetComponent<PlayerCarry>();
                if (carry == null)
                {
                    carry = contents.AddComponent<PlayerCarry>();
                    var handAnchor = contents.transform.Find("CameraPivot/HandAnchor");
                    SetRefs(carry,
                        ("input", contents.GetComponent<PlayerInputReader>()),
                        ("interactor", contents.GetComponent<PlayerInteractor>()),
                        ("locomotion", contents.GetComponent<PlayerLocomotion>()),
                        ("handAnchor", handAnchor));
                    Debug.Log("[Probation] Added missing PlayerCarry to the Player prefab.");
                }

                var cursorLock = contents.GetComponent<CursorLock>();
                if (cursorLock == null)
                {
                    cursorLock = contents.AddComponent<CursorLock>();
                    Debug.Log("[Probation] Added missing CursorLock to the Player prefab.");
                }

                var pivot = contents.transform.Find("CameraPivot");
                var camera = pivot != null ? pivot.Find("Camera") : null;

                SetRefs(setup,
                    ("input", contents.GetComponent<PlayerInputReader>()),
                    ("look", pivot != null ? pivot.GetComponent<PlayerLook>() : null),
                    ("locomotion", contents.GetComponent<PlayerLocomotion>()),
                    ("interactor", contents.GetComponent<PlayerInteractor>()),
                    ("cursorLock", cursorLock),
                    ("carry", carry),
                    ("playerCamera", camera != null ? camera.GetComponent<Camera>() : null),
                    ("audioListener", camera != null ? camera.GetComponent<AudioListener>() : null),
                    ("body", contents.GetComponent<Rigidbody>()));

                // Root: position moves, rotation is frozen but synced anyway so Knockdown
                // replicates later without another pass over this prefab.
                var rootTransform = contents.GetComponent<NetworkTransform>();
                if (rootTransform == null) rootTransform = contents.AddComponent<NetworkTransform>();
                ConfigureTransform(rootTransform, localSpace: false, syncScale: false);

                // Pivot: where the intern is looking, plus the crouch height blend.
                if (pivot != null)
                {
                    var pivotTransform = pivot.GetComponent<NetworkTransform>();
                    if (pivotTransform == null) pivotTransform = pivot.gameObject.AddComponent<NetworkTransform>();
                    ConfigureTransform(pivotTransform, localSpace: true, syncScale: false);
                }

                PrefabUtility.SaveAsPrefabAsset(contents, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            // --- scene side --------------------------------------------------
            var existing = Object.FindFirstObjectByType<NetworkManager>();
            GameObject managerGo;
            if (existing != null)
            {
                managerGo = existing.gameObject;
            }
            else
            {
                managerGo = new GameObject("NetworkManager");
                managerGo.AddComponent<NetworkManager>();
            }

            var manager = managerGo.GetComponent<NetworkManager>();

            var transport = managerGo.GetComponent<UnityTransport>();
            if (transport == null) transport = managerGo.AddComponent<UnityTransport>();
            if (managerGo.GetComponent<NetworkBootstrap>() == null) managerGo.AddComponent<NetworkBootstrap>();
            if (managerGo.GetComponent<NetworkDiagnostics>() == null) managerGo.AddComponent<NetworkDiagnostics>();

            var reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            var so = new SerializedObject(manager);

            // Adding the UnityTransport component is not the same as selecting it. The
            // NetworkManager inspector normally wires this up for you; adding from script
            // skips that, and NetworkManager then throws "No transport has been selected!".
            if (!AssignReference(so, "NetworkConfig.NetworkTransport", transport))
                Debug.LogWarning("[Probation] Could not set NetworkConfig.NetworkTransport - assign the UnityTransport component on NetworkManager by hand.");

            if (!AssignReference(so, "NetworkConfig.PlayerPrefab", reloaded))
                Debug.LogWarning("[Probation] Could not set NetworkConfig.PlayerPrefab - assign the Player prefab on NetworkManager by hand.");

            so.ApplyModifiedPropertiesWithoutUndo();

            // NetworkManager spawns the player, so a hand-placed one in the scene is a duplicate.
            foreach (var stray in Object.FindObjectsByType<PlayerLocomotion>(FindObjectsSortMode.None))
            {
                Debug.Log($"[Probation] Removing scene-placed player '{stray.name}' - NetworkManager spawns it now.");
                Object.DestroyImmediate(stray.gameObject);
            }

            EnsureLobbyCamera();
            PutGreyboxFirstInBuild();

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Debug.Log("[Probation] Player networked. Save the scene, then File > Build Profiles to make a test build.");
        }

        private static void ConfigureTransform(NetworkTransform nt, bool localSpace, bool syncScale, bool ownerAuthority = true)
        {
            nt.InLocalSpace = localSpace;
            nt.Interpolate = true;
            nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = syncScale;

            // NGO 2.x renamed this between minor versions; try each spelling and report.
            var so = new SerializedObject(nt);
            string[] candidates = { "AuthorityMode", "m_AuthorityMode", "Authority" };
            bool set = false;
            foreach (var name in candidates)
            {
                var prop = so.FindProperty(name);
                if (prop == null) continue;
                prop.enumValueIndex = ownerAuthority ? 1 : 0;   // 0 = Server, 1 = Owner
                so.ApplyModifiedPropertiesWithoutUndo();
                set = true;
                Debug.Log($"[Probation] {nt.gameObject.name}: authority = {(ownerAuthority ? "Owner" : "Server")} (field '{name}').");
                break;
            }
            if (!set)
                Debug.LogWarning($"[Probation] Could not find the authority field on {nt.gameObject.name}. " +
                                 "Set 'Authority Mode' to Owner on its NetworkTransform by hand.");
        }

        // ------------------------------------------------------------------ 5

        [MenuItem("Probation/Setup/5 - Add Steam Networking", priority = 4)]
        public static void AddSteamNetworking()
        {
            var manager = Object.FindFirstObjectByType<NetworkManager>();
            if (manager == null) { Debug.LogError("No NetworkManager in the scene. Run step 4 first."); return; }

            var go = manager.gameObject;

            if (go.GetComponent<SteamManager>() == null) go.AddComponent<SteamManager>();
            if (go.GetComponent<SteamLobbyBootstrap>() == null) go.AddComponent<SteamLobbyBootstrap>();

            // Both transports live on the object at once. Whichever panel you press decides
            // which one NetworkConfig points at, so direct-IP stays available for solo work.
            if (go.GetComponent<FacepunchTransport>() == null) go.AddComponent<FacepunchTransport>();

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[Probation] Steam networking added. Steam must be running and logged in. " +
                      "steam_appid.txt (480) must sit beside the built .exe as well as in the project root.");
        }

        // ------------------------------------------------------------------ 6

        [MenuItem("Probation/Setup/6 - Add Grabbable Props", priority = 5)]
        public static void AddGrabbableProps()
        {
            if (Object.FindFirstObjectByType<NetworkManager>() == null)
            {
                Debug.LogError("No NetworkManager in the scene. Run step 4 first.");
                return;
            }

            var existing = GameObject.Find("Props");
            if (existing != null) Object.DestroyImmediate(existing);

            var root = new GameObject("Props");

            // A prep bench the instruments start on. The beds themselves come from step 7.
            var bench = Box("Prep bench", new Vector3(0f, 0.45f, -4.5f), new Vector3(5f, 0.9f, 1.1f));
            bench.transform.SetParent(root.transform, true);

            // Tools: light, precise, one pair of hands each. Ownership follows the holder.
            Tool("Scalpel",    new Vector3(-2.2f, 1.0f, -4.5f), new Vector3(0.04f, 0.03f, 0.28f), 0.3f, 0.05f);
            Tool("Forceps",    new Vector3(-1.5f, 1.0f, -4.5f), new Vector3(0.04f, 0.03f, 0.22f), 0.4f, 0.05f);
            Tool("Forceps",    new Vector3(-0.9f, 1.0f, -4.5f), new Vector3(0.04f, 0.03f, 0.22f), 0.4f, 0.05f);

            // Two retractors, not one two-handed retractor. A step that needs two pairs of
            // hands is two owner-authoritative objects; one object with two owners is the trap.
            Tool("Retractor",  new Vector3(-0.2f, 1.0f, -4.5f), new Vector3(0.18f, 0.04f, 0.22f), 1.2f, 0.12f);
            Tool("Retractor",  new Vector3(0.4f, 1.0f, -4.5f), new Vector3(0.18f, 0.04f, 0.22f), 1.2f, 0.12f);

            Tool("Suture kit", new Vector3(1.1f, 1.0f, -4.5f), new Vector3(0.12f, 0.05f, 0.16f), 0.6f, 0.08f);
            Tool("Suture kit", new Vector3(1.7f, 1.0f, -4.5f), new Vector3(0.12f, 0.05f, 0.16f), 0.6f, 0.08f);
            Tool("Gas rig",    new Vector3(2.4f, 1.0f, -4.5f), new Vector3(0.16f, 0.10f, 0.16f), 2.0f, 0.15f);

            // One scanner. Whoever is holding it knows what is wrong and cannot operate.
            Tool("Scanner",    new Vector3(-2.9f, 1.0f, -4.5f), new Vector3(0.10f, 0.04f, 0.20f), 0.5f, 0.06f);

            Heavy("Gurney", new Vector3(4.5f, 0.6f, -3f), new Vector3(1.9f, 0.6f, 0.85f), 45f, 0.55f);

            foreach (var g in Object.FindObjectsByType<Grabbable>(FindObjectsSortMode.None))
                if (g.transform.parent == null) g.transform.SetParent(root.transform, true);

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[Probation] Props added. Scene NetworkObjects spawn automatically when you host.");
        }

        private static void Tool(string name, Vector3 position, Vector3 size, float mass, float encumbrance) =>
            Grab(name, position, size, mass, encumbrance, GrabKind.Tool);

        private static void Heavy(string name, Vector3 position, Vector3 size, float mass, float encumbrance) =>
            Grab(name, position, size, mass, encumbrance, GrabKind.Heavy);

        private static void Grab(string name, Vector3 position, Vector3 size, float mass,
                                 float encumbrance, GrabKind kind)
        {
            var go = Box(name, position, size);

            var body = go.AddComponent<Rigidbody>();
            body.mass = mass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            go.AddComponent<NetworkObject>();

            var grabbable = go.AddComponent<Grabbable>();
            var so = new SerializedObject(grabbable);
            so.FindProperty("displayName").stringValue = name.ToLowerInvariant();
            so.FindProperty("toolId").stringValue = kind == GrabKind.Tool ? name.ToLowerInvariant() : "";
            so.FindProperty("kind").enumValueIndex = (int)kind;
            so.FindProperty("encumbrance").floatValue = encumbrance;
            so.ApplyModifiedPropertiesWithoutUndo();

            // A tool's authority moves with whoever holds it. A heavy object's never does -
            // that is the whole reason two people can haul one without fighting over ownership.
            var nt = go.AddComponent<NetworkTransform>();
            ConfigureTransform(nt, localSpace: false, syncScale: false,
                               ownerAuthority: kind == GrabKind.Tool);
        }

        // ------------------------------------------------------------------ 7

        private const string SurgeryAssetDir = "Assets/Surgery";

        [MenuItem("Probation/Setup/7 - Build The Ward", priority = 6)]
        public static void BuildTheWard()
        {
            if (Object.FindFirstObjectByType<NetworkManager>() == null)
            {
                Debug.LogError("No NetworkManager in the scene. Run step 4 first.");
                return;
            }

            System.IO.Directory.CreateDirectory(SurgeryAssetDir);
            var species = BuildSpecies();
            var triage = BuildTriage();
            var extraction = BuildExtraction();
            AssetDatabase.SaveAssets();

            var old = GameObject.Find("Ward");
            if (old != null) Object.DestroyImmediate(old);
            foreach (var patient in Object.FindObjectsByType<Patient>(FindObjectsSortMode.None))
                Object.DestroyImmediate(patient.gameObject);

            var ward = new GameObject("Ward");

            // Six beds in two rows, far enough apart that crossing the ward costs you time.
            for (int i = 0; i < 6; i++)
            {
                float x = -6f + i % 3 * 6f;
                float z = 4f + i / 3 * 7f;
                Bed(ward.transform, i + 1, new Vector3(x, 0f, z));
            }

            // More bodies than beds, so intake never runs dry. Half want the quick job, half
            // want the two-handed one - a ward that is all one or all the other has no triage
            // decision in it.
            for (int i = 0; i < 8; i++)
                BuildPatient(ward.transform, $"Patient {i + 1}", species, i % 2 == 0 ? triage : extraction);

            Zone(ward.transform, "Discharge bay", new Vector3(10f, 1f, 12f), WardZoneKind.Discharge);
            Zone(ward.transform, "Morgue", new Vector3(-10f, 1f, 12f), WardZoneKind.Morgue);
            BuildSteriliser(ward.transform, new Vector3(0f, 0.6f, -2f));

            // PEAK's backpack, as furniture. Instruments sit on it, you shove it to whichever
            // bed is on fire, and anybody can shove it somewhere else. It extends what the team
            // can carry without ever giving anybody an inventory.
            InstrumentTray(ward.transform, new Vector3(-2.5f, 0.55f, 0f));

            // One monitor, six beds. Whoever wants a heartbeat has to go and fetch it.
            BuildMonitor(new Vector3(2.5f, 0.55f, 0f));
            var monitor = GameObject.Find("Vitals monitor");
            if (monitor != null) monitor.transform.SetParent(ward.transform, true);

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Debug.Log("[Probation] Ward built: 6 beds, 8 patients, discharge, morgue, steriliser.");
        }

        private static void Bed(Transform parent, int number, Vector3 position)
        {
            var table = Box($"Bed {number}", position + Vector3.up * 0.45f, new Vector3(1.0f, 0.9f, 2.1f));
            table.transform.SetParent(parent, true);

            var surface = new GameObject("Surface");
            surface.transform.SetParent(table.transform, false);
            surface.transform.localPosition = new Vector3(0f, 0.75f, 0f);

            var bed = table.AddComponent<WardBed>();
            SetRefs(bed, ("surface", surface.transform));
        }

        private static void Zone(Transform parent, string name, Vector3 position, WardZoneKind kind)
        {
            var go = Box(name, position, new Vector3(4f, 2f, 4f));
            go.transform.SetParent(parent, true);

            // A doorway you push a gurney through, not a wall you bump into.
            var collider = go.GetComponent<BoxCollider>();
            collider.isTrigger = true;

            var zone = go.AddComponent<WardZone>();
            var so = new SerializedObject(zone);
            so.FindProperty("kind").enumValueIndex = (int)kind;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void InstrumentTray(Transform parent, Vector3 position)
        {
            var go = Box("Instrument tray", position, new Vector3(1.1f, 0.75f, 0.7f));
            go.transform.SetParent(parent, true);

            var body = go.AddComponent<Rigidbody>();
            body.mass = 18f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            go.AddComponent<NetworkObject>();

            var grabbable = go.AddComponent<Grabbable>();
            var so = new SerializedObject(grabbable);
            so.FindProperty("displayName").stringValue = "instrument tray";
            so.FindProperty("toolId").stringValue = "";
            so.FindProperty("kind").enumValueIndex = (int)GrabKind.Heavy;
            so.FindProperty("encumbrance").floatValue = 0.35f;
            so.ApplyModifiedPropertiesWithoutUndo();

            ConfigureTransform(go.AddComponent<NetworkTransform>(),
                               localSpace: false, syncScale: false, ownerAuthority: false);
        }

        private static void BuildSteriliser(Transform parent, Vector3 position)
        {
            var go = Box("Steriliser", position, new Vector3(1.6f, 1.2f, 1.0f));
            go.transform.SetParent(parent, true);
            go.GetComponent<BoxCollider>().isTrigger = true;
            go.AddComponent<Steriliser>();
        }

        private static Species BuildSpecies()
        {
            var species = LoadOrCreate<Species>($"{SurgeryAssetDir}/Species_Thoracid.asset");
            species.displayName = "Thoracid";
            species.restingHeartRate = 68f;
            species.criticalHeartRate = 195f;
            species.bleedOutSeconds = 45f;
            species.wakesToNoise = true;          // volume becomes an input
            species.allergicToMetal = false;
            species.diagnosisText = "foreign body lodged in the upper cavity";
            EditorUtility.SetDirty(species);
            return species;
        }

        /// <summary>The quick job. Most patients want this - it is the ward's baseline tempo.</summary>
        private static Procedure BuildTriage()
        {
            var procedure = LoadOrCreate<Procedure>($"{SurgeryAssetDir}/Procedure_Triage.asset");
            procedure.displayName = "triage";
            procedure.description = "Put them under, close them up, get them out.";
            procedure.steps = new System.Collections.Generic.List<ProcedureStep>
            {
                new() { displayName = "Sedate", requiredToolId = "gas rig", targetSite = "torso",
                        handsRequired = 1, holdSeconds = 1.6f, tolerance = 0.45f,
                        requiresUnconscious = false, wrongToolHarm = 0.05f, sedates = true },
                new() { displayName = "Close them up", requiredToolId = "suture kit", targetSite = "torso",
                        handsRequired = 1, holdSeconds = 2.4f, tolerance = 0.4f,
                        wrongToolHarm = 0.12f, closesBleed = true },
            };
            EditorUtility.SetDirty(procedure);
            return procedure;
        }

        /// <summary>
        /// The one that needs somebody else. handsRequired is what makes co-op structural
        /// rather than decorative, and the scene ships two retractors because two hands means
        /// two owner-authoritative objects, never one object with two owners.
        /// </summary>
        private static Procedure BuildExtraction()
        {
            var procedure = LoadOrCreate<Procedure>($"{SurgeryAssetDir}/Procedure_Extraction.asset");
            procedure.displayName = "extraction";
            procedure.description = "Get the thing out. You cannot do it alone.";
            procedure.steps = new System.Collections.Generic.List<ProcedureStep>
            {
                new() { displayName = "Sedate", requiredToolId = "gas rig", targetSite = "torso",
                        handsRequired = 1, holdSeconds = 1.6f, tolerance = 0.45f,
                        requiresUnconscious = false, wrongToolHarm = 0.05f, sedates = true },
                new() { displayName = "Hold the seam open", requiredToolId = "retractor", targetSite = "torso",
                        handsRequired = 2, holdSeconds = 2f, tolerance = 0.5f,
                        wrongToolHarm = 0.1f, opensBleed = true, bleedRatePerSecond = 0.02f },
                new() { displayName = "Extract and close", requiredToolId = "forceps", targetSite = "cavity",
                        handsRequired = 1, holdSeconds = 2.4f, tolerance = 0.4f,
                        wrongToolHarm = 0.18f, closesBleed = true },
            };
            EditorUtility.SetDirty(procedure);
            return procedure;
        }

        private static void BuildPatient(Transform parent, string name, Species species, Procedure procedure)
        {
            var go = Box(name, new Vector3(0f, -40f, 0f), new Vector3(0.55f, 0.35f, 1.7f));
            go.transform.SetParent(parent, true);

            var body = go.AddComponent<Rigidbody>();
            body.mass = 70f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            go.AddComponent<NetworkObject>();

            var patient = go.AddComponent<Patient>();
            SetRefs(patient, ("species", species));

            var operation = go.AddComponent<Operation>();
            SetRefs(operation, ("procedure", procedure));

            // A patient is haulable, and stays haulable after it dies. A corpse is a physical
            // problem somebody has to move, not a despawn.
            var grabbable = go.AddComponent<Grabbable>();
            var so = new SerializedObject(grabbable);
            so.FindProperty("displayName").stringValue = "patient";
            so.FindProperty("toolId").stringValue = "";
            so.FindProperty("kind").enumValueIndex = (int)GrabKind.Heavy;
            so.FindProperty("encumbrance").floatValue = 0.85f;
            so.ApplyModifiedPropertiesWithoutUndo();

            ConfigureTransform(go.AddComponent<NetworkTransform>(),
                               localSpace: false, syncScale: false, ownerAuthority: false);

            Site(go.transform, "torso", new Vector3(0f, 0.2f, 0f));
            Site(go.transform, "cavity", new Vector3(0f, 0.2f, 0.45f));

        }

        /// <summary>
        /// The monitor cart. Heavy rather than a tool, so it is shoved into place with the grab
        /// beam - and so two people can fight over where it goes.
        /// </summary>
        private static void BuildMonitor(Vector3 position)
        {
            var old = GameObject.Find("Vitals monitor");
            if (old != null) Object.DestroyImmediate(old);

            var go = Box("Vitals monitor", position, new Vector3(0.45f, 1.1f, 0.4f));

            var body = go.AddComponent<Rigidbody>();
            body.mass = 25f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            go.AddComponent<NetworkObject>();

            var grabbable = go.AddComponent<Grabbable>();
            var so = new SerializedObject(grabbable);
            so.FindProperty("displayName").stringValue = "vitals monitor";
            so.FindProperty("toolId").stringValue = "";
            so.FindProperty("kind").enumValueIndex = (int)GrabKind.Heavy;
            so.FindProperty("encumbrance").floatValue = 0.3f;
            so.ApplyModifiedPropertiesWithoutUndo();

            ConfigureTransform(go.AddComponent<NetworkTransform>(),
                               localSpace: false, syncScale: false, ownerAuthority: false);

            go.AddComponent<AudioSource>();
            go.AddComponent<VitalsMonitor>();
        }

        private static void Site(Transform parent, string siteId, Vector3 localPosition)
        {
            var go = new GameObject($"Site_{siteId}");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.AddComponent<SurgerySite>().siteId = siteId;
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        // ------------------------------------------------------------------ verify

        /// <summary>
        /// Checks the scene has everything the current scripts expect and adds whatever is
        /// missing. Exists because the setup steps have grown over time, so a scene set up two
        /// steps ago is quietly missing components added since - which presents as "the UI does
        /// not show up" rather than as an error.
        /// </summary>
        [MenuItem("Probation/Verify and Repair Scene", priority = 20)]
        public static void VerifyScene()
        {
            int added = 0, problems = 0;

            var manager = Object.FindFirstObjectByType<NetworkManager>();
            if (manager == null)
            {
                Debug.LogError("[Verify] No NetworkManager. Run step 4.");
                return;
            }

            var go = manager.gameObject;
            added += Ensure<UnityTransport>(go);
            added += Ensure<NetworkBootstrap>(go);
            added += Ensure<NetworkDiagnostics>(go);
            added += Ensure<ShiftDirector>(go);
            added += Ensure<SurgeryHud>(go);
            added += Ensure<ShiftHud>(go);
            added += Ensure<PatientIntake>(go);
            added += Ensure<ComplicationDirector>(go);
            added += Ensure<SteamManager>(go);
            added += Ensure<SteamLobbyBootstrap>(go);
            added += Ensure<FacepunchTransport>(go);

            var so = new SerializedObject(manager);
            if (so.FindProperty("NetworkConfig.NetworkTransport")?.objectReferenceValue == null)
            {
                AssignReference(so, "NetworkConfig.NetworkTransport", go.GetComponent<UnityTransport>());
                so.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log("[Verify] NetworkConfig.NetworkTransport was unset - pointed at UnityTransport.");
                added++;
            }

            var prefabProp = new SerializedObject(manager).FindProperty("NetworkConfig.PlayerPrefab");
            if (prefabProp == null || prefabProp.objectReferenceValue == null)
            {
                Debug.LogError("[Verify] NetworkConfig.PlayerPrefab is not set. Run step 4.");
                problems++;
            }

            if (Object.FindFirstObjectByType<LobbyCamera>() == null)
            {
                EnsureLobbyCamera();
                added++;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (prefab != null)
            {
                foreach (var (type, label) in new (System.Type, string)[]
                {
                    (typeof(PlayerCarry), nameof(PlayerCarry)),
                    (typeof(CursorLock), nameof(CursorLock)),
                    (typeof(PlayerNetworkSetup), nameof(PlayerNetworkSetup)),
                })
                {
                    if (prefab.GetComponent(type) != null) continue;
                    Debug.LogError($"[Verify] Player prefab is missing {label}. Run step 4.");
                    problems++;
                }
            }

            int beds = Object.FindObjectsByType<WardBed>(FindObjectsSortMode.None).Length;
            int zones = Object.FindObjectsByType<WardZone>(FindObjectsSortMode.None).Length;
            int sterilisers = Object.FindObjectsByType<Steriliser>(FindObjectsSortMode.None).Length;
            if (beds == 0 || zones < 2 || sterilisers == 0)
            {
                Debug.LogWarning($"[Verify] Ward incomplete: {beds} beds, {zones} zones, {sterilisers} sterilisers. Run step 7.");
                problems++;
            }

            int patients = Object.FindObjectsByType<Patient>(FindObjectsSortMode.None).Length;
            int monitors = Object.FindObjectsByType<VitalsMonitor>(FindObjectsSortMode.None).Length;
            int tools = 0;
            foreach (var g in Object.FindObjectsByType<Grabbable>(FindObjectsSortMode.None))
                if (!string.IsNullOrEmpty(g.ToolId)) tools++;

            if (patients == 0) { Debug.LogWarning("[Verify] No patients in the scene. Run step 7."); problems++; }
            if (monitors == 0) { Debug.LogWarning("[Verify] No vitals monitor. Run step 7."); problems++; }
            if (tools == 0) { Debug.LogWarning("[Verify] No tools with ids. Run step 6."); problems++; }

            PutGreyboxFirstInBuild();

            SetProjectSetting("ProjectSettings/TimeManager.asset", "Fixed Timestep", 1f / 60f);
            SetGravity(GameGravity);
            if (prefab != null) TuneLocomotion();

            if (added > 0) EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            Debug.Log($"[Verify] {beds} beds, {patients} patients, {monitors} monitors, {tools} tools. " +
                      $"Added {added} missing component(s), {problems} problem(s) needing a setup step. " +
                      (added > 0 ? "SAVE THE SCENE." : "Nothing to add."));
        }

        private static int Ensure<T>(GameObject go) where T : Component
        {
            if (go.GetComponent<T>() != null) return 0;
            go.AddComponent<T>();
            Debug.Log($"[Verify] Added missing {typeof(T).Name} to {go.name}.");
            return 1;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>
        /// A camera that exists before NetworkManager spawns your player, so the pre-connect
        /// state is the room rather than a black screen.
        /// </summary>
        private static void EnsureLobbyCamera()
        {
            if (Object.FindFirstObjectByType<LobbyCamera>() != null) return;

            var go = new GameObject("LobbyCamera");
            go.transform.position = new Vector3(0f, 3.5f, -7f);
            go.transform.rotation = Quaternion.Euler(18f, 0f, 0f);

            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 60f;
            go.AddComponent<AudioListener>();
            go.AddComponent<LobbyCamera>();

            Debug.Log("[Probation] Added a LobbyCamera - without one the build boots to black.");
        }

        /// <summary>
        /// A build boots whatever sits at index 0. The URP template leaves SampleScene there,
        /// so a fresh build opens an empty template scene and looks broken.
        /// </summary>
        private static void PutGreyboxFirstInBuild()
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            int index = scenes.FindIndex(x => x.path == GreyboxScenePath);

            if (index < 0) scenes.Insert(0, new EditorBuildSettingsScene(GreyboxScenePath, true));
            else if (index > 0)
            {
                var entry = scenes[index];
                scenes.RemoveAt(index);
                scenes.Insert(0, entry);
            }
            else return;

            scenes[0].enabled = true;
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log("[Probation] Greybox moved to build index 0 - builds boot into it now.");
        }

        private static GameObject Box(string name, Vector3 position, Vector3 size)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.position = position;
            go.transform.localScale = size;
            return go;
        }

        private static GameObject Ramp(string name, Vector3 position, float angle)
        {
            var go = Box(name, position, new Vector3(3f, 0.2f, 5f));
            go.transform.rotation = Quaternion.Euler(-angle, 0f, 0f);
            return go;
        }

        private static void SetRefs(Object target, params (string field, Object value)[] refs)
        {
            var so = new SerializedObject(target);
            foreach (var (field, value) in refs)
            {
                var prop = so.FindProperty(field);
                if (prop == null) { Debug.LogWarning($"[Probation] {target.GetType().Name} has no field '{field}'."); continue; }
                prop.objectReferenceValue = value;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetMask(Object target, string field, int mask)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null) { Debug.LogWarning($"[Probation] {target.GetType().Name} has no field '{field}'."); return; }
            prop.intValue = mask;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static bool AssignReference(SerializedObject so, string propertyPath, Object value)
        {
            var prop = so.FindProperty(propertyPath);
            if (prop == null) return false;
            prop.objectReferenceValue = value;
            return true;
        }

        /// <summary>Game gravity, not Earth gravity. See the note in ConfigureProject.</summary>
        private const float GameGravity = -24f;

        private static bool SetGravity(float y)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset");
            if (assets == null || assets.Length == 0) return false;

            var so = new SerializedObject(assets[0]);
            var prop = so.FindProperty("m_Gravity");
            if (prop == null) return false;

            prop.vector3Value = new Vector3(0f, y, 0f);
            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        /// <summary>
        /// Rewrites movement tuning onto the existing Player prefab. Changing the defaults in
        /// PlayerLocomotion.cs does nothing to a prefab that already exists - its values were
        /// serialised when it was built, which is why stale tuning survives a code change.
        /// </summary>
        private static void TuneLocomotion()
        {
            var contents = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                var locomotion = contents.GetComponent<PlayerLocomotion>();
                if (locomotion == null) return;

                var so = new SerializedObject(locomotion);
                void Set(string field, float value)
                {
                    var prop = so.FindProperty(field);
                    if (prop != null) prop.floatValue = value;
                }

                // Faster, because a six bed ward is a lot of floor to cross.
                Set("walkSpeed", 4.2f);
                Set("sprintSpeed", 6.8f);
                Set("crouchSpeed", 1.8f);

                // Snappier starts and stops. Most of "floaty" is really "slow to change".
                Set("groundAcceleration", 65f);
                Set("airAcceleration", 14f);

                // The ride spring has to hold more weight now gravity is stronger, and the
                // damper tracks it - critical damping is about 2*sqrt(spring * mass).
                Set("rideSpring", 45000f);
                Set("rideDamper", 3600f);

                // Base gravity does the work now, so this stops being a sledgehammer.
                Set("fallGravityMultiplier", 1.25f);
                Set("jumpHeight", 0.85f);

                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(contents, PlayerPrefabPath);
                Debug.Log("[Probation] Movement retuned on the Player prefab.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static bool SetProjectSetting(string assetPath, string propertyName, float value)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            if (assets == null || assets.Length == 0) return false;

            var so = new SerializedObject(assets[0]);
            var prop = so.FindProperty(propertyName);
            if (prop == null) return false;

            prop.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        private static int EnsureLayer(string layerName)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return -1;

            var tagManager = new SerializedObject(assets[0]);
            var layers = tagManager.FindProperty("layers");

            for (int i = 0; i < layers.arraySize; i++)
                if (layers.GetArrayElementAtIndex(i).stringValue == layerName) return i;

            // 0-7 are reserved by Unity.
            for (int i = 8; i < layers.arraySize; i++)
            {
                var element = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(element.stringValue)) continue;
                element.stringValue = layerName;
                tagManager.ApplyModifiedPropertiesWithoutUndo();
                return i;
            }
            return -1;
        }
    }
}
