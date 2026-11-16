using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Trainer.Application;
using Trainer.Application.Incidents;
using Trainer.Unity.Bootstrap.BaseOperations;
using Trainer.Unity.Bootstrap.Incidents;
using Trainer.Unity.Configuration;
using Trainer.Unity.Controls;
using Trainer.Unity.Integration.SpaceWeather;
using Trainer.Unity.Networking.BaseOperations;
using Trainer.Unity.Networking.Incidents;
using Trainer.Unity.Presentation.Incidents;
using Trainer.Unity.Validation.Phase05;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Trainer.Tooling.Build.Phase05
{
    /// <summary>
    /// D5 phase 5 validators and Linux build (Editor only). Checks: scenario asset valid; frozen DONKI dataset loads offline with the
    /// manifest hash; a missing dataset yields the labelled synthetic fallback; the scene has exactly one phase 5 composition,
    /// bootstrap, runner, host and client (authority/client only) and none of the phase 4 ones, the phase 5 E key with the phase 4
    /// E disabled, and the phase 5 panel under its mount. The build uses an explicit scene list (Build Settings untouched) and writes
    /// <see cref="BuildRoot"/>/Linux/Phase5LivingBase.x86_64. Results are written to Logs/phase05/d5/.
    /// <para>Command line (Editor closed on this project):
    /// <c>-batchmode -quit -projectPath . -executeMethod Trainer.Tooling.Build.Phase05.Phase05BuildTools.RunChecksFromCommandLine</c>
    /// and <c>...Phase05BuildTools.BuildLinuxFromCommandLine</c>.</para>
    /// </summary>
    public static class Phase05BuildTools
    {
        public const string BuildRoot = "Builds/Phase5LivingBase";
        public const string LogFolder = "Logs/phase05/d5";

        [MenuItem("Tools/Trainer/Phase 5/Run checks")]
        public static void RunChecksMenu() => Debug.Log(Format(RunChecks()));

        [MenuItem("Tools/Trainer/Phase 5/Build Linux player (gated)")]
        public static void BuildMenu() => Debug.Log(BuildLinux());

        public static void RunChecksFromCommandLine()
        {
            List<(string name, bool ok, string detail)> results;
            try { results = RunChecks(); }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
                return;
            }
            string text = Format(results);
            Write("checks.txt", text);
            Debug.Log(text);
            EditorApplication.Exit(results.All(r => r.ok) ? 0 : 1);
        }

        public static void BuildLinuxFromCommandLine()
        {
            string text;
            bool ok;
            try
            {
                text = BuildLinux();
                ok = text.StartsWith("BUILD OK", StringComparison.Ordinal);
            }
            catch (Exception e)
            {
                text = "BUILD FAILED: " + e;
                ok = false;
            }
            Write("build.txt", text);
            Debug.Log(text);
            EditorApplication.Exit(ok ? 0 : 1);
        }

        public static string BuildLinux()
        {
            List<(string name, bool ok, string detail)> checks = RunChecks();
            if (!checks.All(c => c.ok)) return "BUILD NOT STARTED: checks failed\n" + Format(checks);
            const BuildTarget target = BuildTarget.StandaloneLinux64;
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, target)) return "BUILD NOT STARTED: Linux build support module not installed.";
            string output = Path.Combine(BuildRoot, "Linux", "Phase5LivingBase.x86_64");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { Trainer.Tooling.Composition.Phase05.Phase05SceneBuilder.ScenePath },
                locationPathName = output,
                target = target,
                options = BuildOptions.None
            });
            BuildSummary s = report.summary;
            string dataset = Path.Combine(BuildRoot, "Linux", "Phase5LivingBase_Data", "StreamingAssets", "ScientificData", "Phase05", "donki-2017-09-10",
                HistoricalDatasetSource.EpisodeFile);
            return (s.result == BuildResult.Succeeded ? "BUILD OK" : "BUILD " + s.result) + $" {output} size={s.totalSize} errors={s.totalErrors} " +
                   $"warnings={s.totalWarnings} time={s.totalTime} datasetPackaged={File.Exists(dataset)}\n" + Format(checks);
        }

        public static List<(string name, bool ok, string detail)> RunChecks()
        {
            var results = new List<(string, bool, string)>();
            var scenario = AssetDatabase.LoadAssetAtPath<LivingBaseScenarioDefinition>(Trainer.Tooling.Composition.Phase05.Phase05SceneBuilder.ScenarioPath);
            string problem = scenario == null ? "missing asset" : scenario.Validate();
            results.Add(("scenario asset valid", problem == null, problem ?? scenario.name));
            if (scenario == null) return results;

            bool loaded = HistoricalDatasetSource.TryLoad(UnityEngine.Application.streamingAssetsPath, scenario.HistoricalDatasetFolder,
                out SpaceWeatherEpisodeDefinition episode, out string key);
            results.Add(("frozen DONKI dataset loads offline (hash verified)", loaded,
                loaded ? episode.Dataset.DatasetId + " sha256=" + episode.Dataset.Sha256 + " records=" + episode.Records.Count : key));
            if (loaded)
            {
                LivingBaseAuthority h = LivingBaseBootstrap.Create(scenario, new LivingBaseSetup(Trainer.Application.Incidents.DataMode.Historical, 1), scenario.HistoricalDatasetFolder,
                    UnityEngine.Application.streamingAssetsPath);
                h.TryGetSnapshot(out LivingBaseSnapshot hs);
                results.Add(("historical session reports Loaded", hs.Data.Availability == DataAvailability.Loaded, LivingBaseBootstrap.DescribeData(h)));
            }
            LivingBaseAuthority f = LivingBaseBootstrap.Create(scenario, new LivingBaseSetup(Trainer.Application.Incidents.DataMode.Historical, 1), "does-not-exist",
                UnityEngine.Application.streamingAssetsPath);
            f.TryGetSnapshot(out LivingBaseSnapshot fs);
            results.Add(("missing dataset → labelled synthetic fallback",
                fs.Data.Availability == DataAvailability.FallbackSynthetic && fs.Data.ProblemKey == HistoricalEpisodeLoader.Missing, LivingBaseBootstrap.DescribeData(f)));

            string scenePath = Trainer.Tooling.Composition.Phase05.Phase05SceneBuilder.ScenePath;
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) == null)
            {
                results.Add(("scene exists", false, scenePath + " not built (run Tools/Trainer/Phase 5/Build scenario asset and scene)"));
                return results;
            }
            var previous = EditorSceneManager.GetActiveScene().path;
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            Count<LivingBaseSceneComposition>(results, 1);
            Count<LivingBaseBootstrap>(results, 1);
            Count<LivingBaseRunner>(results, 1);
            Count<LivingBaseNetworkHost>(results, 1);
            Count<LivingBaseNetworkClient>(results, 1);
            Count<LivingBaseInteractionInput>(results, 1);
            Count<LivingBasePanel>(results, 1);
            Count<LivingBaseAutopilot>(results, 1);
            Count<BaseOperationsSceneComposition>(results, 0);
            Count<BaseOperationsBootstrap>(results, 0);
            Count<BaseOperationsRunner>(results, 0);
            Count<BaseOperationsNetworkHost>(results, 0);
            Count<BaseOperationsNetworkClient>(results, 0);
            var key4 = All<BaseOperationsInteractionInput>().FirstOrDefault();
            bool e4 = key4 != null && new SerializedObject(key4).FindProperty("interactWithE").boolValue;
            results.Add(("phase 4 E disabled (phase 5 key handles E once)", key4 != null && !e4, key4 == null ? "no phase 4 key" : "interactWithE=" + e4));
            var bootstrap = All<LivingBaseBootstrap>().FirstOrDefault();
            var composition = All<LivingBaseSceneComposition>().FirstOrDefault();
            if (bootstrap != null && composition != null)
            {
                var so = new SerializedObject(composition);
                SerializedProperty authorityOnly = so.FindProperty("authorityOnly");
                bool listed = false;
                for (int i = 0; i < authorityOnly.arraySize; i++)
                    listed |= authorityOnly.GetArrayElementAtIndex(i).objectReferenceValue == bootstrap.gameObject;
                results.Add(("bootstrap/runner/host only in the authority process", listed && !bootstrap.gameObject.activeSelf,
                    "listed=" + listed + " activeInScene=" + bootstrap.gameObject.activeSelf));
                results.Add(("composition uses the phase 5 scenario asset", so.FindProperty("scenario").objectReferenceValue == scenario, ""));
            }
            if (!string.IsNullOrEmpty(previous) && previous != scenePath) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
            return results;
        }

        private static T[] All<T>() where T : Object => Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        private static void Count<T>(List<(string, bool, string)> results, int expected) where T : Object
        {
            int found = All<T>().Length;
            results.Add(($"scene has {expected} {typeof(T).Name}", found == expected, "found " + found));
        }

        public static string Format(List<(string name, bool ok, string detail)> results)
        {
            var b = new StringBuilder($"Phase 5 checks ({DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}): {results.Count(r => r.ok)}/{results.Count} passed\n");
            foreach (var r in results) b.Append(r.ok ? "  PASS " : "  FAIL ").Append(r.name).Append(" — ").Append(r.detail).Append('\n');
            return b.ToString();
        }

        private static void Write(string file, string text)
        {
            Directory.CreateDirectory(LogFolder);
            File.WriteAllText(Path.Combine(LogFolder, file), text);
        }
    }
}
