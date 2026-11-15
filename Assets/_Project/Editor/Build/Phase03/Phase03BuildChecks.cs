using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Trainer.Application.Development;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.LifeSupport;
using Trainer.Tooling.Composition.Phase03;
using Trainer.Tooling.Validation.Phase03;
using Trainer.Unity.Bootstrap;
using Trainer.Unity.Bootstrap.Provisional;
using Trainer.Unity.Configuration;
using Trainer.Unity.Controls;
using Trainer.Unity.Networking.Expedition;
using Trainer.Unity.Networking.Outpost;
using Trainer.Unity.Presentation.Expedition;
using Trainer.Unity.Validation.Phase03;
using Trainer.Unity.World.Expedition;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Trainer.Tooling.BuildChecks.Phase03
{
    /// <summary>
    /// D5 phase 3 checks and build. CLI:
    /// Unity -batchmode -nographics -projectPath &lt;repo&gt; -executeMethod Trainer.Tooling.BuildChecks.Phase03.Phase03BuildChecks.RunChecksFromCommandLine -logFile Logs/phase3-checks.log
    /// Unity -batchmode -nographics -projectPath &lt;repo&gt; -buildTarget Linux64 -executeMethod Trainer.Tooling.BuildChecks.Phase03.Phase03BuildChecks.BuildFromCommandLine [-phase3Scenes "Assets/_Project/Scenes/Phase03_EvaAndShelter.unity"] -logFile Logs/phase3-build.log
    /// The build uses an explicit scene list (Build Settings are not modified) and writes Builds/Phase3Mission/&lt;target&gt;/
    /// with build-info.txt. Checks never save scenes or assets.
    /// </summary>
    public static class Phase03BuildChecks
    {
        public const string SceneArgument = "-phase3Scenes";
        public const string BuildRoot = "Builds/Phase3Mission";

        [MenuItem("Tools/Trainer/Phase 3/Run checks")]
        public static void RunChecksMenu() => Debug.Log(Report(RunAll()));

        [MenuItem("Tools/Trainer/Phase 3/Build mission scene")]
        public static void BuildMenu() => Debug.Log(BuildScenes(ReadScenes()).summary.result);

        public static void RunChecksFromCommandLine()
        {
            int code = 1;
            try
            {
                List<Phase03Check> checks = RunAll();
                Debug.Log(Report(checks));
                code = checks.All(c => c.Passed) ? 0 : 1;
            }
            catch (Exception e) { Debug.LogException(e); }
            EditorApplication.Exit(code);
        }

        public static void BuildFromCommandLine()
        {
            int code = 1;
            try { code = BuildScenes(ReadScenes()).summary.result == BuildResult.Succeeded ? 0 : 1; }
            catch (Exception e) { Debug.LogException(e); }
            EditorApplication.Exit(code);
        }

        public static List<Phase03Check> RunAll()
        {
            var checks = new List<Phase03Check>();
            var definition = AssetDatabase.LoadAssetAtPath<ExpeditionScenarioDefinition>(Phase03SceneBuilder.ScenarioPath);
            checks.Add(new Phase03Check("scenario asset " + Phase03SceneBuilder.ScenarioPath, definition != null && definition.Validate() == null,
                definition == null ? "missing" : definition.Validate() ?? definition.DisplayName));
            if (definition != null && definition.Validate() == null)
                checks.AddRange(ExpeditionScenarioValidator.Validate(definition.ToScenario()).Select(c => Prefix("shipped: ", c)));
            checks.AddRange(ExpeditionScenarioValidator.Validate(ExpeditionFixtures.Scenario()).Select(c => Prefix("fixture: ", c)));
            checks.AddRange(AuditScene(Phase03SceneBuilder.ScenePath));
            checks.Add(ScanProductionFakeUsage());
            checks.Add(Phase2DiscoveryUnchanged());
            return checks;
        }

        private static Phase03Check Prefix(string prefix, Phase03Check c) => new Phase03Check(prefix + c.Name, c.Passed, c.Detail);

        /// <summary>Opens the scene additively (closing it afterwards, never saving) and checks the composition wiring.</summary>
        public static List<Phase03Check> AuditScene(string path)
        {
            var checks = new List<Phase03Check>();
            if (!File.Exists(path))
            {
                checks.Add(new Phase03Check("scene " + path, false, "missing"));
                return checks;
            }
            Scene scene = SceneManager.GetSceneByPath(path);
            bool openedHere = !scene.isLoaded;
            if (openedHere) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                GameObject[] roots = scene.GetRootGameObjects();
                T[] All<T>() where T : Component => roots.SelectMany(r => r.GetComponentsInChildren<T>(true)).ToArray();
                var composition = All<ExpeditionSceneComposition>();
                checks.Add(new Phase03Check("scene has exactly one expedition composition", composition.Length == 1, composition.Length + " found"));
                var so = composition.Length == 1 ? new SerializedObject(composition[0]) : null;
                string[] required = { "scenario", "bootstrap", "networkHost", "networkClient", "keyboardInput", "avatarSync",
                    "interactionInput", "crewPresenter", "baseView", "sessionControls", "presentationMount" };
                var missing = so == null ? required.ToList() : required.Where(f => so.FindProperty(f)?.objectReferenceValue == null).ToList();
                checks.Add(new Phase03Check("composition references are wired", missing.Count == 0, missing.Count == 0 ? "all assigned" : "missing: " + string.Join(", ", missing)));
                var bootstrap = All<ExpeditionBootstrap>();
                checks.Add(new Phase03Check("one bootstrap with the scenario and runner", bootstrap.Length == 1
                    && new SerializedObject(bootstrap[0]).FindProperty("scenario").objectReferenceValue != null
                    && new SerializedObject(bootstrap[0]).FindProperty("runner").objectReferenceValue != null, bootstrap.Length + " found"));
                checks.Add(new Phase03Check("one phase 3 host and one phase 3 client (no phase 1/2 network components)",
                    All<ExpeditionNetworkHost>().Length == 1 && All<ExpeditionNetworkClient>().Length == 1
                    && All<OutpostNetworkHost>().Length == 0 && All<OutpostNetworkClient>().Length == 0
                    && All<Trainer.Unity.Networking.SessionNetworkHost>().Length == 0,
                    "a scene speaks one cable"));
                var keyboard = All<CrewKeyboardInput>();
                checks.Add(new Phase03Check("single movement controller (phase 2 keyboard, its E disabled)", keyboard.Length == 1
                    && !new SerializedObject(keyboard[0]).FindProperty("interactWithE").boolValue, keyboard.Length + " keyboard component(s)"));
                var avatars = All<CrewAvatarSync>();
                var avatarPrefab = avatars.Length == 1
                    ? new SerializedObject(avatars[0]).FindProperty("avatarPrefab").objectReferenceValue as GameObject : null;
                checks.Add(new Phase03Check("avatars use the D4 astronaut prefab", avatars.Length == 1
                    && AssetDatabase.GetAssetPath(avatarPrefab) == Phase03SceneBuilder.AstronautPrefab
                    && avatarPrefab.GetComponentsInChildren<ICrewAvatarView>(true).Length > 0
                    && avatarPrefab.GetComponentsInChildren<IExpeditionCrewView>(true).Length > 0,
                    avatars.Length + " avatar sync; phase 2 movement visuals and phase 3 crew view required"));
                Transform mount = so?.FindProperty("presentationMount")?.objectReferenceValue as Transform;
                int views = mount == null ? 0 : mount.GetComponentsInChildren<IExpeditionView>(true).Length;
                checks.Add(new Phase03Check("presentation mount holds IExpeditionView components", views > 0,
                    views + " view(s)"));
                var world = mount == null ? null : mount.GetComponentsInChildren<ExpeditionWorldView>(true).SingleOrDefault();
                var panel = mount == null ? null : mount.GetComponentsInChildren<ExpeditionPanel>(true).SingleOrDefault();
                checks.Add(new Phase03Check("D4 world and HUD prefab instances replace provisional views", views == 2
                    && world != null && panel != null
                    && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(world.gameObject) == Phase03SceneBuilder.WorldPrefab
                    && PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(panel.gameObject) == Phase03SceneBuilder.PanelPrefab
                    && All<ProvisionalExpeditionWorld>().Length == 0 && All<ProvisionalExpeditionHud>().Length == 0,
                    "two production D4 views; no provisional scene components"));
                var worldSo = world == null ? null : new SerializedObject(world);
                string[] artFields = { "regolith", "rock", "route", "hatchOpen", "hatchClosed", "hatchCycle", "shelter", "antenna",
                    "material", "sciencePrefab", "optionalPrefab" };
                checks.Add(new Phase03Check("D4 world uses the composition base and all visual references", worldSo != null
                    && worldSo.FindProperty("baseView").objectReferenceValue == so?.FindProperty("baseView")?.objectReferenceValue
                    && artFields.All(f => worldSo.FindProperty(f).objectReferenceValue != null),
                    "geometry configured once from the same scenario before presentation Bind"));
                int physics = All<Collider2D>().Length + All<Rigidbody2D>().Length + All<Collider>().Length + All<Rigidbody>().Length;
                checks.Add(new Phase03Check("no Unity physics decides positions", physics == 0, physics + " collider/rigidbody components"));
                checks.Add(new Phase03Check("autopilot present and inert without -trainer-autopilot", All<ExpeditionAutopilot>().Length == 1,
                    "D5 acceptance pilot"));
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(scene, true);
            }
            return checks;
        }

        /// <summary>Player sources (Runtime outside Editor folders and the contracts' Development folder) must not use the
        /// phase 3 development fake or fixtures.</summary>
        public static Phase03Check ScanProductionFakeUsage()
        {
            var offenders = new List<string>();
            foreach (string file in Directory.GetFiles("Assets/_Project/Runtime", "*.cs", SearchOption.AllDirectories))
            {
                string path = file.Replace('\\', '/');
                // Labelled development fixture folders of the contracts (phase 3 and the phase 4 fixture built on it) are the
                // fixtures themselves, not production consumers. Every other player source is still scanned.
                if (path.Split('/').Contains("Editor") || IsContractDevelopmentFolder(path)) continue;
                string text = Regex.Replace(File.ReadAllText(path), @"//.*?$|/\*.*?\*/", "", RegexOptions.Multiline | RegexOptions.Singleline);
                if (Regex.IsMatch(text, @"\b(FakeExpeditionSessionPort|ExpeditionFixtures|ExpeditionSnapshotBuilder)\b")) offenders.Add(path);
            }
            return new Phase03Check("no development fake or fixture in player sources", offenders.Count == 0,
                offenders.Count == 0 ? "clean" : string.Join(", ", offenders));
        }

        public static bool IsContractDevelopmentFolder(string path) =>
            path.Contains("/Runtime/Application/Contracts/Expedition/Development/") || path.Contains("/Runtime/Application/Contracts/BaseOperations/Development/");

        /// <summary>Phase 2 tooling discovers implementations by interface: phase 3 must not add new ones.</summary>
        public static Phase03Check Phase2DiscoveryUnchanged()
        {
            Type[] types = typeof(IOutpostAuthority).Assembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract && t.IsVisible).ToArray();
            string[] Impl(Type i) => types.Where(i.IsAssignableFrom).Where(t => !(t.Namespace ?? "").StartsWith("Trainer.Application.Development"))
                .Select(t => t.Name).ToArray();
            string[] authorities = Impl(typeof(IOutpostAuthority)), rosters = Impl(typeof(ICrewRoster)), models = Impl(typeof(IOutpostSimulation));
            bool ok = authorities.SequenceEqual(new[] { "OutpostAuthority" }) && rosters.SequenceEqual(new[] { "CrewRoster" })
                && models.SequenceEqual(new[] { "OutpostBaseSimulation" });
            return new Phase03Check("phase 2 implementation discovery stays unambiguous", ok,
                $"IOutpostAuthority: {string.Join(",", authorities)}; ICrewRoster: {string.Join(",", rosters)}; IOutpostSimulation: {string.Join(",", models)}");
        }

        public static string[] ReadScenes()
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, SceneArgument);
            if (i >= 0 && i + 1 < args.Length) return args[i + 1].Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries);
            return new[] { Phase03SceneBuilder.ScenePath };
        }

        public static BuildReport BuildScenes(string[] scenes)
        {
            foreach (string scene in scenes)
                if (!Path.GetFileName(scene).StartsWith("Phase03_", StringComparison.Ordinal) || !File.Exists(scene))
                    throw new ArgumentException("Only existing Phase03_* scenes are built here: " + scene);
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            if (!BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(target), target))
                throw new InvalidOperationException("Build target not installed: " + target);
            string output = PlayerPath(target);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = target,
                options = BuildOptions.None
            });
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(output), "build-info.txt"),
                $"commit {ReadCommit()}\neditor {UnityEngine.Application.unityVersion}\ntarget {target}\nscenes {string.Join(";", scenes)}\n" +
                $"result {report.summary.result}\nutc {DateTime.UtcNow:O}\n");
            Debug.Log($"[Phase03BuildChecks] Build {report.summary.result}: {output}");
            return report;
        }

        public static string PlayerPath(BuildTarget target)
        {
            string name;
            switch (target)
            {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64: name = "Phase3Mission.exe"; break;
                case BuildTarget.StandaloneLinux64: name = "Phase3Mission.x86_64"; break;
                case BuildTarget.StandaloneOSX: name = "Phase3Mission.app"; break;
                default: name = "Phase3Mission"; break;
            }
            return Path.Combine(BuildRoot, target.ToString(), name);
        }

        public static string Report(List<Phase03Check> checks) =>
            "== Phase 3 checks: " + checks.Count(c => c.Passed) + " passed, " + checks.Count(c => !c.Passed) + " failed\n"
            + string.Join("\n", checks.Select(c => "  " + c));

        private static string ReadCommit()
        {
            try
            {
                // In a linked worktree .git is a file. Ask Git to resolve loose or packed refs in either layout.
                using (var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("git", "rev-parse --verify HEAD")
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true }))
                {
                    if (process == null || !process.WaitForExit(5000)) return "?";
                    return process.ExitCode == 0 ? process.StandardOutput.ReadToEnd().Trim() : "?";
                }
            }
            catch (Exception) { return "?"; }
        }
    }
}
