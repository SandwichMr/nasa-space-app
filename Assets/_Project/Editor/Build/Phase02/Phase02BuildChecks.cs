using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Trainer.Application.Development;
using Trainer.Application.Outpost;
using Trainer.Tooling.Validation.Phase02;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Phase1 = Trainer.Tooling.BuildChecks;

namespace Trainer.Tooling.BuildChecks.Phase02
{
    /// <summary>
    /// Phase 2 validation and build entry points (D5). Read-only on settings and assets: scenes are opened
    /// additively and never saved, Build Settings and the active target are not modified, nothing is installed.
    /// Evidence kinds stay separate (see <see cref="Phase02CheckKinds"/>); none of them replaces the two-player
    /// run of a build.
    ///
    /// Batch usage (this project closed in other Editors):
    ///   Unity -batchmode -nographics -projectPath &lt;repo&gt; -executeMethod Trainer.Tooling.BuildChecks.Phase02.Phase02BuildChecks.RunChecksFromCommandLine -logFile Logs/phase2-checks.log
    ///   Unity -batchmode -nographics -projectPath &lt;repo&gt; -executeMethod Trainer.Tooling.BuildChecks.Phase02.Phase02BuildChecks.BuildFromCommandLine [-phase2Scenes "Assets/_Project/Scenes/Phase02_X.unity"] -logFile Logs/phase2-build.log
    /// Without -phase2Scenes the single Phase02_* scene under Assets/_Project/Scenes is used; none or several is an error.
    /// </summary>
    public static class Phase02BuildChecks
    {
        public const string SceneArgument = "-phase2Scenes";
        public const string ScenePrefix = "Phase02_";
        public const string ScenesFolder = "Assets/_Project/Scenes";
        public const string ScenariosFolder = "Assets/_Project/Data/Scenarios";
        public const string PhaseOneScene = "Assets/_Project/Scenes/Phase01_PowerCoop.unity";
        public const string BuildRoot = "Builds/Phase2Mission";
        public const string ReportPath = "Logs/phase2-checks.txt";
        public const double ArtTolerance = 0.25;

        [MenuItem("Tools/Trainer/Phase 2/Run validation checks")]
        public static void RunChecksMenu()
        {
            List<Phase02Check> checks = RunAll(ReadSceneArgument());
            UnityEngine.Debug.Log(WriteReport(checks));
        }

        public static void RunChecksFromCommandLine()
        {
            int code = 1;
            try
            {
                List<Phase02Check> checks = RunAll(ReadSceneArgument());
                UnityEngine.Debug.Log(WriteReport(checks));
                code = checks.Any(c => c.Status == Phase02CheckStatus.Failed) ? 1 : 0;
            }
            catch (Exception exception) { UnityEngine.Debug.LogException(exception); }
            EditorApplication.Exit(code);
        }

        [MenuItem("Tools/Trainer/Phase 2/Build mission scene (explicit list, Build Settings untouched)")]
        public static void BuildMenu() => LogBuild(BuildMissionScenes(ReadSceneArgument()));

        public static void BuildFromCommandLine()
        {
            int code = 1;
            try { code = LogBuild(BuildMissionScenes(ReadSceneArgument())) ? 0 : 1; }
            catch (Exception exception) { UnityEngine.Debug.LogException(exception); }
            EditorApplication.Exit(code);
        }

        private static bool LogBuild(BuildReport report)
        {
            UnityEngine.Debug.Log("Phase 2 mission build: " + report.summary.result + ", errors " + report.summary.totalErrors
                + ", output " + report.summary.outputPath + ", commit " + ReadCommit());
            return report.summary.result == BuildResult.Succeeded;
        }

