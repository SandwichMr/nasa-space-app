using System;
using System.IO;
using Trainer.Unity.Bootstrap;
using Trainer.Unity.Bootstrap.BaseOperations;
using Trainer.Unity.Configuration;
using Trainer.Unity.Controls;
using Trainer.Unity.Networking.Expedition;
using Trainer.Unity.Networking.Outpost;
using Trainer.Unity.Presentation.Expedition;
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
    /// from code. It reuses the phase 2 base, movement, avatar and session components and the phase 3 interaction keys,
    /// crew presenter and D4 phase 3 prefabs, and mounts the phase 4 views on the same port: D4's phase 4 panel/world
    /// prefabs when they exist in the branch, otherwise the labelled PROVISIONAL D2 view. Re-run it after D4's prefabs are
    /// merged (integration/phase-04) to swap the provisional view out.
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
        public const string Phase3PanelPrefab = "Assets/_Project/Prefabs/UI/Phase03/ExpeditionPanel.prefab";
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
            var phase3Panel = Load(Phase3PanelPrefab);
            var phase4Panel = AssetDatabase.LoadAssetAtPath<GameObject>(Phase4PanelPrefab); // optional until D4 is merged
            var phase4World = AssetDatabase.LoadAssetAtPath<GameObject>(Phase4WorldPrefab);

            // Camera and light: same configuration as the phase 2 and 3 scenes.
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            cameraObject.transform.position = new Vector3(2.75f, 3.25f, -10f);
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 5;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.03137255f, 0.050980393f, 0.09019608f, 0f);
            camera.GetUniversalAdditionalCameraData();
            cameraObject.AddComponent<LocalCameraFraming>();
            var lightObject = new GameObject("Global Light 2D");
            Light2D light = lightObject.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Global;

            // Composition and the authority-only root (single continuous authority + single runner).
            var compositionObject = new GameObject("BaseOperationsComposition");
            var composition = compositionObject.AddComponent<BaseOperationsSceneComposition>();
            var authorityObject = new GameObject("BaseOperationsAuthority");
            var runner = authorityObject.AddComponent<BaseOperationsRunner>();
            var bootstrap = authorityObject.AddComponent<BaseOperationsBootstrap>();

            // Crew presentation: phase 2 movement (its own E disabled) and avatars, phase 3 keys and crew frames.
            var crewObject = new GameObject("CrewPresentation");
            var keyboard = crewObject.AddComponent<CrewKeyboardInput>();
            var avatars = crewObject.AddComponent<CrewAvatarSync>();
            var interaction = crewObject.AddComponent<ExpeditionInteractionInput>();
            var presenter = crewObject.AddComponent<ExpeditionCrewPresenter>();
            var crewParent = new GameObject("Crew");

            var baseInterior = (GameObject)PrefabUtility.InstantiatePrefab(baseInteriorAsset, scene);
            baseInterior.name = "BaseInterior (D4 phase 2 prefab, phase 4 plan)";
            var baseView = baseInterior.GetComponent<BaseInteriorView>() ?? throw new InvalidOperationException("BaseInteriorView missing");
            var controlsObject = new GameObject("SessionControls");
            var controls = controlsObject.AddComponent<OutpostSessionControls>();

            // Phase 3 views (EVA, hatch, shelter) on the same port.
            var mount3 = new GameObject(Phase3MountName);
            var panel3 = (GameObject)PrefabUtility.InstantiatePrefab(phase3Panel, scene);
            panel3.transform.SetParent(mount3.transform, false);
            if (panel3.GetComponent<ExpeditionPanel>() == null) throw new InvalidOperationException("ExpeditionPanel missing");
            var world3Object = (GameObject)PrefabUtility.InstantiatePrefab(phase3World, scene);
            world3Object.transform.SetParent(mount3.transform, false);
            var world3 = world3Object.GetComponent<ExpeditionWorldView>() ?? throw new InvalidOperationException("ExpeditionWorldView missing");

            // Phase 4 views: D4's prefabs when present, otherwise the labelled provisional view.
            var mount4 = new GameObject(Phase4MountName);
            if (phase4Panel != null) Mount(phase4Panel, mount4, scene);
            if (phase4World != null) Mount(phase4World, mount4, scene);
            if (phase4Panel == null)
            {
                var provisional = new GameObject("ProvisionalBaseOperationsHud (D2, PROVISIONAL)");
                provisional.transform.SetParent(mount4.transform, false);
                provisional.AddComponent<ProvisionalBaseOperationsHud>();
            }

            Set(bootstrap, "scenario", scenario);
            Set(bootstrap, "runner", runner);
            Set(keyboard, "interactWithE", false);
            Set(avatars, "avatarPrefab", astronaut);
            Set(avatars, "avatarParent", crewParent.transform);
            Set(avatars, "followCamera", camera);
            Set(presenter, "avatars", avatars);
            Set(world3, "baseView", baseView);
            Set(composition, "scenario", scenario);
            Set(composition, "bootstrap", bootstrap);
            Set(composition, "keyboardInput", keyboard);
            Set(composition, "avatarSync", avatars);
            Set(composition, "interactionInput", interaction);
            Set(composition, "crewPresenter", presenter);
            Set(composition, "baseView", baseView);
            Set(composition, "sessionControls", controls);
            Set(composition, "phase3Mount", mount3.transform);
            Set(composition, "phase4Mount", mount4.transform);
            SetArray(composition, "authorityOnly", authorityObject);
            authorityObject.SetActive(false);

            EnsureFolder("Assets/_Project/Scenes");
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("Could not save " + ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Phase04SceneBuilder] Saved {ScenePath} with scenario {ScenarioPath}; phase 4 views: " +
                (phase4Panel != null ? "D4 prefabs" : "PROVISIONAL D2 view"));
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

        private static void Mount(GameObject prefab, GameObject mount, Scene scene)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.transform.SetParent(mount.transform, false);
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
