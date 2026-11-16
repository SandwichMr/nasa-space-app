using System;
using System.IO;
using Trainer.Unity.Bootstrap.BaseOperations;
using Trainer.Unity.Bootstrap.Incidents;
using Trainer.Unity.Configuration;
using Trainer.Unity.Controls;
using Trainer.Unity.Networking.BaseOperations;
using Trainer.Unity.Networking.Incidents;
using Trainer.Unity.Presentation.BaseOperations;
using Trainer.Unity.Presentation.Incidents;
using Trainer.Unity.Validation.Phase04;
using Trainer.Unity.Validation.Phase05;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Trainer.Tooling.Composition.Phase05
{
    /// <summary>
    /// Builds <c>Phase05_LivingBase.unity</c> (D2). To PRESERVE the current map and art, it copies the productive phase 4 scene and only
    /// replaces its composition, bootstrap, runner, network host/client and pilot with the phase 5 ones, copying every serialized
    /// reference they shared (camera, crew inputs, avatars, base view, lunar artwork and skin, session controls, mounts, host and
    /// client options). It then adds the phase 5 E key (phase 4 E disabled, used as fallback), the phase 5 presentation mount with
    /// <see cref="LivingBasePanel"/> (phase 4 dashboard skin) and the phase 5 pilot (inert without its argument). The phase 4 scene and
    /// assets are never modified. Scenario asset: <see cref="ScenarioPath"/> (references the phase 4 scenario asset).
    /// Command line: <c>-executeMethod Trainer.Tooling.Composition.Phase05.Phase05SceneBuilder.BuildFromCommandLine</c>.
    /// </summary>
    public static class Phase05SceneBuilder
    {
        public const string ScenePath = "Assets/_Project/Scenes/Phase05_LivingBase.unity";
        public const string Phase4ScenePath = "Assets/_Project/Scenes/Phase04_LivingBase.unity";
        public const string ScenarioFolder = "Assets/_Project/Data/Scenarios/Phase05";
        public const string ScenarioPath = ScenarioFolder + "/Phase05_LivingBase_Synthetic.asset";
        public const string Phase4ScenarioPath = "Assets/_Project/Data/Scenarios/Phase04/Phase04_LivingBase_Synthetic.asset";
        public const string Phase5MountName = "Phase05 Presentation Mount";

        private static readonly string[] SharedCompositionFields =
        {
            "role", "readCommandLine", "keyboardInput", "avatarSync", "interactionInput", "operationsInput", "crewPresenter", "baseView",
            "lunarInteriorArtwork", "lunarInteriorSkin", "sessionControls", "phase3Mount", "phase4Mount", "authorityPlays"
        };

        [MenuItem("Tools/Trainer/Phase 5/Build scenario asset and scene")]
        public static void BuildMenu() => Build(false);

        public static void BuildFromCommandLine()
        {
            try
            {
                Build(Array.IndexOf(Environment.GetCommandLineArgs(), "-phase5ResetScenario") >= 0);
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
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Phase4ScenePath) == null) throw new InvalidOperationException("Missing " + Phase4ScenePath);
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null && !AssetDatabase.DeleteAsset(ScenePath))
                throw new InvalidOperationException("Could not replace " + ScenePath);
            if (!AssetDatabase.CopyAsset(Phase4ScenePath, ScenePath)) throw new InvalidOperationException("Could not copy the phase 4 scene.");
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var scenario = AssetDatabase.LoadAssetAtPath<LivingBaseScenarioDefinition>(ScenarioPath) ?? throw new InvalidOperationException("Missing " + ScenarioPath);
            string problem = scenario.Validate();
            if (problem != null) throw new InvalidOperationException("Phase 5 scenario invalid: " + problem);

            var c4 = One<BaseOperationsSceneComposition>();
            var bootstrap4 = One<BaseOperationsBootstrap>();
            var host4 = One<BaseOperationsNetworkHost>();
            var client4 = One<BaseOperationsNetworkClient>();
            var operationsKey = One<BaseOperationsInteractionInput>();
            var panel4 = One<BaseOperationsPanel>();
            GameObject compositionObject = c4.gameObject, authorityObject = bootstrap4.gameObject, clientObject = client4.gameObject;
            compositionObject.name = "LivingBaseComposition";
            authorityObject.name = "LivingBaseAuthority (authority only)";
            clientObject.name = "LivingBaseClient (client only)";

            // Composition: phase 5 component with every shared reference copied from the phase 4 one.
            var composition = compositionObject.AddComponent<LivingBaseSceneComposition>();
            Copy(c4, composition, SharedCompositionFields);
            // Authority-only object: one runner, one bootstrap, one host (host options copied).
            var runner = authorityObject.AddComponent<LivingBaseRunner>();
            var bootstrap = authorityObject.AddComponent<LivingBaseBootstrap>();
            var host = authorityObject.AddComponent<LivingBaseNetworkHost>();
            Copy(host4, host, "options", "listenOnStart", "readCommandLine", "disconnectOnProtocolError", "batchModeFrameRate", "runInBackground");
            var client = clientObject.AddComponent<LivingBaseNetworkClient>();
            Copy(client4, client, "options", "connectOnStart", "readLaunchArguments", "synchronizationTimeoutSeconds", "runInBackground");

            foreach (BaseOperationsAutopilot pilot in All<BaseOperationsAutopilot>()) Object.DestroyImmediate(pilot);
            Object.DestroyImmediate(c4);
            Object.DestroyImmediate(host4);
            Object.DestroyImmediate(bootstrap4);
            foreach (BaseOperationsRunner r in All<BaseOperationsRunner>()) Object.DestroyImmediate(r);
            Object.DestroyImmediate(client4);
            compositionObject.AddComponent<LivingBaseAutopilot>();

            // Phase 5 E key next to the phase 4 one (whose own E is disabled: it is the fallback).
            var livingKey = operationsKey.gameObject.AddComponent<LivingBaseInteractionInput>();
            Set(livingKey, "phase4", operationsKey);
            Set(operationsKey, "interactWithE", false);

            // Phase 5 presentation mount (phase 4 dashboard and its skin are reused, not modified).
            var mount5 = new GameObject(Phase5MountName);
            SceneManager.MoveGameObjectToScene(mount5, scene);
            var panel5 = mount5.AddComponent<LivingBasePanel>();
            Set(panel5, "phase4", panel4);
            Copy(panel4, panel5, "skin");

            Set(bootstrap, "scenario", scenario);
            Set(bootstrap, "runner", runner);
            Set(composition, "scenario", scenario);
            Set(composition, "bootstrap", bootstrap);
            Set(composition, "networkHost", host);
            Set(composition, "networkClient", client);
            Set(composition, "livingInput", livingKey);
            Set(composition, "phase5Mount", mount5.transform);
            SetArray(composition, "authorityOnly", authorityObject);
            SetArray(composition, "clientOnly", clientObject);
            authorityObject.SetActive(false);
            clientObject.SetActive(false);

            if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("Could not save " + ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Phase05SceneBuilder] Saved {ScenePath} from {Phase4ScenePath} (map and art preserved) with scenario {ScenarioPath}: " +
                      "LivingBase composition/bootstrap/runner/host/client, phase 5 E key, LivingBasePanel, phase 5 pilot (inert).");
        }

        private static void EnsureScenario(bool reset)
        {
            EnsureFolder(ScenarioFolder);
            var existing = AssetDatabase.LoadAssetAtPath<LivingBaseScenarioDefinition>(ScenarioPath);
            if (existing != null && !reset) return;
            if (existing != null) AssetDatabase.DeleteAsset(ScenarioPath);
            var phase4 = AssetDatabase.LoadAssetAtPath<BaseOperationsScenarioDefinition>(Phase4ScenarioPath)
                ?? throw new InvalidOperationException("Missing " + Phase4ScenarioPath);
            var created = ScriptableObject.CreateInstance<LivingBaseScenarioDefinition>(); // defaults = synthetic phase 5 scenario
            created.name = Path.GetFileNameWithoutExtension(ScenarioPath);
            AssetDatabase.CreateAsset(created, ScenarioPath);
            Set(created, "baseOperations", phase4);
            EditorUtility.SetDirty(created);
            AssetDatabase.SaveAssets();
        }

        private static T One<T>() where T : Object
        {
            T[] found = All<T>();
            if (found.Length != 1) throw new InvalidOperationException($"The phase 4 scene must contain exactly one {typeof(T).Name} (found {found.Length}).");
            return found[0];
        }

        private static T[] All<T>() where T : Object => Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        /// <summary>Copies same-named serialized fields (values and references) from one component to another.</summary>
        private static void Copy(Object from, Object to, params string[] fields)
        {
            var source = new SerializedObject(from);
            var target = new SerializedObject(to);
            foreach (string field in fields)
            {
                SerializedProperty p = source.FindProperty(field) ?? throw new InvalidOperationException($"{from.GetType().Name}.{field} not found");
                if (target.FindProperty(field) == null) throw new InvalidOperationException($"{to.GetType().Name}.{field} not found");
                target.CopyFromSerializedProperty(p);
            }
            target.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static void Set(Object target, string field, object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(field) ?? throw new InvalidOperationException($"{target.GetType().Name}.{field} not found");
            if (value is bool b) p.boolValue = b;
            else p.objectReferenceValue = (Object)value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetArray(Object target, string field, params GameObject[] values)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(field) ?? throw new InvalidOperationException($"{target.GetType().Name}.{field} not found");
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
