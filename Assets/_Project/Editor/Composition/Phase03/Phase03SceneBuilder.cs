using System;
using System.IO;
using Trainer.Unity.Bootstrap;
using Trainer.Unity.Bootstrap.Provisional;
using Trainer.Unity.Configuration;
using Trainer.Unity.Controls;
using Trainer.Unity.Networking.Expedition;
using Trainer.Unity.Networking.Outpost;
using Trainer.Unity.Validation.Phase03;
using Trainer.Unity.World.Outpost;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Trainer.Tooling.Composition.Phase03
{
    /// <summary>
    /// D2 (single scene editor): builds the phase 3 scenario asset and the scene <c>Phase03_EvaAndShelter</c>
    /// reproducibly from code, so the composition is reviewable and can be regenerated after a contract or prefab change.
    /// It reuses the phase 2 base and astronaut prefabs (D4) and the phase 2 movement, avatar and session components;
    /// the phase 3 views under the presentation mount are the labelled PROVISIONAL ones until D4's arrive.
    /// Menu: Tools/Trainer/Phase 3/Build scenario asset and scene. CLI:
    /// Unity -batchmode -nographics -projectPath &lt;repo&gt; -executeMethod Trainer.Tooling.Composition.Phase03.Phase03SceneBuilder.BuildFromCommandLine
    /// [-phase3ResetScenario] -logFile Logs/phase3-scene.log
    /// </summary>
    public static class Phase03SceneBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/Phase03_EvaAndShelter.unity";
        public const string ScenarioFolder = "Assets/_Project/Data/Scenarios/Phase03";
        public const string ScenarioPath = ScenarioFolder + "/Phase03_EvaAndShelter_Synthetic.asset";
        public const string BaseInteriorPrefab = "Assets/_Project/Prefabs/World/Phase02/BaseInterior.prefab";
        public const string AstronautPrefab = "Assets/_Project/Prefabs/Crew/Phase02/AstronautVisual.prefab";
        public const string PresentationMountName = "Phase03 Presentation Mount";

        [MenuItem("Tools/Trainer/Phase 3/Build scenario asset and scene")]
        public static void BuildMenu() => Build(false);

        public static void BuildFromCommandLine()
        {
            try
            {
                Build(Array.IndexOf(Environment.GetCommandLineArgs(), "-phase3ResetScenario") >= 0);
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
            var scenario = AssetDatabase.LoadAssetAtPath<ExpeditionScenarioDefinition>(ScenarioPath)
                ?? throw new InvalidOperationException("Missing " + ScenarioPath);
            string problem = scenario.Validate();
            if (problem != null) throw new InvalidOperationException("Scenario asset invalid: " + problem);
            var baseInteriorAsset = AssetDatabase.LoadAssetAtPath<GameObject>(BaseInteriorPrefab)
                ?? throw new InvalidOperationException("Missing " + BaseInteriorPrefab);
            var astronaut = AssetDatabase.LoadAssetAtPath<GameObject>(AstronautPrefab)
                ?? throw new InvalidOperationException("Missing " + AstronautPrefab);

            // Camera and light: same configuration as the phase 2 scene.
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

            // Composition, authority-only and client-only roots.
            var compositionObject = new GameObject("ExpeditionComposition");
            var composition = compositionObject.AddComponent<ExpeditionSceneComposition>();
            var autopilot = compositionObject.AddComponent<ExpeditionAutopilot>();
            var authorityObject = new GameObject("ExpeditionAuthority");
            var runner = authorityObject.AddComponent<ExpeditionRunner>();
            var bootstrap = authorityObject.AddComponent<ExpeditionBootstrap>();
            var host = authorityObject.AddComponent<ExpeditionNetworkHost>();
            var clientObject = new GameObject("ExpeditionClient");
            var client = clientObject.AddComponent<ExpeditionNetworkClient>();

            // Crew presentation: phase 2 movement (its own E disabled) and avatars, phase 3 keys and crew frames.
            var crewObject = new GameObject("CrewPresentation");
            var keyboard = crewObject.AddComponent<CrewKeyboardInput>();
            var avatars = crewObject.AddComponent<CrewAvatarSync>();
            var interaction = crewObject.AddComponent<ExpeditionInteractionInput>();
            var presenter = crewObject.AddComponent<ExpeditionCrewPresenter>();
            var crewParent = new GameObject("Crew");

            // Base and plan view (D4 phase 2 prefab, configured at runtime from the phase 3 plan).
            var baseInterior = (GameObject)PrefabUtility.InstantiatePrefab(baseInteriorAsset, scene);
            baseInterior.name = "BaseInterior (D4 phase 2 prefab, phase 3 plan)";
            var baseView = baseInterior.GetComponent<BaseInteriorView>() ?? throw new InvalidOperationException("BaseInteriorView missing");
            var controlsObject = new GameObject("SessionControls");
            var controls = controlsObject.AddComponent<OutpostSessionControls>();

            // D4 mount: provisional views until D4's IExpeditionView components replace them.
            var mount = new GameObject(PresentationMountName);
            var hudObject = new GameObject("PROVISIONAL HUD (D2)");
            hudObject.transform.SetParent(mount.transform, false);
            var hud = hudObject.AddComponent<ProvisionalExpeditionHud>();
            var worldObject = new GameObject("PROVISIONAL World overlays (D2)");
            worldObject.transform.SetParent(mount.transform, false);
            var world = worldObject.AddComponent<ProvisionalExpeditionWorld>();

            Set(bootstrap, "scenario", scenario);
            Set(bootstrap, "runner", runner);
            Set(keyboard, "interactWithE", false);
            Set(avatars, "avatarPrefab", astronaut);
            Set(avatars, "avatarParent", crewParent.transform);
            Set(avatars, "followCamera", camera);
            Set(presenter, "avatars", avatars);
            Set(hud, "baseView", baseView);
            Set(world, "baseView", baseView);
            Set(world, "avatars", avatars);
            Set(autopilot, "composition", composition);
            Set(autopilot, "keyboard", keyboard);
            Set(autopilot, "interaction", interaction);
            Set(composition, "scenario", scenario);
            Set(composition, "bootstrap", bootstrap);
            Set(composition, "networkHost", host);
            Set(composition, "networkClient", client);
            Set(composition, "keyboardInput", keyboard);
            Set(composition, "avatarSync", avatars);
            Set(composition, "interactionInput", interaction);
            Set(composition, "crewPresenter", presenter);
            Set(composition, "baseView", baseView);
            Set(composition, "sessionControls", controls);
            Set(composition, "presentationMount", mount.transform);
            SetArray(composition, "authorityOnly", authorityObject);
            SetArray(composition, "clientOnly", clientObject);
            authorityObject.SetActive(false);
            clientObject.SetActive(false);

            EnsureFolder("Assets/_Project/Scenes");
            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("Could not save " + ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Phase03SceneBuilder] Saved {ScenePath} with scenario {ScenarioPath}.");
        }

        private static ExpeditionScenarioDefinition EnsureScenario(bool reset)
        {
            EnsureFolder(ScenarioFolder);
            var existing = AssetDatabase.LoadAssetAtPath<ExpeditionScenarioDefinition>(ScenarioPath);
            if (existing != null && !reset) return existing;
            if (existing != null) AssetDatabase.DeleteAsset(ScenarioPath);
            var created = ScriptableObject.CreateInstance<ExpeditionScenarioDefinition>(); // defaults = shipped synthetic scenario
            created.name = Path.GetFileNameWithoutExtension(ScenarioPath);
            AssetDatabase.CreateAsset(created, ScenarioPath);
            AssetDatabase.SaveAssets();
            return created;
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
