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
                // First, before anything is added. See StripMissingScripts - an orphaned
                // component silently throws away every other edit made in this block.
                StripMissingScripts(contents);

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

                var (brace, hands) = WireVerbs(contents);

                SetRefs(setup,
                    ("input", contents.GetComponent<PlayerInputReader>()),
                    ("look", pivot != null ? pivot.GetComponent<PlayerLook>() : null),
                    ("locomotion", contents.GetComponent<PlayerLocomotion>()),
                    ("interactor", contents.GetComponent<PlayerInteractor>()),
                    ("cursorLock", cursorLock),
                    ("carry", carry),
                    ("brace", brace),
                    ("hands", hands),
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

        /// <summary>
        /// Remove components whose script no longer exists, anywhere in the prefab.
        ///
        /// These are not merely untidy. Unity will not reliably serialise modifications to a
        /// prefab that is carrying one, so every AddComponent performed alongside it is quietly
        /// thrown away and the menu item reports success having changed absolutely nothing on
        /// disk. The symptom is the worst kind of loop: PlayerNetworkSetup re-adds PlayerBrace
        /// and PlayerHands at runtime every session and tells you to run step 4 to make it
        /// stick, running step 4 appears to work, and nothing is different next time.
        ///
        /// Deleting a component whose script is gone is always safe - there is no script left to
        /// read whatever was serialised on it.
        /// </summary>
        private static int StripMissingScripts(GameObject contents)
        {
            int removed = 0;

            foreach (var transform in contents.GetComponentsInChildren<Transform>(true))
                removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transform.gameObject);

            if (removed > 0)
                Debug.Log($"[Probation] Removed {removed} component(s) from the Player prefab whose " +
                          "script no longer exists. Those were stopping every other change to this " +
                          "prefab from being saved.");

            return removed;
        }

        /// <summary>
        /// Add and wire <see cref="PlayerBrace"/> on an already-loaded prefab root.
        ///
        /// Shared so that a scene which needs bracing can guarantee it rather than assuming
        /// somebody remembered to run step 4 - a prefab with no PlayerBrace on it presents as
        /// "right mouse does nothing", with no error anywhere to explain why.
        /// </summary>
        private static PlayerBrace WireBrace(GameObject contents) => WireVerbs(contents).brace;

        /// <summary>
        /// Add and wire the components that carry the surgical verbs - bracing, and the bare hand
        /// that holds pressure. Both are newer than the Player prefab, so both are missing from
        /// anything authored before them.
        /// </summary>
        private static (PlayerBrace brace, PlayerHands hands) WireVerbs(GameObject contents)
        {
            var brace = AddBrace(contents);

            var hands = contents.GetComponent<PlayerHands>();
            if (hands == null)
            {
                hands = contents.AddComponent<PlayerHands>();
                Debug.Log("[Probation] Added missing PlayerHands to the Player prefab.");
            }

            SetRefs(hands,
                ("input", contents.GetComponent<PlayerInputReader>()),
                ("interactor", contents.GetComponent<PlayerInteractor>()),
                ("carry", contents.GetComponent<PlayerCarry>()));

            return (brace, hands);
        }

        private static PlayerBrace AddBrace(GameObject contents)
        {
            var pivot = contents.transform.Find("CameraPivot");
            var camera = pivot != null ? pivot.Find("Camera") : null;

            var brace = contents.GetComponent<PlayerBrace>();
            if (brace == null)
            {
                brace = contents.AddComponent<PlayerBrace>();
                Debug.Log("[Probation] Added missing PlayerBrace to the Player prefab.");
            }

            // Brace leans the CAMERA, never the pivot - Locomotion owns the pivot's local Y for
            // crouch and eye height, and the two would fight over it every frame.
            SetRefs(brace,
                ("input", contents.GetComponent<PlayerInputReader>()),
                ("interactor", contents.GetComponent<PlayerInteractor>()),
                ("carry", contents.GetComponent<PlayerCarry>()),
                ("look", pivot != null ? pivot.GetComponent<PlayerLook>() : null),
                ("locomotion", contents.GetComponent<PlayerLocomotion>()),
                ("view", camera != null ? camera.GetComponent<Camera>() : null));

            // Your own capsule is not a surface you can brace against. The raycast starts inside
            // it, which Unity would normally ignore, but crouching puts the eye level with your
            // own shoulders and that stops being reliable.
            int playerLayer = LayerMask.NameToLayer(PlayerLayerName);
            if (playerLayer >= 0) SetMask(brace, "workMask", ~(1 << playerLayer));

            return brace;
        }

        /// <summary>
        /// Guarantee the saved Player prefab can brace, wiring it if it cannot. Returns false only
        /// if the prefab is missing entirely.
        /// </summary>
        private static bool EnsurePlayerCanBrace()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (prefab == null) return false;

            if (prefab.GetComponent<PlayerBrace>() != null &&
                prefab.GetComponent<PlayerHands>() != null) return true;

            var contents = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                StripMissingScripts(contents);

                var (brace, hands) = WireVerbs(contents);

                // Without these the gates in PlayerNetworkSetup never fire and remote players keep
                // live copies of both.
                var setup = contents.GetComponent<PlayerNetworkSetup>();
                if (setup != null) SetRefs(setup, ("brace", brace), ("hands", hands));

                PrefabUtility.SaveAsPrefabAsset(contents, PlayerPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            AssetDatabase.SaveAssets();
            return true;
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
            var scalpel = Tool("Scalpel", new Vector3(-2.2f, 1.0f, -4.5f), new Vector3(0.04f, 0.03f, 0.28f), 0.3f, 0.05f);

            // The first instrument that actually does something when you press the button. Until
            // now the scalpel was the one tool no procedure step ever asked for - it existed
            // purely to be the wrong answer.
            scalpel.AddComponent<ScalpelTool>();
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

        private static GameObject Tool(string name, Vector3 position, Vector3 size, float mass, float encumbrance)
        {
            var go = Grab(name, position, size, mass, encumbrance, GrabKind.Tool);

            // Every instrument gets a working end. Convention is local +Z, so the tip sits on the
            // far +Z face - which is why every tool above is authored long on Z.
            var tip = new GameObject("Tip");
            tip.transform.SetParent(go.transform, false);
            tip.transform.localPosition = new Vector3(0f, 0f, 0.5f);
            tip.AddComponent<ToolTip>();

            return go;
        }

        private static GameObject Heavy(string name, Vector3 position, Vector3 size, float mass, float encumbrance) =>
            Grab(name, position, size, mass, encumbrance, GrabKind.Heavy);

        private static GameObject Grab(string name, Vector3 position, Vector3 size, float mass,
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

            return go;
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
            var thoracid = BuildThoracid();
            var vithrid = BuildVithrid();
            var triage = BuildTriage();
            var extraction = BuildExtraction();
            var (foreignBody, laceration, brood) = BuildConditions(thoracid, vithrid, triage, extraction);
            var casebook = BuildCasebook(thoracid, vithrid, triage, extraction, foreignBody, laceration, brood);
            AssetDatabase.SaveAssets();

            var old = GameObject.Find("Ward");
            if (old != null) Object.DestroyImmediate(old);
            foreach (var patient in Object.FindObjectsByType<Patient>(FindObjectsSortMode.None))
                Object.DestroyImmediate(patient.gameObject);

            var ward = new GameObject("Ward");
            var t = ward.transform;

            // Intake at the south end, theatres along the north, discharge and morgue at the far
            // corners. Everything has to cross the middle, which is where interns meet each other
            // going opposite ways with trolleys.
            Slab(t, "Ward floor", new Vector3(0f, -0.5f, 6f), new Vector3(42f, 1f, 34f), solid: true);

            Slab(t, "Wall S", new Vector3(0f, 2f, -10f), new Vector3(42f, 5f, 0.5f), solid: true);
            Slab(t, "Wall N", new Vector3(0f, 2f, 23f), new Vector3(42f, 5f, 0.5f), solid: true);
            Slab(t, "Wall W", new Vector3(-21f, 2f, 6f), new Vector3(0.5f, 5f, 34f), solid: true);
            Slab(t, "Wall E", new Vector3(21f, 2f, 6f), new Vector3(0.5f, 5f, 34f), solid: true);

            // Waiting room, with a gap in the middle you steer trolleys out through.
            Slab(t, "WAITING ROOM", new Vector3(0f, 0.02f, -6f), new Vector3(11f, 0.02f, 7f), solid: false);
            Slab(t, "Waiting wall W", new Vector3(-8f, 2f, -2f), new Vector3(10f, 5f, 0.5f), solid: true);
            Slab(t, "Waiting wall E", new Vector3(8f, 2f, -2f), new Vector3(10f, 5f, 0.5f), solid: true);
            for (int i = 0; i < 4; i++)
                Slab(t, $"Waiting chair {i + 1}", new Vector3(-4.5f + i * 3f, 0.45f, -8f),
                     new Vector3(0.7f, 0.9f, 0.7f), solid: true);

            for (int i = 0; i < 4; i++)
                Bay(t, i + 1, new Vector3(-13.5f + i * 9f, 0f, 15f));

            Zone(t, "Discharge bay", new Vector3(17f, 1.5f, -6f), new Vector3(6f, 4f, 6f), WardZoneKind.Discharge);
            Slab(t, "DISCHARGE", new Vector3(17f, 0.02f, -6f), new Vector3(6f, 0.02f, 6f), solid: false);

            Zone(t, "Morgue", new Vector3(-17f, 1.5f, -6f), new Vector3(6f, 4f, 6f), WardZoneKind.Morgue);
            Slab(t, "MORGUE", new Vector3(-17f, 0.02f, -6f), new Vector3(6f, 0.02f, 6f), solid: false);

            BuildSteriliser(t, new Vector3(0f, 0.7f, 3.5f));
            InstrumentTray(t, new Vector3(-3f, 0.55f, 3.5f));
            BuildMonitor(new Vector3(3f, 0.55f, 3.5f));
            var monitor = GameObject.Find("Vitals monitor");
            if (monitor != null) monitor.transform.SetParent(t, true);

            BuildIntakeBay(t);
            Slab(t, "INTAKE", new Vector3(0f, 0.02f, -5.5f), new Vector3(18f, 0.02f, 6f), solid: false);

            for (int i = 0; i < 6; i++)
                Trolley(t, i + 1, new Vector3(-6.5f + i * 2.6f, 0.5f, -5f));

            // No species and no procedure here any more. Both are dealt at admission from the
            // casebook, and a patient carrying an authored procedure would silently outrank
            // whatever the ward charted.
            for (int i = 0; i < 8; i++)
                BuildPatient(t, $"Patient {i + 1}");

            // Before looking for the intake, because this is what creates it in the right place.
            var manager = Object.FindFirstObjectByType<NetworkManager>();
            if (manager != null) BuildWardSystems(manager.gameObject);

            var intake = Object.FindFirstObjectByType<PatientIntake>();
            if (intake != null) SetRefs(intake, ("casebook", casebook));

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            Debug.Log("[Probation] Ward built: waiting room, 4 theatres, discharge, morgue, 6 gurneys, 8 patients.");
        }

        /// <summary>Geometry. Solid ones collide; the flat ones are floor markings.</summary>
        private static GameObject Slab(Transform parent, string name, Vector3 position, Vector3 size, bool solid)
        {
            var go = Box(name, position, size);
            go.transform.SetParent(parent, true);

            if (solid) go.isStatic = true;
            else Object.DestroyImmediate(go.GetComponent<Collider>());

            return go;
        }

        /// <summary>
        /// An operating theatre: two walls, an open front, and a volume you are allowed to work
        /// inside. The volume is the point - procedures do not progress in a corridor, which is
        /// what turns "a patient arrived" into "somebody wheel them somewhere".
        /// </summary>
        private static void Bay(Transform parent, int number, Vector3 origin)
        {
            Slab(parent, $"Theatre {number} wall W", origin + new Vector3(-3.5f, 2f, 3f), new Vector3(0.5f, 5f, 7f), solid: true);
            Slab(parent, $"Theatre {number} wall E", origin + new Vector3(3.5f, 2f, 3f), new Vector3(0.5f, 5f, 7f), solid: true);
            Slab(parent, $"THEATRE {number}", origin + new Vector3(0f, 0.02f, 3f), new Vector3(6.5f, 0.02f, 7f), solid: false);

            var volume = Box($"Bay {number}", origin + new Vector3(0f, 1.6f, 3f), new Vector3(6.5f, 3.5f, 7f));
            volume.transform.SetParent(parent, true);
            volume.GetComponent<BoxCollider>().isTrigger = true;
            Object.DestroyImmediate(volume.GetComponent<MeshRenderer>());

            var bay = volume.AddComponent<OperatingBay>();
            var so = new SerializedObject(bay);
            so.FindProperty("bayName").stringValue = $"Theatre {number}";
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// The volume new patients are admitted into. Without one, intake has nowhere to put
        /// anybody and the night silently never starts.
        /// </summary>
        private static void BuildIntakeBay(Transform parent)
        {
            var go = Box("Intake bay", new Vector3(0f, 1.5f, -5.5f), new Vector3(18f, 4f, 6f));
            if (parent != null) go.transform.SetParent(parent, true);

            go.GetComponent<BoxCollider>().isTrigger = true;
            Object.DestroyImmediate(go.GetComponent<MeshRenderer>());

            go.AddComponent<IntakeBay>();
            Debug.Log("[Probation] Intake bay built.");
        }

        private static void Trolley(Transform parent, int number, Vector3 position)
        {
            var go = Box($"Gurney {number}", position, new Vector3(0.9f, 1f, 2.1f));
            go.transform.SetParent(parent, true);

            var body = go.AddComponent<Rigidbody>();
            body.mass = 40f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            // A gurney that tips over is a bug, not a joke.
            body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            go.AddComponent<NetworkObject>();

            var surface = new GameObject("Surface");
            surface.transform.SetParent(go.transform, false);
            surface.transform.localPosition = new Vector3(0f, 0.65f, 0f);

            SetRefs(go.AddComponent<Gurney>(), ("surface", surface.transform));

            var grabbable = go.AddComponent<Grabbable>();
            var so = new SerializedObject(grabbable);
            so.FindProperty("displayName").stringValue = $"gurney {number}";
            so.FindProperty("toolId").stringValue = "";
            so.FindProperty("kind").enumValueIndex = (int)GrabKind.Heavy;
            so.FindProperty("encumbrance").floatValue = 0.5f;
            so.ApplyModifiedPropertiesWithoutUndo();

            ConfigureTransform(go.AddComponent<NetworkTransform>(),
                               localSpace: false, syncScale: false, ownerAuthority: false);
        }

        private static void Zone(Transform parent, string name, Vector3 position, Vector3 size, WardZoneKind kind)
        {
            var go = Box(name, position, size);
            go.transform.SetParent(parent, true);
            go.GetComponent<BoxCollider>().isTrigger = true;
            Object.DestroyImmediate(go.GetComponent<MeshRenderer>());

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
            var go = Box("Steriliser", position, new Vector3(1.6f, 1.4f, 1.0f));
            go.transform.SetParent(parent, true);
            go.GetComponent<BoxCollider>().isTrigger = true;
            go.AddComponent<Steriliser>();
        }

        /// <summary>
        /// The common one. Slow heart, tolerant of metal, and - the fact the whole diagnostic
        /// system turns on - it has a second heart sitting in the upper cavity as normal anatomy.
        /// </summary>
        private static Species BuildThoracid()
        {
            var species = LoadOrCreate<Species>($"{SurgeryAssetDir}/Species_Thoracid.asset");
            species.displayName = "Thoracid";
            species.restingHeartRate = 68f;
            species.criticalHeartRate = 195f;
            species.bleedOutSeconds = 45f;
            species.wakesToNoise = true;          // volume becomes an input
            species.allergicToMetal = false;
            EditorUtility.SetDirty(species);
            return species;
        }

        /// <summary>
        /// The other one, built to invert every human instinct the Thoracid teaches. It rests at
        /// 112, so a rate that means "this one is dying" on the next bed means nothing here; it
        /// bleeds out in twenty seconds; and it is the species that finally exercises
        /// allergicToMetal, a rule Operation has implemented since day one and never once run.
        /// </summary>
        private static Species BuildVithrid()
        {
            var species = LoadOrCreate<Species>($"{SurgeryAssetDir}/Species_Vithrid.asset");
            species.displayName = "Vithrid";
            species.restingHeartRate = 112f;
            species.criticalHeartRate = 240f;
            species.bleedOutSeconds = 20f;
            species.wakesToNoise = false;
            species.allergicToMetal = true;
            EditorUtility.SetDirty(species);
            return species;
        }

        /// <summary>
        /// The three conditions the ward opens with.
        ///
        /// Foreign body is the thesis: one presentation, two answers, and the answer that kills
        /// is the confident one. Its scanner lines and the brood's are deliberately near
        /// identical apart from the word that matters.
        /// </summary>
        private static (Condition foreignBody, Condition laceration, Condition brood) BuildConditions(
            Species thoracid, Species vithrid, Procedure triage, Procedure extraction)
        {
            var foreignBody = LoadOrCreate<Condition>($"{SurgeryAssetDir}/Condition_ForeignBody.asset");
            foreignBody.id = "foreign_body";
            foreignBody.displayName = "mass in the upper cavity";
            foreignBody.scannerLines = new[]
            {
                "dense mass, upper cavity",
                "no movement across repeat scans",
                "surrounding tissue undisturbed",
            };
            foreignBody.restingRateOffset = 14f;
            foreignBody.presentingSickness = 0.18f;
            foreignBody.arrivesHarmed = 0.05f;
            foreignBody.untreatedHarmPerSecond = 0.003f;
            foreignBody.answers = new System.Collections.Generic.List<ConditionAnswer>
            {
                // The trap. Nothing about this reads as a trap from across the ward, which is
                // the entire point - it presents exactly like the one you are meant to cut.
                //
                // Not instantly lethal, deliberately. Taking the heart out costs them 0.45 on
                // top of the per-step harm, and because the condition is never resolved they go
                // on deteriorating afterwards - so they are dying rather than dead, and a team
                // who notice can still save them. It becomes the slower, nastier version once
                // fragility lands and they crash during cover-up instead.
                new() { species = thoracid, treatment = null, harmIfOperated = 0.45f,
                        reviewLineWrong = "cut open a Thoracid to take out its second heart",
                        reviewLineRight = "left a Thoracid's second heart where it was" },

                new() { species = vithrid, treatment = extraction, reliefIfCorrect = 0.4f,
                        harmPerWrongStep = 0.06f, fragilityIfWrong = 0.6f,
                        reviewLineWrong = "operated on a Vithrid for the wrong thing",
                        reviewLineRight = "got a mass out of a Vithrid" },
            };
            EditorUtility.SetDirty(foreignBody);

            var laceration = LoadOrCreate<Condition>($"{SurgeryAssetDir}/Condition_Laceration.asset");
            laceration.id = "laceration";
            laceration.displayName = "open laceration";
            laceration.scannerLines = new[]
            {
                "pressure falling",
                "focal source at torso",
                "no mass detected",
            };
            laceration.restingRateOffset = 18f;
            laceration.presentingSickness = 0.2f;
            laceration.arrivesBleedingRate = 0.012f;
            laceration.arrivesHarmed = 0.08f;
            laceration.untreatedHarmPerSecond = 0.006f;
            laceration.answers = new System.Collections.Generic.List<ConditionAnswer>
            {
                // No species set: the fallback, and the reason night one is survivable. The same
                // answer on everybody makes this the condition new players learn the ward on.
                new() { species = null, treatment = triage, reliefIfCorrect = 0.45f,
                        harmPerWrongStep = 0.05f, fragilityIfWrong = 0.5f,
                        reviewLineWrong = "ran the wrong procedure on a bleeder",
                        reviewLineRight = "closed somebody up" },
            };
            EditorUtility.SetDirty(laceration);

            var brood = LoadOrCreate<Condition>($"{SurgeryAssetDir}/Condition_Brood.asset");
            brood.id = "brood";
            brood.displayName = "brood";
            brood.scannerLines = new[]
            {
                "mass, upper cavity",
                "movement across repeat scans",
                "surrounding tissue displaced",
            };
            brood.restingRateOffset = 18f;
            brood.presentingSickness = 0.22f;
            brood.arrivesHarmed = 0.1f;
            brood.untreatedHarmPerSecond = 0.005f;
            brood.answers = new System.Collections.Generic.List<ConditionAnswer>
            {
                new() { species = null, treatment = extraction, reliefIfCorrect = 0.35f,
                        harmPerWrongStep = 0.08f, fragilityIfWrong = 0.7f,
                        reviewLineWrong = "left a brood inside somebody",
                        reviewLineRight = "got a brood out intact" },
            };
            EditorUtility.SetDirty(brood);

            return (foreignBody, laceration, brood);
        }

        /// <summary>
        /// The registry every client resolves patient indices through.
        ///
        /// The arrival weights are the difficulty curve, and they are ordered as a teaching
        /// sequence: night one is nothing but lacerations so the ward can be learned at all,
        /// extraction arrives on night two, the Thoracid trap on night three once cutting a mass
        /// out has become the obvious move, and the brood on night four.
        /// </summary>
        private static Casebook BuildCasebook(Species thoracid, Species vithrid,
                                              Procedure triage, Procedure extraction,
                                              Condition foreignBody, Condition laceration, Condition brood)
        {
            var book = LoadOrCreate<Casebook>($"{SurgeryAssetDir}/Casebook.asset");

            // APPEND ONLY. These list positions are the wire format - see Casebook.
            book.species = new System.Collections.Generic.List<Species> { thoracid, vithrid };
            book.procedures = new System.Collections.Generic.List<Procedure> { triage, extraction };
            book.conditions = new System.Collections.Generic.List<Condition> { foreignBody, laceration, brood };

            book.arrivals = new System.Collections.Generic.List<CaseWeight>
            {
                new() { condition = laceration,  species = thoracid, weight = 3f,   fromNight = 1 },
                new() { condition = laceration,  species = vithrid,  weight = 3f,   fromNight = 1 },
                new() { condition = foreignBody, species = vithrid,  weight = 2f,   fromNight = 2 },
                new() { condition = foreignBody, species = thoracid, weight = 1.5f, fromNight = 3 },
                new() { condition = brood,       species = thoracid, weight = 1.5f, fromNight = 4 },
                new() { condition = brood,       species = vithrid,  weight = 1.5f, fromNight = 4 },
            };

            EditorUtility.SetDirty(book);
            return book;
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

        private static void BuildPatient(Transform parent, string name)
        {
            var go = Box(name, new Vector3(0f, -40f, 0f), new Vector3(0.55f, 0.35f, 1.7f));
            go.transform.SetParent(parent, true);

            var body = go.AddComponent<Rigidbody>();
            body.mass = 70f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            go.AddComponent<NetworkObject>();

            go.AddComponent<Patient>();
            go.AddComponent<Operation>();

            // A patient is haulable, and stays haulable after it dies. A corpse is a physical
            // problem somebody has to move, not a despawn.
            go.AddComponent<PatientAppearance>();

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = FleshMaterial();

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

            Chart(go.transform);
        }

        /// <summary>
        /// The board at the foot of the bed.
        ///
        /// Its own GameObject, and that is load-bearing rather than tidy: PlayerInteractor
        /// resolves focus through GetComponentInParent&lt;IInteractable&gt;, and the patient root is
        /// already a Grabbable, which is one too. On the root they would fight over the prompt
        /// and the winner would depend on component order. The collider is a trigger so it stays
        /// out of the patient's compound collider, where it would corrupt the impact speeds
        /// Patient.OnCollisionEnter reads - PlayerInteractor casts against triggers anyway.
        /// </summary>
        private static void Chart(Transform parent)
        {
            var go = new GameObject("Chart");
            go.transform.SetParent(parent, false);

            // Local space, so these are multiplied by the patient's own scale.
            go.transform.localPosition = new Vector3(0f, 0.8f, -0.62f);

            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(1.1f, 1.2f, 0.2f);

            go.AddComponent<PatientChart>();
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

        /// <summary>
        /// The one authored material in the project. Everything else is untextured greybox on
        /// purpose - the ward is meant to read as flat and clinical so the patients are the only
        /// organic thing in the building. That contrast is the art direction.
        /// </summary>
        private static Material FleshMaterial()
        {
            const string path = SurgeryAssetDir + "/M_PatientFlesh.mat";

            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            var shader = Shader.Find("Probation/PatientFlesh");
            if (shader == null)
            {
                Debug.LogWarning("[Probation] Probation/PatientFlesh not found - patients will use the default material.");
                return null;
            }

            var material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
            Debug.Log($"[Probation] Created {path}.");
            return material;
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

        // ------------------------------------------------------------------ 8

        private const string TestbedScenePath = "Assets/Scenes/Surgery_Testbed.unity";

        /// <summary>
        /// A room with nothing in it but things to cut.
        ///
        /// It exists to answer the question the rest of the surgery rehaul is waiting on: does
        /// dragging a blade along a line, blind, with speed punishing you, feel good for four
        /// seconds? Everything not needed to answer that is absent - no patients, no procedures,
        /// no shift, no quota, no ward.
        ///
        /// Three stations, because the work plane is captured from a raycast and frozen, and a
        /// single flat table would never prove that generalises. Three scalpel weights, because
        /// "does the lag feel like weight or like input lag" is the first thing to judge and you
        /// cannot judge it without something to compare against.
        ///
        /// It still runs a NetworkManager. Grabbable and PlayerCarry are NetworkBehaviours and
        /// are completely inert without one, so a "no netcode" testbed would test nothing.
        /// NetworkBootstrap.autoHost starts it, so pressing Play is the whole setup.
        /// </summary>
        [MenuItem("Probation/Setup/8 - Build Surgery Testbed", priority = 7)]
        public static void BuildSurgeryTestbed()
        {
            // Bracing is the entire point of this scene, so guarantee the prefab can do it rather
            // than assuming step 4 has been re-run since PlayerBrace was written. A prefab without
            // it presents as "right mouse does nothing", with nothing in the console to say why.
            if (!EnsurePlayerCanBrace())
            {
                Debug.LogError("[Probation] No Player prefab. Run steps 2 and 4 first.");
                return;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            EnsureLobbyCamera();

            // Bright and flat. A seam is a thin line on a dark surface, and half of judging a cut
            // is being able to see where the last one went.
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.3f;
            light.shadows = LightShadows.Soft;
            lightGo.transform.rotation = Quaternion.Euler(52f, -28f, 0f);
            RenderSettings.ambientIntensity = 1.2f;

            Box("Floor", new Vector3(0f, -0.5f, 0f), new Vector3(24f, 1f, 24f));

            var testbed = new GameObject("Testbed");
            testbed.AddComponent<SurgeryTestbed>();

            // Greybox gets its crosshair from SurgeryHud, which lives on the ward's NetworkManager
            // and drags in the whole shift with it. The testbed has none of that, so without this
            // there is no crosshair and no prompt - and aiming a spherecast at a bench with no
            // crosshair is indistinguishable from grabbing being broken.
            testbed.AddComponent<InteractionHud>();

            // --- network -----------------------------------------------------
            var managerGo = new GameObject("NetworkManager");
            var manager = managerGo.AddComponent<NetworkManager>();
            var transport = managerGo.AddComponent<UnityTransport>();

            var bootstrap = managerGo.AddComponent<NetworkBootstrap>();
            var bootSo = new SerializedObject(bootstrap);
            bootSo.FindProperty("autoHost").boolValue = true;
            bootSo.ApplyModifiedPropertiesWithoutUndo();

            var managerSo = new SerializedObject(manager);
            AssignReference(managerSo, "NetworkConfig.NetworkTransport", transport);
            AssignReference(managerSo, "NetworkConfig.PlayerPrefab", prefab);
            managerSo.ApplyModifiedPropertiesWithoutUndo();

            // NOTHING may be placed at the world origin. NetworkManager spawns the player at the
            // prefab's own transform, and Player.prefab sits at (0, 0, 0) - so the origin is the
            // spawn point. A bench there means spawning inside a solid collider, being ejected by
            // the solver, and taking the instruments with you.
            const float benchZ = 1.6f;
            const float stationZ = 4.2f;

            // --- three weights, straight ahead of the spawn ---------------------
            Box("Bench", new Vector3(0f, 0.45f, benchZ), new Vector3(2.4f, 0.9f, 0.7f));

            // Same shape, same tip, three masses. PlayerCarry's speed ceiling is
            // maxCarrySpeed / (mass * massDrag), so these three should feel genuinely different
            // in the hand - and if they do not, massDrag is the value to reach for first.
            Blade("Scalpel light", new Vector3(-0.7f, 1.0f, benchZ), 0.12f);
            Blade("Scalpel", new Vector3(0f, 1.0f, benchZ), 0.30f);
            Blade("Scalpel heavy", new Vector3(0.7f, 1.0f, benchZ), 0.90f);

            // --- three surfaces, past the bench ---------------------------------
            // Flat. The baseline, and the one that has to feel right first.
            Station("Flat", new Vector3(0f, 0f, stationZ), tilt: 0f);

            // Tilted. The work plane is captured from the surface normal, so this proves the
            // tangent basis is not quietly assuming the world is horizontal.
            Station("Tilted 20deg", new Vector3(2.8f, 0f, stationZ), tilt: 20f);

            // Near vertical. The awkward case: braced against something you cannot look down at.
            Station("Upright 75deg", new Vector3(-2.8f, 0f, stationZ), tilt: 75f);

            // Wounds are pooled, like patients and gurneys - nothing in this project spawns a
            // prefab at runtime. Twelve is well past what merging should ever let you reach; if
            // you can exhaust it, the merge radius is too small.
            var wounds = new GameObject("Wounds");
            for (int i = 0; i < 12; i++)
            {
                var go = new GameObject($"Wound {i}");
                go.transform.SetParent(wounds.transform, false);
                go.AddComponent<Wound>();
            }

            WarnIfSpawnBlocked();

            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, TestbedScenePath);

            // Make it part of the project, not just a file on disk: listed in Build Settings,
            // loadable by name, and present in a player build so the two-machine test can use it.
            AddSceneToBuild(TestbedScenePath);
            PutGreyboxFirstInBuild();
            AssetDatabase.SaveAssets();

            Debug.Log($"[Probation] Surgery testbed written to {TestbedScenePath}. Press Play. " +
                      "E to take a scalpel, walk to a station, hold RMB to brace, hold LMB and " +
                      "drag along the seam - slowly. R closes every seam so you can go again.");
        }

        /// <summary>
        /// The player is spawned by NetworkManager at Player.prefab's own transform, which is the
        /// world origin - so anything solid parked there is something the intern spawns inside of.
        ///
        /// The symptom is never "I am inside a bench". It is the solver ejecting you across the
        /// room and taking the loose props with you, which presents as "I cannot pick anything up".
        /// </summary>
        private static void WarnIfSpawnBlocked()
        {
            Physics.SyncTransforms();

            // Starts at 0.6 so the sphere at the bottom clears the floor's top face - otherwise
            // this fires on the floor every time and the warning becomes noise you learn to skip.
            var hits = Physics.OverlapCapsule(new Vector3(0f, 0.6f, 0f), new Vector3(0f, 1.6f, 0f),
                                              0.4f, ~0, QueryTriggerInteraction.Ignore);

            foreach (var hit in hits)
            {
                if (hit == null) continue;

                Debug.LogError($"[Probation] '{hit.name}' is sitting on the player spawn at the " +
                               "world origin. The intern will spawn inside it, get ejected, and " +
                               "scatter the instruments on the way out.", hit.gameObject);
            }
        }

        /// <summary>A table, a body-sized slab on it at some tilt, and a seam down the slab.</summary>
        private static void Station(string name, Vector3 origin, float tilt)
        {
            var root = new GameObject($"Station - {name}");
            root.transform.position = origin;

            Box($"{name} table", origin + new Vector3(0f, 0.45f, 0f), new Vector3(1.0f, 0.9f, 1.6f))
                .transform.SetParent(root.transform, true);

            // Tilt about Z so the slab leans left-right: you can still stand at the long side of
            // the table and look at the face of it, which is how you would stand at a patient.
            var rotation = Quaternion.Euler(0f, 0f, tilt);

            const float halfWidth = 0.31f, halfDepth = 0.11f, tableTop = 0.9f;

            // Lift it clear of its own table. A tilted box is taller than an untilted one, and at
            // 75 degrees a slab parked at a fixed height sinks a third of a metre into the table.
            float radians = tilt * Mathf.Deg2Rad;
            float halfHeight = halfWidth * Mathf.Abs(Mathf.Sin(radians))
                             + halfDepth * Mathf.Abs(Mathf.Cos(radians));

            var slab = Box($"{name} body",
                           origin + new Vector3(0f, tableTop + halfHeight + 0.01f, 0f),
                           new Vector3(halfWidth * 2f, halfDepth * 2f, 1.5f));
            slab.transform.rotation = rotation;
            slab.transform.SetParent(root.transform, true);

            // Sit the seam just proud of the slab's top face, in the slab's own frame, so it
            // stays on the surface at any tilt.
            Vector3 surfaceUp = rotation * Vector3.up;
            Vector3 seamCentre = slab.transform.position + surfaceUp * (halfDepth + 0.006f);

            BuildSeam(root.transform, seamCentre, rotation * Vector3.forward, length: 0.36f, points: 7);
        }

        private static void Blade(string name, Vector3 position, float mass)
        {
            var go = Tool(name, position, new Vector3(0.04f, 0.03f, 0.28f), mass, 0.05f);

            var blade = go.AddComponent<ScalpelTool>();
            var so = new SerializedObject(blade);
            so.FindProperty("showDebug").boolValue = true;      // tuning readout, testbed only
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>A straight seam of the given length, centred and running along an axis.</summary>
        private static void BuildSeam(Transform parent, Vector3 centre, Vector3 along,
                                      float length, int points)
        {
            var go = new GameObject("Seam");
            go.transform.position = centre;
            go.transform.rotation = Quaternion.LookRotation(along, Vector3.up);

            // Parented only AFTER the transform is set, and only to an unscaled root. Hanging a
            // seam off a scaled primitive stretches its points: a slab with lossyScale
            // (0.62, 0.22, 1.5) would throw the line off the end of the collider entirely.
            if (parent != null) go.transform.SetParent(parent, true);

            var line = go.AddComponent<LineRenderer>();
            line.material = new Material(Shader.Find("Sprites/Default"));
            line.useWorldSpace = true;
            line.textureMode = LineTextureMode.Stretch;
            line.numCapVertices = 2;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;

            var nodes = new Transform[points];
            for (int i = 0; i < points; i++)
            {
                float t = points > 1 ? i / (float)(points - 1) : 0.5f;

                var node = new GameObject($"P{i}");
                node.transform.SetParent(go.transform, false);
                node.transform.localPosition = new Vector3(0f, 0f, Mathf.Lerp(-length * 0.5f, length * 0.5f, t));
                nodes[i] = node.transform;
            }

            var seam = go.AddComponent<Seam>();
            var so = new SerializedObject(seam);
            var array = so.FindProperty("points");
            array.arraySize = points;
            for (int i = 0; i < points; i++)
                array.GetArrayElementAtIndex(i).objectReferenceValue = nodes[i];
            so.ApplyModifiedPropertiesWithoutUndo();
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

            // Verify repairs a WARD - it adds ShiftDirector, PatientIntake, the HUDs and the
            // complication director. Run against the testbed it would bolt a whole hospital onto
            // a scene whose entire point is that none of that is there.
            if (SceneManager.GetActiveScene().path == TestbedScenePath)
            {
                Debug.Log("[Verify] This is the surgery testbed, not a ward - nothing to repair. " +
                          "Rebuild it with Probation > Setup > 8 if it has drifted.");
                return;
            }

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
            added += Ensure<SurgeryHud>(go);
            added += Ensure<ShiftHud>(go);

            added += BuildWardSystems(go);
            problems += VerifyCasebook();

            // Wards built before the intake bay existed have no way to admit anybody, and the
            // symptom is simply that no patients ever appear. Repair it in place rather than
            // making you rebuild the ward and lose wherever everything has been parked.
            if (Object.FindFirstObjectByType<IntakeBay>() == null)
            {
                var ward = GameObject.Find("Ward");
                BuildIntakeBay(ward != null ? ward.transform : null);
                added++;
            }
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

            // Repairable rather than merely reportable: this one is new enough that every prefab
            // built before it is missing it, and the symptom is a silent right mouse button.
            if (!EnsurePlayerCanBrace())
            {
                Debug.LogError("[Verify] No Player prefab at all. Run step 2.");
                problems++;
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (prefab != null)
            {
                foreach (var (type, label) in new (System.Type, string)[]
                {
                    (typeof(PlayerCarry), nameof(PlayerCarry)),
                    (typeof(CursorLock), nameof(CursorLock)),
                    (typeof(PlayerNetworkSetup), nameof(PlayerNetworkSetup)),
                    (typeof(PlayerBrace), nameof(PlayerBrace)),
                })
                {
                    if (prefab.GetComponent(type) != null) continue;
                    Debug.LogError($"[Verify] Player prefab is missing {label}. Run step 4.");
                    problems++;
                }
            }

            int beds = Object.FindObjectsByType<OperatingBay>(FindObjectsSortMode.None).Length
                     + Object.FindObjectsByType<Gurney>(FindObjectsSortMode.None).Length;
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

        /// <summary>
        /// The casebook is load-bearing in a way nothing else in the scene is. Patients replicate
        /// their species and condition as indices into its lists, so a missing or mis-authored
        /// one produces a ward of patients with no species, no condition and no procedure - and
        /// not one error anywhere to say so. Everything here exists because the failure is silent.
        /// </summary>
        private static int VerifyCasebook()
        {
            var intake = Object.FindFirstObjectByType<PatientIntake>();
            if (intake == null) return 0;

            int problems = 0;

            var serialized = new SerializedObject(intake);
            var field = serialized.FindProperty("casebook");
            var book = field.objectReferenceValue as Casebook;

            if (book == null)
            {
                book = AssetDatabase.LoadAssetAtPath<Casebook>($"{SurgeryAssetDir}/Casebook.asset");
                if (book == null)
                {
                    Debug.LogError("[Verify] No casebook asset. Run step 7 - without one, every " +
                                   "patient is admitted with no species and no condition.");
                    return 1;
                }

                field.objectReferenceValue = book;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Debug.Log("[Verify] Reattached the casebook to PatientIntake.");
            }

            if (book.species.Count == 0 || book.procedures.Count == 0 || book.conditions.Count == 0)
            {
                Debug.LogError("[Verify] The casebook has an empty list. Run step 7.");
                problems++;
            }

            var ids = new System.Collections.Generic.HashSet<string>();
            foreach (var condition in book.conditions)
            {
                if (condition == null)
                {
                    Debug.LogError("[Verify] The casebook has an empty condition slot. Indices are " +
                                   "the wire format, so a hole here shifts every patient after it.");
                    problems++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(condition.id))
                {
                    Debug.LogError($"[Verify] Condition '{condition.name}' has no id.");
                    problems++;
                }
                else if (!ids.Add(condition.id))
                {
                    Debug.LogError($"[Verify] Two conditions share the id '{condition.id}'.");
                    problems++;
                }

                // A scanner line that contains the condition's own name hands over the answer,
                // and diagnosis becomes reading one label off one screen.
                foreach (string line in condition.scannerLines)
                {
                    if (string.IsNullOrEmpty(line) || string.IsNullOrWhiteSpace(condition.displayName)) continue;
                    if (!line.ToLowerInvariant().Contains(condition.displayName.ToLowerInvariant())) continue;

                    Debug.LogWarning($"[Verify] '{condition.name}' has a scanner line naming the " +
                                     $"condition itself ('{line}'). Signs, never the answer.");
                    problems++;
                }
            }

            foreach (var arrival in book.arrivals)
            {
                if (arrival?.condition == null || arrival.species == null) continue;
                if (arrival.condition.AnswerFor(arrival.species) != null) continue;

                Debug.LogError($"[Verify] '{arrival.condition.name}' has no answer for " +
                               $"{arrival.species.displayName}, and no fallback. That patient can " +
                               "be admitted and never correctly treated.");
                problems++;
            }

            // A patient with no chart cannot be diagnosed, cannot be operated on, and cannot be
            // discharged - they simply occupy a bed all night. Repair rather than report: wards
            // built before the chart existed are otherwise unplayable and give no reason why.
            foreach (var patient in Object.FindObjectsByType<Patient>(FindObjectsSortMode.None))
            {
                if (patient.GetComponentInChildren<PatientChart>(true) != null) continue;

                Chart(patient.transform);
                Debug.Log($"[Verify] Added the missing chart to {patient.name}.");
            }

            // Operation.SiteFor returns null on a miss and Evaluate silently does nothing, so a
            // step aimed at a site nobody has produces a patient who can never be operated on
            // and never explains why.
            var sites = new System.Collections.Generic.HashSet<string>();
            foreach (var site in Object.FindObjectsByType<SurgerySite>(FindObjectsSortMode.None))
                if (site != null) sites.Add(site.siteId);

            foreach (var procedure in book.procedures)
            {
                if (procedure == null) continue;

                foreach (var step in procedure.steps)
                {
                    if (sites.Contains(step.targetSite)) continue;

                    Debug.LogError($"[Verify] '{procedure.displayName}' step '{step.displayName}' " +
                                   $"targets site '{step.targetSite}', which no patient has.");
                    problems++;
                }
            }

            return problems;
        }

        private const string WardSystemsName = "Ward Systems";

        /// <summary>
        /// Give the ward's directors something they can actually spawn on.
        ///
        /// ShiftDirector, PatientIntake and ComplicationDirector are NetworkBehaviours, and a
        /// NetworkBehaviour only ever gets IsServer set inside UpdateNetworkProperties, which
        /// runs from NetworkObject spawn and nowhere else. They were sitting on the
        /// NetworkManager object, which has no NetworkObject and should never have one - so they
        /// never spawned, IsServer stayed false for the whole session, and every Update in all
        /// three returned on its first line.
        ///
        /// The symptom was a ward that started, drew a HUD, spawned a player and then simply did
        /// nothing forever: no patients, no shift clock, no complications, no announcements, and
        /// not one error anywhere to say why. Their own spawnable object fixes all of it.
        /// </summary>
        private static int BuildWardSystems(GameObject managerObject)
        {
            var systems = GameObject.Find(WardSystemsName);
            if (systems == null)
            {
                systems = new GameObject(WardSystemsName);
                Debug.Log($"[Verify] Created '{WardSystemsName}' - the ward's directors had nothing to spawn on.");
            }

            if (systems.GetComponent<NetworkObject>() == null) systems.AddComponent<NetworkObject>();

            int changed = 0;
            changed += Relocate<ShiftDirector>(managerObject, systems);
            changed += Relocate<PatientIntake>(managerObject, systems);
            changed += Relocate<ComplicationDirector>(managerObject, systems);
            return changed;
        }

        /// <summary>
        /// Move one component onto the systems object, keeping whatever was set on it in the
        /// inspector - PatientIntake is carrying the casebook reference, and a plain
        /// AddComponent would silently drop it.
        /// </summary>
        private static int Relocate<T>(GameObject from, GameObject to) where T : Component
        {
            var stray = from.GetComponent<T>();
            var already = to.GetComponent<T>();

            if (stray == null)
            {
                if (already != null) return 0;

                to.AddComponent<T>();
                return 1;
            }

            if (already == null)
            {
                UnityEditorInternal.ComponentUtility.CopyComponent(stray);
                UnityEditorInternal.ComponentUtility.PasteComponentAsNew(to);
            }

            Object.DestroyImmediate(stray);
            Debug.Log($"[Verify] Moved {typeof(T).Name} onto '{WardSystemsName}'. On the " +
                      "NetworkManager it could never spawn, so it never ran at all.");
            return 1;
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
        /// <summary>
        /// Register a scene in Build Settings so it is a real part of the project rather than a
        /// loose file - it shows up in File > Build Settings, it can be loaded by name, and it
        /// exists in a player build.
        ///
        /// Appended, never inserted at 0. Index 0 is what a build boots into, and that has to stay
        /// Greybox: a build that opens on the testbed is a build nobody can play.
        /// </summary>
        private static void AddSceneToBuild(string path)
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);

            int index = scenes.FindIndex(x => x.path == path);
            if (index >= 0)
            {
                if (scenes[index].enabled) return;

                scenes[index].enabled = true;
                EditorBuildSettings.scenes = scenes.ToArray();
                Debug.Log($"[Probation] Re-enabled {path} in Build Settings.");
                return;
            }

            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log($"[Probation] Added {path} to Build Settings at index {scenes.Count - 1}.");
        }

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
