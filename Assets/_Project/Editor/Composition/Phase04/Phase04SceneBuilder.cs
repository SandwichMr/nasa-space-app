using System;
using System.IO;
using Trainer.Unity.Bootstrap;
using Trainer.Unity.Bootstrap.BaseOperations;
using Trainer.Unity.Configuration;
using Trainer.Unity.Controls;
using Trainer.Unity.Networking.BaseOperations;
using Trainer.Unity.Networking.Expedition;
using Trainer.Unity.Networking.Outpost;
using Trainer.Unity.Presentation.BaseOperations;
using Trainer.Unity.Validation.Phase04;
using Trainer.Unity.World.BaseOperations;
using Trainer.Unity.World.Expedition;
using Trainer.Unity.World.Outpost;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Trainer.Tooling.Composition.Phase04
{
    /// <summary>
    /// D2 (single scene editor): builds the phase 4 scenario asset and the scene <c>Phase04_LivingBase</c> reproducibly
    /// from code. It reuses the phase 2 base, movement, avatar and session components, the phase 3 R key, crew presenter,
    /// astronaut and world prefab, and mounts on ONE port: D3's network host (authority-only) and client (client-only),
    /// D3's phase 4 E key, D4's phase 4 panel/world prefabs (required: no provisional view) with D4's camera framing, and
    /// D5's acceptance pilot (inert without -trainer-autopilot continuity). D4's recipe retires the phase 3 ExpeditionPanel:
    /// the phase 4 panel shows reserves, EVA margin and received advisories, so both HUDs never overlap.
    /// Menu: Tools/Trainer/Phase 4/Build scenario asset and scene. CLI:
    /// Unity -batchmode -nographics -projectPath &lt;repo&gt; -executeMethod Trainer.Tooling.Composition.Phase04.Phase04SceneBuilder.BuildFromCommandLine
    /// [-phase4ResetScenario] -logFile Logs/phase4-scene.log
    /// </summary>
    public static class Phase04SceneBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/Phase04_LivingBase.unity";
        public const string ScenarioFolder = "Assets/_Project/Data/Scenarios/Phase04";
        public const string ScenarioPath = ScenarioFolder + "/Phase04_LivingBase_Synthetic.asset";
        public const string Phase3ScenarioPath = "Assets/_Project/Data/Scenarios/Phase03/Phase03_EvaAndShelter_Synthetic.asset";
        public const string BaseInteriorPrefab = "Assets/_Project/Prefabs/World/Phase02/BaseInterior.prefab";
        public const string AstronautPrefab = "Assets/_Project/Prefabs/Crew/Phase03/ExpeditionAstronaut.prefab";
        public const string Phase3WorldPrefab = "Assets/_Project/Prefabs/World/Phase03/ExpeditionWorld.prefab";
        public const string Phase4PanelPrefab = "Assets/_Project/Prefabs/UI/Phase04/BaseOperationsPanel.prefab";
        public const string Phase4WorldPrefab = "Assets/_Project/Prefabs/World/Phase04/BaseOperationsWorld.prefab";
        public const string Phase3MountName = "Phase03 Presentation Mount";
        public const string Phase4MountName = "Phase04 Presentation Mount";

        [MenuItem("Tools/Trainer/Phase 4/Build scenario asset and scene")]
        public static void BuildMenu() => Build(false);

        public static void BuildFromCommandLine()
        {
            try
            {
                Build(Array.IndexOf(Environment.GetCommandLineArgs(), "-phase4ResetScenario") >= 0);
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        public static void Build(bool resetScenario)
        {
            EnsureScenario(resetScenario);
            // A Single-mode NewScene unloads unreferenced assets, so every asset is (re)loaded AFTER creating the scene.
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var scenario = AssetDatabase.LoadAssetAtPath<BaseOperationsScenarioDefinition>(ScenarioPath)
                ?? throw new InvalidOperationException("Missing " + ScenarioPath);
            string problem = scenario.Validate();
            if (problem != null) throw new InvalidOperationException("Scenario asset invalid: " + problem);
            var baseInteriorAsset = Load(BaseInteriorPrefab);
            var astronaut = Load(AstronautPrefab);
            var phase3World = Load(Phase3WorldPrefab);
            var phase4Panel = Load(Phase4PanelPrefab);
            var phase4World = Load(Phase4WorldPrefab);

            // Camera and light: same configuration as the phase 2 and 3 scenes.
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            cameraObject.transform.position = new Vector3(2.75f, 3.25f, -10f);
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.03137255f, 0.050980393f, 0.09019608f, 0f);
            camera.GetUniversalAdditionalCameraData();
            var framing = cameraObject.AddComponent<BaseOperationsCameraFraming>(); // D4 framing; never with LocalCameraFraming
            var lightObject = new GameObject("Global Light 2D");
            Light2D light = lightObject.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Global;

            // Composition and the authority-only root (single continuous authority + single runner).
            var compositionObject = new GameObject("BaseOperationsComposition");
            var composition = compositionObject.AddComponent<BaseOperationsSceneComposition>();
            compositionObject.AddComponent<BaseOperationsAutopilot>(); // D5, inert unless -trainer-autopilot continuity
            var authorityObject = new GameObject("BaseOperationsAuthority (authority only)");
            var runner = authorityObject.AddComponent<BaseOperationsRunner>();
            var bootstrap = authorityObject.AddComponent<BaseOperationsBootstrap>();
            var host = authorityObject.AddComponent<BaseOperationsNetworkHost>();
            var clientObject = new GameObject("BaseOperationsClient (client only)");
            var client = clientObject.AddComponent<BaseOperationsNetworkClient>();

            // Crew presentation: phase 2 movement (its own E disabled) and avatars, phase 3 keys and crew frames.
            var crewObject = new GameObject("CrewPresentation");
            var keyboard = crewObject.AddComponent<CrewKeyboardInput>();
            var avatars = crewObject.AddComponent<CrewAvatarSync>();
            var interaction = crewObject.AddComponent<ExpeditionInteractionInput>();
            var operationsKey = crewObject.AddComponent<BaseOperationsInteractionInput>();
            var presenter = crewObject.AddComponent<ExpeditionCrewPresenter>();
            var crewParent = new GameObject("Crew");

            var baseInterior = (GameObject)PrefabUtility.InstantiatePrefab(baseInteriorAsset, scene);
            baseInterior.name = "BaseInterior (D4 phase 2 prefab, phase 4 plan)";
            var baseView = baseInterior.GetComponent<BaseInteriorView>() ?? throw new InvalidOperationException("BaseInteriorView missing");
            var controlsObject = new GameObject("SessionControls");
            var controls = controlsObject.AddComponent<OutpostSessionControls>();

            // Phase 3 world (EVA, hatch, shelter signs, avatars' frames) on the same port. ExpeditionPanel is retired (D4 recipe).
            var mount3 = new GameObject(Phase3MountName);
            var world3Object = (GameObject)PrefabUtility.InstantiatePrefab(phase3World, scene);
            world3Object.transform.SetParent(mount3.transform, false);
            var world3 = world3Object.GetComponent<ExpeditionWorldView>() ?? throw new InvalidOperationException("ExpeditionWorldView missing");

            // Phase 4 views: D4's prefabs (required in the productive scene).
            var mount4 = new GameObject(Phase4MountName);
            var panel4Object = Mount(phase4Panel, mount4, scene);
            var panel4 = panel4Object.GetComponentInChildren<BaseOperationsPanel>(true) ?? throw new InvalidOperationException("BaseOperationsPanel missing");
            var world4Object = Mount(phase4World, mount4, scene);
            var world4 = world4Object.GetComponentInChildren<BaseOperationsWorldView>(true) ?? throw new InvalidOperationException("BaseOperationsWorldView missing");

            Set(bootstrap, "scenario", scenario);
            Set(bootstrap, "runner", runner);
            Set(keyboard, "interactWithE", false);
            Set(interaction, "interactWithE", false); // E is D3's phase 4 key, which falls back to the phase 3 interaction
            Set(world4, "baseInterior", baseView);
            Set(framing, "panel", panel4);
            Set(avatars, "avatarPrefab", astronaut);
            Set(avatars, "avatarParent", crewParent.transform);
            Set(avatars, "followCamera", camera);
            Set(presenter, "avatars", avatars);
            Set(world3, "baseView", baseView);
            Set(composition, "scenario", scenario);
            Set(composition, "bootstrap", bootstrap);
            Set(composition, "networkHost", host);
            Set(composition, "networkClient", client);
            Set(composition, "operationsInput", operationsKey);
            Set(composition, "keyboardInput", keyboard);
            Set(composition, "avatarSync", avatars);
            Set(composition, "interactionInput", interaction);
            Set(composition, "crewPresenter", presenter);
            Set(composition, "baseView", baseView);
            Set(composition, "sessionControls", controls);
            Set(composition, "phase3Mount", mount3.transform);
            Set(composition, "phase4Mount", mount4.transform);
            SetArray(composition, "authorityOnly", authorityObject);
            SetArray(composition, "clientOnly", clientObject);
            authorityObject.SetActive(false);
            clientObject.SetActive(false);

            EnsureFolder("Assets/_Project/Scenes");
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("Could not save " + ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Phase04SceneBuilder] Saved {ScenePath} with scenario {ScenarioPath}: D3 host/client/local port/E key, " +
                "D4 BaseOperationsPanel + BaseOperationsWorld + BaseOperationsCameraFraming, phase 3 ExpeditionWorld, D5 pilot (inert)");
        }

        private static void EnsureScenario(bool reset)
        {
            EnsureFolder(ScenarioFolder);
            var existing = AssetDatabase.LoadAssetAtPath<BaseOperationsScenarioDefinition>(ScenarioPath);
            if (existing != null && !reset) return;
            if (existing != null) AssetDatabase.DeleteAsset(ScenarioPath);
            var phase3 = AssetDatabase.LoadAssetAtPath<ExpeditionScenarioDefinition>(Phase3ScenarioPath)
                ?? throw new InvalidOperationException("Missing " + Phase3ScenarioPath);
            var created = ScriptableObject.CreateInstance<BaseOperationsScenarioDefinition>(); // defaults = synthetic scenario
            created.name = Path.GetFileNameWithoutExtension(ScenarioPath);
            AssetDatabase.CreateAsset(created, ScenarioPath);
            Set(created, "expedition", phase3);
            EditorUtility.SetDirty(created);
            AssetDatabase.SaveAssets();
        }

        private static GameObject Load(string path) =>
            AssetDatabase.LoadAssetAtPath<GameObject>(path) ?? throw new InvalidOperationException("Missing " + path);

        private static GameObject Mount(GameObject prefab, GameObject mount, Scene scene)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.transform.SetParent(mount.transform, false);
            return instance;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static void Set(UnityEngine.Object target, string field, object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(field) ?? throw new InvalidOperationException($"{target.GetType().Name}.{field} not found");
            if (value is bool b) p.boolValue = b;
            else p.objectReferenceValue = (UnityEngine.Object)value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetArray(UnityEngine.Object target, string field, params GameObject[] values)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(field) ?? throw new InvalidOperationException($"{target.GetType().Name}.{field} not found");
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
