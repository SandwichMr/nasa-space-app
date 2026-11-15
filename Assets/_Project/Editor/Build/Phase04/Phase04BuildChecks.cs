using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Trainer.Application.BaseOperations;
using Trainer.Tooling.Validation.Phase04;
using Trainer.Unity.Bootstrap;
using Trainer.Unity.Controls;
using Trainer.Unity.Networking.Expedition;
using Trainer.Unity.Networking.Outpost;
using Trainer.Unity.Validation.Phase04;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace Trainer.Tooling.BuildChecks.Phase04
{
    /// <summary>
    /// D5 phase 4 gates, build and build run. Gate order (a failed gate is recorded and the later ones are not certified):
    /// Unity import/compile → EditMode (Test Runner) → <see cref="RunChecksFromCommandLine"/> (validators) → PlayMode (Test
    /// Runner) → <see cref="BuildFromCommandLine"/> (target and scene) → <see cref="RunBuildFromCommandLine"/> (authority + two
    /// clients of that build). CLI (from the repository root):
    /// <code>
    /// Unity -batchmode -nographics -projectPath . -executeMethod Trainer.Tooling.BuildChecks.Phase04.Phase04BuildChecks.RunChecksFromCommandLine [-phase4Quick] -logFile Logs/phase04/d5-checks.log
    /// Unity -batchmode -nographics -projectPath . -buildTarget Win64|Linux64 -executeMethod Trainer.Tooling.BuildChecks.Phase04.Phase04BuildChecks.BuildFromCommandLine [-phase4Scenes "Assets/_Project/Scenes/Phase04_LivingBase.unity"] -logFile Logs/phase04/d5-build.log
    /// Unity -batchmode -nographics -projectPath . -buildTarget Win64|Linux64 -executeMethod Trainer.Tooling.BuildChecks.Phase04.Phase04BuildChecks.RunBuildFromCommandLine [-phase4Minutes 30] [-phase4Port 7795] -logFile Logs/phase04/d5-run.log
    /// </code>
    /// Exit codes: 0 passed, 1 failed, 2 nothing failed but something is still Pending. Checks never save scenes or assets;
    /// the build uses an explicit scene list (Build Settings are not modified) and writes Builds/Phase4LivingBase/&lt;target&gt;/
    /// with build-info.txt. Reports go to Logs/phase04/ (ignored by git).
    /// </summary>
    public static class Phase04BuildChecks
    {
        public const string ScenePath = "Assets/_Project/Scenes/Phase04_LivingBase.unity";
        public const string ScenarioAssetPath = "Assets/_Project/Data/Scenarios/Phase04/Phase04_LivingBase_Synthetic.asset";
        public const string MountName = "Phase04 Presentation Mount";
        public const string BuildRoot = "Builds/Phase4LivingBase";
        public const string LogRoot = "Logs/phase04";
        public const string SceneArgument = "-phase4Scenes";

        [MenuItem("Tools/Trainer/Phase 4/Run D5 checks (quick)")]
        public static void RunQuickMenu() => Debug.Log(Phase04Checks.Format(RunAll(false)));

        [MenuItem("Tools/Trainer/Phase 4/Run D5 checks (full, long)")]
        public static void RunFullMenu() => Debug.Log(Phase04Checks.Format(RunAll(true)));

        [MenuItem("Tools/Trainer/Phase 4/Build living base scene")]
        public static void BuildMenu() => Debug.Log(BuildGated(ReadScenes(), false));

        public static void RunChecksFromCommandLine()
        {
            int code = Phase04Checks.ExitFailed;
            try
            {
                List<Phase04Check> checks = RunAll(!HasArgument("-phase4Quick"));
                string report = Phase04Checks.Format(checks);
                Debug.Log(report);
                Write("d5-checks.txt", report);
                code = Phase04Checks.ExitCode(checks);
            }
            catch (Exception e) { Debug.LogException(e); }
            EditorApplication.Exit(code);
        }

        public static void BuildFromCommandLine()
        {
            int code = Phase04Checks.ExitFailed;
            try
            {
                string result = BuildGated(ReadScenes(), HasArgument("-phase4BuildAnyway"));
                Debug.Log(result);
                code = result.StartsWith("DiagnosticSucceeded", StringComparison.Ordinal) ? Phase04Checks.ExitPending
                    : result.StartsWith("Succeeded", StringComparison.Ordinal) ? Phase04Checks.ExitPassed : Phase04Checks.ExitFailed;
            }
            catch (Exception e) { Debug.LogException(e); }
            EditorApplication.Exit(code);
        }

        public static void RunBuildFromCommandLine()
        {
            int code = Phase04Checks.ExitFailed;
            try
            {
                double minutes = NumberArgument("-phase4Minutes", 30);
                int port = (int)NumberArgument("-phase4Port", 7795);
                List<Phase04Check> checks = RunBuild(EditorUserBuildSettings.activeBuildTarget, minutes, port);
                string report = Phase04Checks.Format(checks);
                Debug.Log(report);
                code = Phase04Checks.ExitCode(checks);
            }
            catch (Exception e) { Debug.LogException(e); }
            EditorApplication.Exit(code);
        }

        /// <summary>Managed gates + shipped scenario + scene audit + fake scan.</summary>
        public static List<Phase04Check> RunAll(bool includeLongRuns)
        {
            var checks = Phase04Validation.RunManaged(includeLongRuns);
            checks.AddRange(ShippedScenario());
            checks.AddRange(AuditScene(ScenePath));
            checks.Add(ScanProductionFakeUsage());
            checks.Add(Phase04Checks.Pending("build of the living base and its two-process run", Phase04Kinds.Build,
                "run BuildFromCommandLine and RunBuildFromCommandLine; this report does not certify them"));
            return checks;
        }

        /// <summary>D2's shipped scenario asset, through its own Validate()/ToScenario() (found by reflection: no compile-time
        /// dependency on D2's class), then the D5 scenario validator.</summary>
        public static List<Phase04Check> ShippedScenario()
        {
            string k = Phase04Kinds.ScenarioValidator;
            var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(ScenarioAssetPath);
            if (asset == null) return new List<Phase04Check> { Phase04Checks.Pending("shipped scenario asset", k, "pendiente de D2: " + ScenarioAssetPath + " missing") };
            Type t = asset.GetType();
            MethodInfo validate = t.GetMethod("Validate", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            MethodInfo toScenario = t.GetMethod("ToScenario", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (toScenario == null || toScenario.ReturnType != typeof(BaseOperationsScenario))
                return new List<Phase04Check> { Phase04Checks.Result("shipped scenario asset", k, false, t.Name + " has no ToScenario(): BaseOperationsScenario") };
            string problem = validate != null && validate.ReturnType == typeof(string) ? (string)validate.Invoke(asset, null) : null;
            if (problem != null) return new List<Phase04Check> { Phase04Checks.Result("shipped scenario asset", k, false, problem) };
            BaseOperationsScenario scenario;
            try { scenario = (BaseOperationsScenario)toScenario.Invoke(asset, null); }
            catch (TargetInvocationException e) { return new List<Phase04Check> { Phase04Checks.Result("shipped scenario asset", k, false, e.InnerException?.Message) }; }
            var checks = new List<Phase04Check> { Phase04Checks.Info("shipped scenario asset", k, t.Name + " '" + scenario.Id + "'") };
            checks.AddRange(BaseOperationsScenarioValidator.Validate(scenario, "shipped"));
            return checks;
        }

        /// <summary>Opens the productive scene additively (never saving) and audits its composition by contract.</summary>
        public static List<Phase04Check> AuditScene(string path)
        {
            string k = Phase04Kinds.SceneAudit;
            var checks = new List<Phase04Check>();
            if (!File.Exists(path))
            {
                checks.Add(Phase04Checks.Pending("productive scene " + path, k, "pendiente de D2: scene missing (no substitute scene is accepted)"));
                return checks;
            }
            Scene scene = SceneManager.GetSceneByPath(path);
            bool openedHere = !scene.isLoaded;
            if (openedHere) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                GameObject[] roots = scene.GetRootGameObjects();
                MonoBehaviour[] all = roots.SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(true)).Where(b => b != null).ToArray();
                int missingScripts = roots.SelectMany(r => r.GetComponentsInChildren<Component>(true)).Count(c => c == null);
                checks.Add(Phase04Checks.Result("no missing scripts", k, missingScripts == 0, missingScripts + " missing"));
                MonoBehaviour[] Named(string suffix) => all.Where(b => b.GetType().Name.EndsWith(suffix, StringComparison.Ordinal)).ToArray();

                MonoBehaviour[] compositions = all.Where(b => b.GetType().Name.Contains("BaseOperations") && b.GetType().Name.EndsWith("SceneComposition", StringComparison.Ordinal)).ToArray();
                checks.Add(Phase04Checks.Result("exactly one phase 4 scene composition", k, compositions.Length == 1, string.Join(", ", compositions.Select(c => c.GetType().Name))));
                if (compositions.Length == 1)
                {
                    var so = new SerializedObject(compositions[0]);
                    var missing = new List<string>();
                    SerializedProperty it = so.GetIterator();
                    for (bool enter = true; it.NextVisible(enter); enter = false)
                        if (it.propertyType == SerializedPropertyType.ObjectReference && it.name != "m_Script" && it.objectReferenceValue == null) missing.Add(it.name);
                    checks.Add(Phase04Checks.Result("composition references are wired", k, missing.Count == 0, missing.Count == 0 ? "all assigned" : "missing: " + string.Join(", ", missing)));
                }

                MonoBehaviour[] runners = Named("Runner");
                checks.Add(Phase04Checks.Result("one clock: a single runner, the phase 4 one", k,
                    runners.Length == 1 && runners[0].GetType().Name.Contains("BaseOperations"), string.Join(", ", runners.Select(r => r.GetType().Name))));
                int oldAuthorities = all.Count(b => b is ExpeditionBootstrap || b is ExpeditionSceneComposition);
                checks.Add(Phase04Checks.Result("no phase 3 bootstrap or composition in the phase 4 scene", k, oldAuthorities == 0, oldAuthorities + " found"));
                int oldNet = all.Count(b => b is ExpeditionNetworkHost || b is ExpeditionNetworkClient || b is OutpostNetworkHost || b is OutpostNetworkClient
                    || b is Trainer.Unity.Networking.SessionNetworkHost);
                checks.Add(Phase04Checks.Result("a scene speaks one cable: no phase 1/2/3 network components", k, oldNet == 0, oldNet + " found"));
                checks.Add(Phase04Checks.Result("one phase 4 host and one phase 4 client (D3)", k,
                    Named("BaseOperationsNetworkHost").Length == 1 && Named("BaseOperationsNetworkClient").Length == 1,
                    $"{Named("BaseOperationsNetworkHost").Length} host, {Named("BaseOperationsNetworkClient").Length} client"));
                int keyboards = all.Count(b => b is CrewKeyboardInput);
                checks.Add(Phase04Checks.Result("single movement controller", k, keyboards == 1, keyboards + " keyboard component(s)"));

                var fakes = all.Where(b => b.GetType().Name.IndexOf("Fake", StringComparison.OrdinalIgnoreCase) >= 0
                    || (b.GetType().Namespace ?? "").Contains(".Development")).Select(b => b.GetType().Name).ToList();
                checks.Add(Phase04Checks.Result("no fake or development component in the productive scene", k, fakes.Count == 0,
                    fakes.Count == 0 ? "clean" : string.Join(", ", fakes)));

                Transform mount = roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).FirstOrDefault(x => x.name == MountName);
                IBaseOperationsView[] views = mount == null ? new IBaseOperationsView[0] : mount.GetComponentsInChildren<IBaseOperationsView>(true);
                checks.Add(Phase04Checks.Result("'" + MountName + "' holds phase 4 views", k, views.Length > 0,
                    mount == null ? "mount missing" : views.Length + " view(s): " + string.Join(", ", views.Select(v => v.GetType().Name))));
                var provisional = views.Where(v => (v.GetType().Namespace ?? "").Contains("Provisional")).Select(v => v.GetType().Name).ToList();
                checks.Add(provisional.Count == 0
                    ? Phase04Checks.Result("D4 views replace the provisional ones", k, views.Length > 0, "no provisional view mounted")
                    : Phase04Checks.Pending("D4 views replace the provisional ones", k, "pendiente de D4/integración: provisional views mounted: " + string.Join(", ", provisional)));

                int physics = roots.Sum(r => r.GetComponentsInChildren<Collider2D>(true).Length + r.GetComponentsInChildren<Rigidbody2D>(true).Length
                    + r.GetComponentsInChildren<Collider>(true).Length + r.GetComponentsInChildren<Rigidbody>(true).Length);
                checks.Add(Phase04Checks.Result("no Unity physics decides positions", k, physics == 0, physics + " collider/rigidbody components"));
                int pilots = all.Count(b => b is BaseOperationsAutopilot);
                checks.Add(pilots == 1
                    ? Phase04Checks.Result("D5 continuity pilot present (inert without -trainer-autopilot)", k, true, "1")
                    : Phase04Checks.Pending("D5 continuity pilot present (inert without -trainer-autopilot)", k,
                        "pendiente de D2 (único editor de escenas): place one BaseOperationsAutopilot; found " + pilots));
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(scene, true);
            }
            return checks;
        }

        /// <summary>Player sources (Runtime outside Editor folders and the contracts' Development folder) must not use the phase 4
        /// development fake or fixtures.</summary>
        public static Phase04Check ScanProductionFakeUsage()
        {
            var offenders = new List<string>();
            foreach (string file in Directory.GetFiles("Assets/_Project/Runtime", "*.cs", SearchOption.AllDirectories))
            {
                string path = file.Replace('\\', '/');
                if (path.Split('/').Contains("Editor") || path.Contains("/Contracts/BaseOperations/Development/")) continue;
                string text = Regex.Replace(File.ReadAllText(path), @"//.*?$|/\*.*?\*/", "", RegexOptions.Multiline | RegexOptions.Singleline);
                if (Regex.IsMatch(text, @"\b(FakeBaseOperationsSessionPort|BaseOperationsFixtures|BaseOperationsSnapshotBuilder)\b")) offenders.Add(path);
            }
            return Phase04Checks.Result("no phase 4 development fake or fixture in player sources", Phase04Kinds.SceneAudit, offenders.Count == 0,
                offenders.Count == 0 ? "clean" : string.Join(", ", offenders));
        }

        // ---- build -------------------------------------------------------------------------------------------------

        public static string[] ReadScenes()
        {
            string value = ArgumentValue(SceneArgument);
            return value != null ? value.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries) : new[] { ScenePath };
        }

        /// <summary>Quick gates (discovery, shipped scenario, scene audit, fake scan) must have no Failed before building, unless
        /// forced; the result and the gates go to build-info.txt either way.</summary>
        public static string BuildGated(string[] scenes, bool force)
        {
            foreach (string scene in scenes)
                if (!Path.GetFileName(scene).StartsWith("Phase04_", StringComparison.Ordinal) || !File.Exists(scene))
                    throw new ArgumentException("Only existing Phase04_* scenes are built here: " + scene);
            var gates = new List<Phase04Check>(Phase04ImplementationDiscovery.Discover().Checks);
            gates.AddRange(ShippedScenario());
            foreach (string scene in scenes) gates.AddRange(AuditScene(scene));
            gates.Add(ScanProductionFakeUsage());
            int gate = Phase04Checks.ExitCode(gates);
            Write("d5-build-gates.txt", Phase04Checks.Format(gates));
            if (gate == Phase04Checks.ExitFailed && !force)
                return "NotBuilt: a gate failed (see " + LogRoot + "/d5-build-gates.txt); the build is not certified";
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            if (!BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(target), target))
                throw new InvalidOperationException("Build target not installed: " + target);
            // A forced build never overwrites the candidate used by RunBuild, even when its quick gates happen to pass.
            string output = PlayerPath(target, force);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = scenes, locationPathName = output, target = target, options = BuildOptions.None });
            string gateText = gate == Phase04Checks.ExitPassed ? "passed" : gate == Phase04Checks.ExitPending ? "pending" : "FAILED (forced build)";
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(output), "build-info.txt"),
                $"commit {Git("rev-parse --verify HEAD")}\nworking tree {(string.IsNullOrEmpty(Git("status --porcelain")) ? "clean" : "MODIFIED")}\n" +
                $"editor {UnityEngine.Application.unityVersion}\ntarget {target}\nscenes {string.Join(";", scenes)}\n" +
                $"scenario {ScenarioAssetPath} guid {AssetDatabase.AssetPathToGUID(ScenarioAssetPath)}\ngates {gateText}\n" +
                $"purpose {(force ? "diagnostic-only" : "acceptance-candidate (quick gates only)")}\n" +
                $"result {report.summary.result}\nsha256 {(File.Exists(output) ? Sha256(output) : "unavailable")}\nutc {DateTime.UtcNow:O}\n");
            return (force ? "Diagnostic" : "") + report.summary.result + ": " + output + " (gates " + gateText + ")";
        }

        public static string PlayerPath(BuildTarget target, bool diagnostic = false)
        {
            string name;
            switch (target)
            {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64: name = "Phase4LivingBase.exe"; break;
                case BuildTarget.StandaloneLinux64: name = "Phase4LivingBase.x86_64"; break;
                case BuildTarget.StandaloneOSX: name = "Phase4LivingBase.app"; break;
                default: name = "Phase4LivingBase"; break;
            }
            return diagnostic ? Path.Combine(BuildRoot, "Diagnostic", target.ToString(), name) : Path.Combine(BuildRoot, target.ToString(), name);
        }

        private static string Sha256(string path)
        {
            using (var hash = SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>
        /// Runs the BUILT player as an authority process plus two client processes (sockets on 127.0.0.1, -batchmode
        /// -nographics: no render and no human keyboard), each client driven by the D5 continuity pilot for
        /// <paramref name="minutes"/> REAL minutes. Real and simulated time are recorded separately. Network arguments are the
        /// shared phase 1–3 ones (-trainer-role/-trainer-bind/-trainer-server/-trainer-port/-trainer-connect).
        /// </summary>
        public static List<Phase04Check> RunBuild(BuildTarget target, double minutes, int port)
        {
            string k = Phase04Kinds.BuildRun;
            string player = Path.GetFullPath(PlayerPath(target));
            if (target == BuildTarget.StandaloneOSX) player = Path.Combine(player, "Contents/MacOS/Phase4LivingBase");
            if (!File.Exists(player)) return new List<Phase04Check> { Phase04Checks.Pending("built player", k, "no build at " + player + ": run BuildFromCommandLine first") };
            string info = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(PlayerPath(target))), "build-info.txt");
            string dir = Path.GetFullPath(Path.Combine(LogRoot, "run-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)));
            Directory.CreateDirectory(dir);
            string p = port.ToString(CultureInfo.InvariantCulture), m = minutes.ToString(CultureInfo.InvariantCulture);
            var watch = Stopwatch.StartNew();
            using (Process authority = Launch(player, $"-batchmode -nographics -trainer-role authority -trainer-bind 127.0.0.1 -trainer-port {p} -logFile \"{dir}/authority.log\""))
            {
                System.Threading.Thread.Sleep(5000);
                string client = $"-batchmode -nographics -trainer-role client -trainer-server 127.0.0.1 -trainer-port {p} -trainer-connect -trainer-autopilot continuity -trainer-autopilot-minutes {m}";
                using (Process water = Launch(player, client + $" -trainer-autopilot-role water -logFile \"{dir}/water.log\""))
                using (Process air = Launch(player, client + $" -trainer-autopilot-role air -logFile \"{dir}/air.log\""))
                {
                    int limit = (int)((minutes * 60 + 900) * 1000);
                    bool waterDone = water.WaitForExit(limit);
                    bool airDone = air.WaitForExit((int)Math.Max(1000, limit - watch.ElapsedMilliseconds));
                    if (!waterDone) Kill(water);
                    if (!airDone) Kill(air);
                    Kill(authority);
                    double real = watch.Elapsed.TotalSeconds;
                    var checks = new List<Phase04Check>
                    {
                        Phase04Checks.Info("build run", k, $"{player}; {(File.Exists(info) ? File.ReadAllText(info).Replace('\n', ' ') : "no build-info.txt")}; logs {dir}")
                    };
                    checks.Add(Verdict("water client (pilot, not a person)", Path.Combine(dir, "water.log"), waterDone, water, real, k));
                    checks.Add(Verdict("air/logistics client (pilot, not a person)", Path.Combine(dir, "air.log"), airDone, air, real, k));
                    checks.Add(Phase04Checks.Pending("two people play the build for 30+ real minutes", Phase04Kinds.HumanSession,
                        "an automated pilot over sockets does not prove fun, comprehension, render or a human keyboard"));
                    Write("run-summary.txt", Phase04Checks.Format(checks));
                    File.Copy(Path.Combine(LogRoot, "run-summary.txt"), Path.Combine(dir, "run-summary.txt"), true);
                    return checks;
                }
            }
        }

        private static Phase04Check Verdict(string name, string log, bool exited, Process process, double realSeconds, string kind)
        {
            string text = File.Exists(log) ? File.ReadAllText(log) : "";
            Match result = Regex.Matches(text, @"\[Autopilot\] RESULT (PASS|FAIL)[^\r\n]*").Cast<Match>().LastOrDefault();
            Match sim = Regex.Matches(text, @"sim=([0-9.]+)s").Cast<Match>().LastOrDefault();
            string detail = $"real {realSeconds:0} s (process wall clock); simulated {(sim != null ? sim.Groups[1].Value + " s" : "unknown")}; " +
                            $"exit {(exited ? process.ExitCode.ToString(CultureInfo.InvariantCulture) : "killed (timeout)")}; " +
                            (result != null ? result.Value : "no RESULT line in " + log);
            bool passed = exited && process.ExitCode == 0 && result != null && result.Groups[1].Value == "PASS";
            return Phase04Checks.Result(name, kind, passed, detail);
        }

        private static Process Launch(string file, string arguments) =>
            Process.Start(new ProcessStartInfo(file, arguments) { UseShellExecute = false, CreateNoWindow = true })
            ?? throw new InvalidOperationException("could not start " + file);

        private static void Kill(Process process)
        {
            try { if (!process.HasExited) process.Kill(); }
            catch (Exception) { }
        }

        // ---- helpers -----------------------------------------------------------------------------------------------

        private static void Write(string name, string text)
        {
            Directory.CreateDirectory(LogRoot);
            File.WriteAllText(Path.Combine(LogRoot, name), text);
        }

        private static bool HasArgument(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;

        private static string ArgumentValue(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        private static double NumberArgument(string name, double fallback) =>
            double.TryParse(ArgumentValue(name), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v > 0 ? v : fallback;

        private static string Git(string arguments)
        {
            try
            {
                using (Process process = Process.Start(new ProcessStartInfo("git", arguments)
                       { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true }))
                {
                    if (process == null) return "?";
                    string output = process.StandardOutput.ReadToEnd();
                    if (!process.WaitForExit(5000)) return "?";
                    return process.ExitCode == 0 ? output.Trim() : "?";
                }
            }
            catch (Exception) { return "?"; }
        }
    }
}