        public static List<Phase02Check> RunAll(string[] explicitScenes)
        {
            var checks = new List<Phase02Check>();
            checks.AddRange(Environment());
            checks.AddRange(ValidateScenarios());
            Phase02ImplementationDiscovery discovery = Phase02ImplementationDiscovery.Discover();
            checks.AddRange(discovery.RunConformance());
            checks.Add(new Phase02Check("two real ports in one Editor process", Phase02CheckKinds.TwoPortSession, Phase02CheckStatus.Pending,
                "Wire TwoPortSessionScript to D3's host-local + client adapters at integration/phase-02."));
            checks.AddRange(AuditScenes(explicitScenes));
            checks.Add(ScanProductionFakeUsage());
            checks.AddRange(PhaseOneRegression());
            checks.Add(new Phase02Check("build played by two people", Phase02CheckKinds.TwoPlayerBuild, Phase02CheckStatus.Pending,
                "Manual acceptance: see docs/phase-02/handoffs/05_VALIDATION_HANDOFF.md (two processes, checklist, logs)."));
            return checks;
        }

        // ---------------------------------------------------------------- environment

        private static IEnumerable<Phase02Check> Environment()
        {
            const string kind = Phase02CheckKinds.Environment;
            string expected = File.Exists("ProjectSettings/ProjectVersion.txt")
                ? File.ReadAllLines("ProjectSettings/ProjectVersion.txt").Select(l => l.Trim())
                    .Where(l => l.StartsWith("m_EditorVersion:", StringComparison.Ordinal)).Select(l => l.Substring(16).Trim()).FirstOrDefault()
                : null;
            yield return new Phase02Check("Editor version matches project", kind,
                expected == UnityEngine.Application.unityVersion ? Phase02CheckStatus.Passed : Phase02CheckStatus.Failed,
                "running " + UnityEngine.Application.unityVersion + ", project " + (expected ?? "unreadable"));
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            bool supported = BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(target), target);
            yield return new Phase02Check("Active build target module installed", kind,
                supported ? Phase02CheckStatus.Passed : Phase02CheckStatus.Failed, target.ToString());
            yield return new Phase02Check("Commit under test", kind, Phase02CheckStatus.Info, ReadCommit());
        }

        // ---------------------------------------------------------------- scenarios

        /// <summary>D5 fixture, common-base fixture and every D2 scenario source found under Data/Scenarios.</summary>
        public static List<Phase02Check> ValidateScenarios()
        {
            var checks = new List<Phase02Check>
            {
                ScenarioCheck("D5 conformance scenario", Phase02Fixtures.Scenario),
                ScenarioCheck("common-base fixture scenario", OutpostFixtures.Scenario)
            };
            List<(string, Func<OutpostScenario>)> sources = FindScenarioSources();
            if (sources.Count == 0)
                checks.Add(new Phase02Check("D2 scenario data", Phase02CheckKinds.Validator, Phase02CheckStatus.Pending,
                    "No asset under " + ScenariosFolder + " exposes a parameterless method returning OutpostScenario."));
            foreach ((string name, Func<OutpostScenario> build) in sources) checks.Add(ScenarioCheck(name, build));
            return checks;
        }

        private static Phase02Check ScenarioCheck(string name, Func<OutpostScenario> build) =>
            Phase02Checks.Check("scenario " + name, Phase02CheckKinds.Validator, () =>
            {
                Phase02Report report = OutpostScenarioValidator.Validate(build());
                Phase02Checks.Require(report.IsValid, report.ToString());
                return report.ToString();
            });

        /// <summary>Assets in Data/Scenarios with a public parameterless instance method returning OutpostScenario
        /// (D2's conversion). Found by shape, so D5 does not depend on D2's type names.</summary>
        public static List<(string, Func<OutpostScenario>)> FindScenarioSources()
        {
            var result = new List<(string, Func<OutpostScenario>)>();
            if (!Directory.Exists(ScenariosFolder)) return result;
            foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject", new[] { ScenariosFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset == null) continue;
                    MethodInfo method = asset.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance)
                        .FirstOrDefault(m => m.ReturnType == typeof(OutpostScenario) && m.GetParameters().Length == 0);
                    if (method == null) continue;
                    UnityEngine.Object captured = asset;
                    result.Add((path + " (" + asset.GetType().Name + "." + method.Name + ")", () =>
                    {
                        try { return (OutpostScenario)method.Invoke(captured, null); }
                        catch (TargetInvocationException e) when (e.InnerException != null) { throw e.InnerException; }
                    }));
                }
            }
            return result;
        }

        // ---------------------------------------------------------------- scenes

        public static string[] ReadSceneArgument()
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == SceneArgument)
                    return args[i + 1].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
            return null;
        }

        /// <summary>Explicit scenes or the single Phase02_* scene. Never templates or the phase 1 scene.</summary>
        public static string[] ResolveScenes(string[] explicitScenes, out string problem)
        {
            problem = null;
            string[] scenes = explicitScenes;
            if (scenes == null)
            {
                scenes = Directory.Exists(ScenesFolder)
                    ? AssetDatabase.FindAssets("t:Scene", new[] { ScenesFolder }).Select(AssetDatabase.GUIDToAssetPath)
                        .Where(p => Path.GetFileName(p).StartsWith(ScenePrefix, StringComparison.Ordinal)).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray()
                    : new string[0];
                if (scenes.Length == 0) { problem = "No " + ScenePrefix + "* scene under " + ScenesFolder + " (scene composition is D2's)."; return null; }
                if (scenes.Length > 1) { problem = "Several phase 2 scenes; pass " + SceneArgument + ": " + string.Join(", ", scenes); return null; }
            }
            if (scenes.Length == 0) { problem = SceneArgument + " is empty."; return null; }
            foreach (string scene in scenes)
            {
                if (Phase1.PhaseOneBuildChecks.TemplateScenes.Any(t => string.Equals(t, scene, StringComparison.OrdinalIgnoreCase)))
                { problem = scene + " is a template scene, not the mission."; return null; }
                if (Path.GetFileName(scene).StartsWith(Phase1.PhaseOneBuildChecks.PhaseOneScenePrefix, StringComparison.Ordinal))
                { problem = scene + " is the phase 1 scene; the phase 2 build needs the phase 2 mission scene."; return null; }
                if (!File.Exists(scene)) { problem = scene + " does not exist."; return null; }
            }
            return scenes;
        }

        public static List<Phase02Check> AuditScenes(string[] explicitScenes)
        {
            var checks = new List<Phase02Check>();
            string[] scenes = ResolveScenes(explicitScenes, out string problem);
            if (scenes == null)
            {
                checks.Add(new Phase02Check("phase 2 scene audit", Phase02CheckKinds.SceneAudit, Phase02CheckStatus.Pending, problem));
                return checks;
            }
            List<(string, Func<OutpostScenario>)> sources = FindScenarioSources();
            OutpostScenario scenario = null;
            if (sources.Count == 1) { try { scenario = sources[0].Item2(); } catch (Exception) { scenario = null; } }
            foreach (string path in scenes)
            {
                try { checks.AddRange(AuditScene(path, scenario)); }
                catch (Exception e)
                {
                    checks.Add(new Phase02Check("scene " + path, Phase02CheckKinds.SceneAudit, Phase02CheckStatus.Failed, e.GetType().Name + ": " + e.Message));
                }
            }
            return checks;
        }

        private static List<Phase02Check> AuditScene(string path, OutpostScenario scenario)
        {
            const string kind = Phase02CheckKinds.SceneAudit;
            var checks = new List<Phase02Check>();
            Scene scene = SceneManager.GetSceneByPath(path);
            bool openedHere = !scene.IsValid() || !scene.isLoaded;
            if (openedHere) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                List<GameObject> objects = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).ToList();
                List<Component> components = objects.SelectMany(o => o.GetComponents<Component>()).ToList();
                string label = Path.GetFileNameWithoutExtension(path) + ": ";

                int missing = components.Count(c => c == null);
                checks.Add(new Phase02Check(label + "no missing scripts", kind, missing == 0 ? Phase02CheckStatus.Passed : Phase02CheckStatus.Failed,
                    missing + " missing component scripts"));

                var fakes = components.Where(c => c != null && !Phase02ImplementationDiscovery.IsProductionCandidate(c.GetType())
                    && (c.GetType().Namespace ?? "").StartsWith("Trainer", StringComparison.Ordinal)).Select(c => c.GetType().FullName).Distinct().ToList();
                checks.Add(new Phase02Check(label + "no fakes/doubles in the scene", kind,
                    fakes.Count == 0 ? Phase02CheckStatus.Passed : Phase02CheckStatus.Failed, string.Join(", ", fakes)));

                var dynamicBodies = components.OfType<Rigidbody2D>().Where(b => b.bodyType == RigidbodyType2D.Dynamic).Select(b => b.name).ToList();
                checks.Add(new Phase02Check(label + "Unity physics does not move crew (no dynamic Rigidbody2D)", kind,
                    dynamicBodies.Count == 0 ? Phase02CheckStatus.Passed : Phase02CheckStatus.Failed,
                    dynamicBodies.Count == 0 ? "collision authority is ICrewRoster" : "dynamic bodies: " + string.Join(", ", dynamicBodies)));
                int solidColliders = components.OfType<Collider2D>().Count(c => !c.isTrigger);
                checks.Add(new Phase02Check(label + "solid Collider2D count", kind, Phase02CheckStatus.Info,
                    solidColliders + " (must not decide positions; the plan is the only geometry)"));

                var cameras = components.OfType<Camera>().Where(c => c.enabled && c.gameObject.activeInHierarchy).ToList();
                checks.Add(new Phase02Check(label + "active camera", kind, cameras.Count >= 1 ? Phase02CheckStatus.Passed : Phase02CheckStatus.Failed,
                    string.Join(", ", cameras.Select(c => c.name + (c.orthographic ? " (orthographic " + c.orthographicSize + ")" : " (perspective)")))));

                var canvases = components.OfType<Canvas>().ToList();
                int eventSystems = components.Count(c => c != null && c.GetType().Name == "EventSystem");
                if (canvases.Count > 0)
                    checks.Add(new Phase02Check(label + "one EventSystem for the UI", kind,
                        eventSystems == 1 ? Phase02CheckStatus.Passed : Phase02CheckStatus.Failed, eventSystems + " EventSystem(s)"));
                checks.Add(UiCaptureRisk(label, components));

                if (scenario == null)
                    checks.Add(new Phase02Check(label + "art matches the plan", kind, Phase02CheckStatus.Pending,
                        "Needs exactly one D2 scenario source to compare positions."));
                else checks.AddRange(ArtMatchesPlan(label, objects, scenario));
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(scene, true);
            }
            return checks;
        }

        /// <summary>Full-screen raycast targets over screen-space canvases can swallow clicks on the world. Keyboard
        /// movement is not affected by raycasts; focus-stealing fields are checked by name of type. Heuristic only:
        /// the real check is the manual two-player run.</summary>
        private static Phase02Check UiCaptureRisk(string label, List<Component> components)
        {
            var risky = new List<string>();
            foreach (UnityEngine.UI.Graphic g in components.OfType<UnityEngine.UI.Graphic>())
            {
                if (!g.raycastTarget || !g.enabled || !g.gameObject.activeInHierarchy) continue;
                Canvas canvas = g.canvas;
                if (canvas == null || canvas.renderMode == RenderMode.WorldSpace) continue;
                RectTransform rect = g.rectTransform;
                bool stretched = rect.anchorMin == Vector2.zero && rect.anchorMax == Vector2.one;
                if (stretched) risky.Add(g.name);
            }
            int inputFields = components.Count(c => c != null && (c.GetType().Name == "InputField" || c.GetType().Name == "TMP_InputField"));
            return new Phase02Check(label + "UI does not cover the world for input", Phase02CheckKinds.SceneAudit,
                risky.Count == 0 ? Phase02CheckStatus.Passed : Phase02CheckStatus.Failed,
                (risky.Count == 0 ? "no full-screen raycast target" : "full-screen raycast targets: " + string.Join(", ", risky))
                + "; text fields: " + inputFields + " (must release focus so WASD/arrows still move)");
        }

        /// <summary>Objects named after a station id must sit on the plan position (world X/Y = plan X/Y, 1 unit = 1 m).</summary>
        private static IEnumerable<Phase02Check> ArtMatchesPlan(string label, List<GameObject> objects, OutpostScenario scenario)
        {
            foreach (StationPlacement station in scenario.Layout.Stations)
            {
                GameObject match = objects.FirstOrDefault(o => o.name.IndexOf(station.Id.Value, StringComparison.OrdinalIgnoreCase) >= 0);
                if (match == null)
                {
                    yield return new Phase02Check(label + "station '" + station.Id + "' drawn on plan", Phase02CheckKinds.SceneAudit,
                        Phase02CheckStatus.Pending, "No scene object named after the station id (D4/D2 naming).");
                    continue;
                }
                Vector3 p = match.transform.position;
                double d = Math.Sqrt(Math.Pow(p.x - station.Position.X, 2) + Math.Pow(p.y - station.Position.Y, 2));
                yield return new Phase02Check(label + "station '" + station.Id + "' drawn on plan", Phase02CheckKinds.SceneAudit,
                    d <= ArtTolerance ? Phase02CheckStatus.Passed : Phase02CheckStatus.Failed,
                    match.name + " at (" + p.x.ToString("0.##") + ", " + p.y.ToString("0.##") + "), plan " + station.Position + ", Δ " + d.ToString("0.###") + " m");
            }
        }

        /// <summary>The development fake must not feed production code (Runtime/Unity).</summary>
        public static Phase02Check ScanProductionFakeUsage()
        {
            const string root = "Assets/_Project/Runtime/Unity";
            var hits = Directory.Exists(root)
                ? Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
                    .Where(f => File.ReadAllText(f).Contains("FakeOutpostSessionPort") || File.ReadAllText(f).Contains("OutpostFixtures"))
                    .Select(f => f.Replace('\\', '/')).ToList()
                : new List<string>();
            return new Phase02Check("no phase 2 fake/fixtures in Runtime/Unity", Phase02CheckKinds.SceneAudit,
                hits.Count == 0 ? Phase02CheckStatus.Passed : Phase02CheckStatus.Failed,
                hits.Count == 0 ? "" : string.Join(", ", hits) + " (allowed on feature branches; must be gone at integration)");
        }

        // ---------------------------------------------------------------- phase 1 regression

        public static List<Phase02Check> PhaseOneRegression()
        {
            var outcomes = new List<Phase1.CheckOutcome>();
            void Safe(string what, Func<IEnumerable<Phase1.CheckOutcome>> run)
            {
                try { outcomes.AddRange(run()); }
                catch (Exception e) { outcomes.Add(new Phase1.CheckOutcome(what, "phase1", Phase1.CheckStatus.Failed, e.GetType().Name + ": " + e.Message)); }
            }
            Safe("validator", Phase1.PhaseOneContractChecks.RunValidatorChecks);
            Safe("fake", () => Phase1.PhaseOneContractChecks.RunFakeSessionChecks(() => new Trainer.Application.Development.FakeSessionPort()));
            Safe("real session", Trainer.Tooling.Validation.PhaseOneIntegrationChecks.RunRealSessionChecks);
            Safe("loopback", Trainer.Tooling.Validation.PhaseOneIntegrationChecks.RunLoopbackChecks);
            Safe("scene", () => Phase1.PhaseOneAssetAudit.AuditMissionScenes(new[] { PhaseOneScene }));
            Safe("prefabs", Phase1.PhaseOneAssetAudit.AuditPrefabs);
            return outcomes.Select(o => new Phase02Check(o.Name, Phase02CheckKinds.PhaseOneRegression,
                o.Status == Phase1.CheckStatus.Passed ? Phase02CheckStatus.Passed
                : o.Status == Phase1.CheckStatus.Failed ? Phase02CheckStatus.Failed
                : o.Status == Phase1.CheckStatus.Pending ? Phase02CheckStatus.Pending : Phase02CheckStatus.Info,
                "(" + o.Kind + ") " + o.Detail)).ToList();
        }

        // ---------------------------------------------------------------- build and two players

        public static BuildReport BuildMissionScenes(string[] explicitScenes)
        {
            string[] scenes = ResolveScenes(explicitScenes, out string problem);
            if (scenes == null) throw new InvalidOperationException("Phase 2 build refused: " + problem);
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            BuildTargetGroup group = BuildPipeline.GetBuildTargetGroup(target);
            if (!BuildPipeline.IsBuildTargetSupported(group, target))
                throw new InvalidOperationException("Active build target " + target + " has no installed module; nothing is installed automatically.");
            string output = PlayerPath(target);
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes, target = target, targetGroup = group, locationPathName = output, options = BuildOptions.None
            });
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(output), "build-info.txt"),
                "commit " + ReadCommit() + "\neditor " + UnityEngine.Application.unityVersion + "\ntarget " + target
                + "\nscenes " + string.Join(";", scenes) + "\nresult " + report.summary.result + "\nutc " + DateTime.UtcNow.ToString("o") + "\n");
            return report;
        }

        public static string PlayerPath(BuildTarget target)
        {
            string name;
            switch (target)
            {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64: name = "Phase2Mission.exe"; break;
                case BuildTarget.StandaloneLinux64: name = "Phase2Mission.x86_64"; break;
                case BuildTarget.StandaloneOSX: name = "Phase2Mission.app"; break;
                default: name = "Phase2Mission"; break;
            }
            return Path.Combine(BuildRoot, target.ToString(), name);
        }

        /// <summary>
        /// Starts the last phase 2 build twice (host and client) with separate log files, then returns. It does not
        /// play, judge or stop them: two people play and fill the checklist. Role arguments default to the phase 1
        /// recipe and can be replaced by D3's phase 2 arguments with -phase2HostArgs / -phase2ClientArgs.
        /// </summary>
        [MenuItem("Tools/Trainer/Phase 2/Launch last build twice (host + client)")]
        public static void LaunchTwoPlayers()
        {
            string player = Path.GetFullPath(PlayerPath(EditorUserBuildSettings.activeBuildTarget));
            if (!File.Exists(player) && !Directory.Exists(player)) throw new FileNotFoundException("Build the phase 2 scene first.", player);
            string hostArgs = Argument("-phase2HostArgs") ?? "-trainer-role authority -trainer-port 7777";
            string clientArgs = Argument("-phase2ClientArgs") ?? "-trainer-role client -trainer-server 127.0.0.1 -trainer-port 7777 -trainer-connect";
            string logs = Path.GetFullPath(Path.Combine("Logs", "phase2-two-players-" + DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ")));
            Directory.CreateDirectory(logs);
            Process host = Process.Start(new ProcessStartInfo(player, hostArgs + " -logFile \"" + Path.Combine(logs, "host.log") + "\"") { UseShellExecute = false });
            Process client = Process.Start(new ProcessStartInfo(player, clientArgs + " -logFile \"" + Path.Combine(logs, "client.log") + "\"") { UseShellExecute = false });
            File.WriteAllText(Path.Combine(logs, "launch.txt"), "commit " + ReadCommit() + "\nplayer " + player + "\nhost " + hostArgs + " (pid " + host?.Id
                + ")\nclient " + clientArgs + " (pid " + client?.Id + ")\n");
            UnityEngine.Debug.Log("Phase 2: host pid " + host?.Id + ", client pid " + client?.Id + "; logs in " + logs);
        }

        private static string Argument(string name)
        {
            string[] args = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1];
            return null;
        }

        // ---------------------------------------------------------------- report

        public static string WriteReport(List<Phase02Check> checks)
        {
            string text = "Phase 2 checks · commit " + ReadCommit() + " · " + DateTime.UtcNow.ToString("o") + "\n"
                + "Kinds are separate evidence; do not add them up. Pending = not verified.\n\n" + Phase02Checks.Format(checks);
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, text);
            return text + "\nReport: " + Path.GetFullPath(ReportPath);
        }

        /// <summary>HEAD commit read from .git without running git (detached or branch).</summary>
        public static string ReadCommit()
        {
            try
            {
                string head = File.ReadAllText(".git/HEAD").Trim();
                if (!head.StartsWith("ref: ", StringComparison.Ordinal)) return head;
                string reference = head.Substring(5);
                string loose = Path.Combine(".git", reference);
                if (File.Exists(loose)) return File.ReadAllText(loose).Trim() + " (" + reference + ")";
                string packed = Path.Combine(".git", "packed-refs");
                if (File.Exists(packed))
                    foreach (string line in File.ReadAllLines(packed))
                        if (line.EndsWith(" " + reference, StringComparison.Ordinal)) return line.Split(' ')[0] + " (" + reference + ")";
                return "unknown (" + reference + ")";
            }
            catch (Exception e) { return "unreadable: " + e.GetType().Name; }
        }
    }
}
