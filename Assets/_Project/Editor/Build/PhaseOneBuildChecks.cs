using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Trainer.Application;
using Trainer.Application.Development;
using Trainer.Simulation;
using Trainer.Tooling.Validation;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
// UnityEditor also declares SessionState; the mission contract type is the one used here.
using SessionState = Trainer.Application.SessionState;

namespace Trainer.Tooling.BuildChecks
{
    public enum CheckStatus { Passed, Failed, Pending, Info }

    public sealed class CheckOutcome
    {
        public string Name { get; }
        /// <summary>validator-synthetic | fake | environment | real-pending</summary>
        public string Kind { get; }
        public CheckStatus Status { get; }
        public string Detail { get; }

        public CheckOutcome(string name, string kind, CheckStatus status, string detail)
        {
            Name = name; Kind = kind; Status = status; Detail = detail ?? string.Empty;
        }

        public override string ToString() { return "[" + Status + "] (" + Kind + ") " + Name + (Detail.Length > 0 ? " - " + Detail : ""); }
    }

    /// <summary>
    /// Phase 1 checks that run with the current project configuration (no test assemblies, packages or
    /// settings changes). Everything here only reads settings and assets; scenes and prefabs are opened
    /// without saving. The build uses an explicit list of MISSION scenes (never SampleScene/templates) and
    /// writes to the git-ignored Builds/ folder; Build Settings are not modified.
    /// Batch usage (Unity closed for this project):
    ///   Unity.exe -batchmode -nographics -projectPath &lt;repo&gt; -executeMethod Trainer.Tooling.BuildChecks.PhaseOneBuildChecks.RunChecksFromCommandLine -logFile Logs/phase1-checks.log
    ///   Unity.exe -batchmode -nographics -projectPath &lt;repo&gt; -executeMethod Trainer.Tooling.BuildChecks.PhaseOneBuildChecks.BuildFromCommandLine -phase1Scenes "Assets/_Project/Scenes/&lt;Mission&gt;.unity" -logFile Logs/phase1-build.log
    /// Without -phase1Scenes the single scene under Assets/_Project/Scenes is used; zero or several is an error.
    /// </summary>
    public static class PhaseOneBuildChecks
    {
        public const string OutputRoot = "Builds/Phase1Check";
        public const string MissionBuildRoot = "Builds/Phase1Mission";
        public const string ReportFile = "phase1-checks.txt";
        public const string NUnitGateDefine = "TRAINER_PHASE1_VALIDATION_TESTS";
        public const string SceneArgument = "-phase1Scenes";
        public const string MissionScenesFolder = "Assets/_Project/Scenes";
        /// <summary>Template scenes that must never be presented as the game.</summary>
        public static readonly string[] TemplateScenes = { "Assets/Scenes/SampleScene.unity", "Assets/Settings/Scenes/URP2DSceneTemplate.unity" };

        [MenuItem("Tools/Trainer/Phase 1/Run validation checks")]
        public static void RunChecksMenu()
        {
            List<CheckOutcome> outcomes = RunAll();
            string path = WriteReport(outcomes);
            UnityEngine.Debug.Log(Format(outcomes) + "\nReport: " + path);
        }

        public static void RunChecksFromCommandLine()
        {
            int code = 1;
            try
            {
                List<CheckOutcome> outcomes = RunAll();
                string path = WriteReport(outcomes);
                UnityEngine.Debug.Log(Format(outcomes) + "\nReport: " + path);
                code = outcomes.Any(o => o.Status == CheckStatus.Failed) ? 1 : 0;
            }
            catch (Exception exception) { UnityEngine.Debug.LogException(exception); }
            EditorApplication.Exit(code);
        }

        [MenuItem("Tools/Trainer/Phase 1/Build mission scene (explicit list, Build Settings untouched)")]
        public static void BuildMenu()
        {
            BuildReport report = BuildMissionScenes(null);
            UnityEngine.Debug.Log("Phase 1 mission build: " + report.summary.result + " -> " + report.summary.outputPath);
        }

        public static void BuildFromCommandLine()
        {
            int code = 1;
            try
            {
                BuildReport report = BuildMissionScenes(ReadSceneArgument());
                UnityEngine.Debug.Log("Phase 1 mission build: " + report.summary.result + ", errors " + report.summary.totalErrors
                    + ", output " + report.summary.outputPath);
                code = report.summary.result == BuildResult.Succeeded ? 0 : 1;
            }
            catch (Exception exception) { UnityEngine.Debug.LogException(exception); }
            EditorApplication.Exit(code);
        }

        public static List<CheckOutcome> RunAll()
        {
            var outcomes = new List<CheckOutcome>();
            outcomes.AddRange(CollectEnvironment());
            outcomes.AddRange(PhaseOneContractChecks.RunValidatorChecks());
            outcomes.AddRange(PhaseOneContractChecks.RunFakeSessionChecks(() => new FakeSessionPort()));
            outcomes.AddRange(PhaseOneIntegrationChecks.RunRealSessionChecks());
            outcomes.AddRange(PhaseOneIntegrationChecks.RunLoopbackChecks());
            outcomes.AddRange(PhaseOneIntegrationChecks.RunDesignReview());
            outcomes.AddRange(PhaseOneAssetAudit.AuditMissionScenes(ReadSceneArgument()));
            outcomes.AddRange(PhaseOneAssetAudit.AuditPrefabs());
            outcomes.Add(PhaseOneAssetAudit.ScanProductionFakeUsage());
            outcomes.AddRange(PhaseOneAssetAudit.InventoryDisabledTests());
            outcomes.AddRange(PhaseOneContractChecks.PendingIntegrationChecks());
            return outcomes;
        }

        /// <summary>Scenes from -phase1Scenes "a.unity;b.unity", or null when the argument is absent.</summary>
        public static string[] ReadSceneArgument()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == SceneArgument)
                    return args[i + 1].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
            return null;
        }

        /// <summary>Explicit scenes, or the only scene under Assets/_Project/Scenes. Never SampleScene or templates.
        /// Returns null and a reason when no valid mission scene can be chosen.</summary>
        public static string[] ResolveMissionScenes(string[] explicitScenes, out string problem)
        {
            problem = null;
            string[] scenes = explicitScenes;
            if (scenes == null)
            {
                scenes = Directory.Exists(MissionScenesFolder)
                    ? AssetDatabase.FindAssets("t:Scene", new[] { MissionScenesFolder }).Select(g => AssetDatabase.GUIDToAssetPath(g))
                        .Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray()
                    : new string[0];
                if (scenes.Length == 0) { problem = "No mission scene under " + MissionScenesFolder + " (scene composition is D2's)."; return null; }
                if (scenes.Length > 1) { problem = "Several scenes under " + MissionScenesFolder + "; pass " + SceneArgument + " explicitly: " + string.Join(", ", scenes); return null; }
            }
            if (scenes.Length == 0) { problem = SceneArgument + " is empty."; return null; }
            foreach (string scene in scenes)
            {
                if (TemplateScenes.Any(t => string.Equals(t, scene, StringComparison.OrdinalIgnoreCase)))
                { problem = scene + " is a template scene, not the mission."; return null; }
                if (!File.Exists(scene)) { problem = scene + " does not exist."; return null; }
            }
            return scenes;
        }

        /// <summary>Builds the active target from an explicit scene list; Build Settings and the target stay as they are.</summary>
        public static BuildReport BuildMissionScenes(string[] explicitScenes)
        {
            string problem;
            string[] scenes = ResolveMissionScenes(explicitScenes, out problem);
            if (scenes == null) throw new InvalidOperationException("Mission build refused: " + problem);
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            BuildTargetGroup group = BuildPipeline.GetBuildTargetGroup(target);
            if (!BuildPipeline.IsBuildTargetSupported(group, target))
                throw new InvalidOperationException("Active build target " + target + " has no installed module. Install nothing automatically; report it.");
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                target = target,
                targetGroup = group,
                locationPathName = Path.Combine(MissionBuildRoot, target.ToString(), PlayerName(target)),
                options = BuildOptions.None
            };
            return BuildPipeline.BuildPlayer(options);
        }

        public static List<CheckOutcome> CollectEnvironment()
        {
            const string kind = "environment";
            var outcomes = new List<CheckOutcome>();
            string running = UnityEngine.Application.unityVersion;
            string expected = ReadProjectEditorVersion();
            outcomes.Add(new CheckOutcome("EditorVersionMatchesProject", kind,
                expected == null ? CheckStatus.Pending : (expected == running ? CheckStatus.Passed : CheckStatus.Failed),
                "running " + running + ", ProjectVersion.txt " + (expected ?? "unreadable")));

            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            BuildTargetGroup group = BuildPipeline.GetBuildTargetGroup(target);
            bool activeSupported = BuildPipeline.IsBuildTargetSupported(group, target);
            outcomes.Add(new CheckOutcome("ActiveBuildTargetModuleInstalled", kind,
                activeSupported ? CheckStatus.Passed : CheckStatus.Failed, target + " (" + group + ")"));

            var supported = new List<string>();
            foreach (BuildTarget candidate in new[] { BuildTarget.StandaloneWindows64, BuildTarget.StandaloneLinux64,
                         BuildTarget.StandaloneOSX, BuildTarget.WebGL })
                if (BuildPipeline.IsBuildTargetSupported(BuildPipeline.GetBuildTargetGroup(candidate), candidate))
                    supported.Add(candidate.ToString());
            outcomes.Add(new CheckOutcome("InstalledDesktopWebModules", kind, CheckStatus.Info,
                supported.Count == 0 ? "none" : string.Join(", ", supported)));

            NamedBuildTarget named = NamedBuildTarget.FromBuildTargetGroup(group);
            outcomes.Add(new CheckOutcome("ScriptingBackend", kind, CheckStatus.Info, PlayerSettings.GetScriptingBackend(named).ToString()));

            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            var missing = scenes.Where(s => s.enabled && !File.Exists(s.path)).Select(s => s.path).ToArray();
            outcomes.Add(new CheckOutcome("BuildScenesExist", kind,
                scenes.Count(s => s.enabled) == 0 || missing.Length > 0 ? CheckStatus.Failed : CheckStatus.Passed,
                "enabled: " + string.Join(", ", scenes.Where(s => s.enabled).Select(s => s.path).ToArray())
                + (missing.Length > 0 ? "; missing: " + string.Join(", ", missing) : "")));
            string sceneProblem;
            string[] missionScenes = ResolveMissionScenes(ReadSceneArgument(), out sceneProblem);
            outcomes.Add(new CheckOutcome("MissionSceneForBuild", "build",
                missionScenes == null ? CheckStatus.Pending : CheckStatus.Info,
                missionScenes == null ? sceneProblem : "explicit build list: " + string.Join(", ", missionScenes)
                    + " (Build Settings are not modified)"));

            string manifest = ReadText("Packages/manifest.json");
            outcomes.Add(new CheckOutcome("TestFrameworkPackage", kind,
                manifest != null && manifest.Contains("\"com.unity.test-framework\"") ? CheckStatus.Passed : CheckStatus.Failed,
                "com.unity.test-framework in Packages/manifest.json"));
            string[] networking = { "com.unity.netcode", "com.unity.transport", "com.unity.services.multiplayer", "mirror", "photon", "fishnet" };
            string[] found = manifest == null ? new string[0] : networking.Where(n => manifest.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
            outcomes.Add(new CheckOutcome("NetworkingBackend", "real-pending",
                found.Length == 0 ? CheckStatus.Pending : CheckStatus.Info,
                found.Length == 0 ? "No networking package selected (expected: AGENTS.md says no backend is approved)." : string.Join(", ", found)));

            string[] asmdefs = Directory.Exists("Assets/_Project")
                ? Directory.GetFiles("Assets/_Project", "*.asmdef", SearchOption.AllDirectories) : new string[0];
            string defines = PlayerSettings.GetScriptingDefineSymbols(named) ?? string.Empty;
            bool gate = defines.Split(';').Any(d => d.Trim() == NUnitGateDefine);
            outcomes.Add(new CheckOutcome("ValidationNUnitAssemblies", "real-pending",
                asmdefs.Length > 0 && gate ? CheckStatus.Info : CheckStatus.Pending,
                asmdefs.Length + " .asmdef under Assets/_Project; define " + NUnitGateDefine + (gate ? " set" : " not set")
                + ". NUnit files in Tests/EditMode/Validation and Tests/PlayMode/Integration stay compiled out until integration adds test assemblies."));
            return outcomes;
        }

        public static string Format(IEnumerable<CheckOutcome> outcomes)
        {
            var list = outcomes.ToList();
            var text = new StringBuilder();
            text.AppendLine("Phase 1 validation checks (" + DateTime.UtcNow.ToString("u") + ")");
            foreach (CheckStatus status in new[] { CheckStatus.Passed, CheckStatus.Failed, CheckStatus.Pending, CheckStatus.Info })
                text.Append(status).Append(": ").Append(list.Count(o => o.Status == status)).Append("  ");
            text.AppendLine();
            foreach (CheckOutcome outcome in list) text.AppendLine(outcome.ToString());
            return text.ToString();
        }

        private static string WriteReport(List<CheckOutcome> outcomes)
        {
            Directory.CreateDirectory(OutputRoot);
            string path = Path.Combine(OutputRoot, ReportFile);
            File.WriteAllText(path, Format(outcomes));
            return path;
        }

        private static string PlayerName(BuildTarget target)
        {
            switch (target)
            {
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64: return "Phase1Check.exe";
                case BuildTarget.StandaloneOSX: return "Phase1Check.app";
                case BuildTarget.StandaloneLinux64: return "Phase1Check.x86_64";
                default: return "Phase1Check";
            }
        }

        private static string ReadProjectEditorVersion()
        {
            string text = ReadText("ProjectSettings/ProjectVersion.txt");
            if (text == null) return null;
            foreach (string line in text.Split('\n'))
                if (line.StartsWith("m_EditorVersion:", StringComparison.Ordinal))
                    return line.Substring("m_EditorVersion:".Length).Trim();
            return null;
        }

        private static string ReadText(string path) { return File.Exists(path) ? File.ReadAllText(path) : null; }
    }

    /// <summary>
    /// Executable phase 1 matrix without NUnit. Synthetic fixtures are labeled SYNTHETIC (not NASA data);
    /// expected values were computed independently: cap 36000 J, E0 18000 J, generation 50 W, essential 60 W,
    /// science 20 W, max charge/discharge 40 W. Lossless deficit: 10 W for 60 s -> 17400 J; 30 W for 60 s -> 15600 J.
    /// FakeSessionPort checks prove the shared contract/fake only, never networking or a real authority.
    /// </summary>
    public static class PhaseOneContractChecks
    {
        private sealed class CheckFailed : Exception { public CheckFailed(string message) : base(message) { } }

        private static void Require(bool condition, string message) { if (!condition) throw new CheckFailed(message); }

        private static void RequireNoErrors(ValidationReport report, string context)
        {
            if (report.HasErrors) throw new CheckFailed(context + ": " + report);
        }

        private static void RequireCode(ValidationReport report, string code)
        {
            if (!report.Contains(code)) throw new CheckFailed("Expected " + code + ", got: " + report);
        }

        private static CheckOutcome Run(string name, string kind, Func<string> body)
        {
            try { return new CheckOutcome(name, kind, CheckStatus.Passed, body()); }
            catch (CheckFailed failure) { return new CheckOutcome(name, kind, CheckStatus.Failed, failure.Message); }
            catch (Exception exception) { return new CheckOutcome(name, kind, CheckStatus.Failed, exception.GetType().Name + ": " + exception.Message); }
        }

        // ------------------------------------------------------------ synthetic fixture (SYNTHETIC, not NASA)

        public static PowerConfiguration SyntheticConfiguration()
        {
            return new PowerConfiguration(18000, 36000, 50, 60, 20, 40, 40);
        }

        private static PowerSnapshot Power(double t, double energy, double scienceRequested = 0, double scienceServed = 0,
            double essentialServed = 60)
        {
            return new PowerSnapshot(t, energy, 36000, 50, 60, essentialServed, scienceRequested, scienceServed);
        }

        private static MissionSnapshot Mission(long epoch, long revision, SessionState state, PowerSnapshot power,
            double work, bool science)
        {
            return new MissionSnapshot(epoch, revision, state, power, work, science);
        }

        /// <summary>Ready -> Resume -> 60 s essential only -> science on -> 60 s with science -> Pause.</summary>
        public static MissionSnapshot[] SyntheticValidSequence()
        {
            return new[]
            {
                Mission(0, 0, SessionState.Ready, Power(0, 18000), 0, false),
                Mission(0, 1, SessionState.Running, Power(0, 18000), 0, false),
                Mission(0, 2, SessionState.Running, Power(60, 17400), 0, false),
                Mission(0, 3, SessionState.Running, Power(60, 17400), 0, true),
                Mission(0, 4, SessionState.Running, Power(120, 15600, 20, 20), 60, true),
                Mission(0, 5, SessionState.Paused, Power(120, 15600, 20, 20), 60, true)
            };
        }

        public static List<CheckOutcome> RunValidatorChecks()
        {
            const string kind = "validator-synthetic";
            var outcomes = new List<CheckOutcome>();
            PowerConfiguration config = SyntheticConfiguration();

            outcomes.Add(Run("SyntheticConfigurationIsClean", kind, () =>
            {
                ValidationReport report = PhaseOneScenarioValidator.ValidateConfiguration(config);
                Require(report.Issues.Count == 0, report.ToString());
                return "no errors or warnings";
            }));

            outcomes.Add(Run("ContractConstructorRejectsInvalidMagnitudes", kind, () =>
            {
                Require(Throws(() => new PowerConfiguration(double.NaN, 1, 0, 0, 0, 0, 0)), "NaN accepted");
                Require(Throws(() => new PowerConfiguration(0, double.PositiveInfinity, 0, 0, 0, 0, 0)), "Infinity accepted");
                Require(Throws(() => new PowerConfiguration(0, 1, -1, 0, 0, 0, 0)), "negative accepted");
                Require(Throws(() => new PowerConfiguration(2, 1, 0, 0, 0, 0, 0)), "initial > capacity accepted");
                return "NaN, Infinity, negative and initial > capacity throw";
            }));

            outcomes.Add(Run("UnitMismatchIsFlagged", kind, () =>
            {
                // Capacity typed as 10 (Wh?) instead of 36000 J.
                RequireCode(PhaseOneScenarioValidator.ValidateConfiguration(new PowerConfiguration(5, 10, 50, 60, 20, 40, 40)), "PossibleUnitMismatch");
                return "PossibleUnitMismatch warning";
            }));

            outcomes.Add(Run("UnservableDemandIsFlagged", kind, () =>
            {
                RequireCode(PhaseOneScenarioValidator.ValidateConfiguration(new PowerConfiguration(0, 36000, 10, 60, 20, 40, 40)), "EssentialNotFullyServable");
                RequireCode(PhaseOneScenarioValidator.ValidateConfiguration(new PowerConfiguration(0, 36000, 50, 60, 40, 40, 40)), "ScienceNotFullyServable");
                return "essential and science warnings";
            }));

            outcomes.Add(Run("LargeMagnitudeIsPrecisionWarning", kind, () =>
            {
                ValidationReport report = PhaseOneScenarioValidator.ValidateConfiguration(new PowerConfiguration(0, 1e16, 0, 0, 0, 0, 0));
                RequireCode(report, "ExceedsExactRange");
                Require(!report.HasErrors, "Finite magnitudes must remain valid.");
                return "1e16 J > 2^53: precision warning only";
            }));

            outcomes.Add(Run("SyntheticSequenceBalances", kind, () =>
            {
                MissionSnapshot[] sequence = SyntheticValidSequence();
                RequireNoErrors(PhaseOneScenarioValidator.ValidateMissionSnapshot(sequence[0], config), "initial");
                for (int i = 1; i < sequence.Length; i++)
                {
                    ValidationReport report = PhaseOneScenarioValidator.ValidateMissionTransition(sequence[i - 1], sequence[i], config);
                    Require(report.Issues.Count == 0, "step " + i + ": " + report);
                }
                return sequence.Length + " snapshots, 0 issues";
            }));

            outcomes.Add(Run("EnergyAccountingViolationsAreErrors", kind, () =>
            {
                PowerSnapshot start = Power(0, 18000);
                RequireCode(PhaseOneScenarioValidator.ValidatePowerTransition(start, Power(60, 21000), config), "EnergyGainExceedsLimits");
                RequireCode(PhaseOneScenarioValidator.ValidatePowerTransition(start, Power(60, 15000), config), "EnergyLossExceedsLimits");
                RequireCode(PhaseOneScenarioValidator.ValidatePowerTransition(Power(60, 17400), start, config), "TimeWentBackwards");
                RequireCode(PhaseOneScenarioValidator.ValidatePowerSnapshot(Power(0, 36001)), "EnergyAboveCapacity");
                RequireCode(PhaseOneScenarioValidator.ValidatePowerSnapshot(Power(0, 18000, 0, 0, 70)), "ServedExceedsRequested");
                RequireCode(PhaseOneScenarioValidator.ValidatePowerSnapshot(Power(0, 18000, 20, 20, 60)
                    , new PowerConfiguration(18000, 36000, 50, 60, 20, 40, 10)), "ServedExceedsSupply");
                return "gain, loss, time, capacity, served and supply bounds";
            }));

            outcomes.Add(Run("MissionStreamViolationsAreErrors", kind, () =>
            {
                MissionSnapshot[] s = SyntheticValidSequence();
                RequireCode(PhaseOneScenarioValidator.ValidateMissionTransition(s[5],
                    Mission(0, 6, SessionState.Paused, Power(180, 15600, 20, 20), 60, true), config), "ClockAdvancedWhileNotRunning");
                RequireCode(PhaseOneScenarioValidator.ValidateMissionTransition(s[3],
                    Mission(0, 4, SessionState.Running, Power(120, 15600, 20, 20), 90, true), config), "ScienceWorkExceedsElapsedTime");
                RequireCode(PhaseOneScenarioValidator.ValidateMissionTransition(
                    Mission(1, 0, SessionState.Ready, Power(0, 18000), 0, false), s[2], config), "EpochRegressed");
                RequireCode(PhaseOneScenarioValidator.ValidateMissionTransition(s[2],
                    Mission(0, 2, SessionState.Running, Power(60, 17000), 0, false), config), "RevisionReusedWithDifferentContent");
                RequireCode(PhaseOneScenarioValidator.ValidateMissionTransition(s[2], s[1], config), "RevisionRegressed");
                RequireCode(PhaseOneScenarioValidator.ValidateMissionTransition(s[2],
                    Mission(0, 3, SessionState.Running, Power(90, 17100), 30, false), config), "ScienceWorkWithoutServedPower");
                return "pause clock, powered work, epoch, revision";
            }));

            outcomes.Add(Run("RestartCleanlinessIsChecked", kind, () =>
            {
                MissionSnapshot last = SyntheticValidSequence()[5];
                ValidationReport clean = PhaseOneScenarioValidator.ValidateMissionTransition(last,
                    Mission(1, 0, SessionState.Ready, Power(0, 18000), 0, false), config);
                Require(clean.Issues.Count == 0, clean.ToString());
                RequireCode(PhaseOneScenarioValidator.ValidateMissionTransition(last,
                    Mission(1, 0, SessionState.Ready, Power(0, 18000), 5, false), config), "RestartNotClean");
                RequireCode(PhaseOneScenarioValidator.ValidateMissionTransition(last,
                    Mission(1, 0, SessionState.Ready, Power(0, 15600), 0, false), config), "RestartNotClean");
                return "clean reset accepted; leftover work/energy flagged";
            }));

            return outcomes;
        }

        // ------------------------------------------------------------ session port (shared fake)

        private sealed class Probe
        {
            public readonly ISessionPort Port;
            public readonly List<MissionSnapshot> Snapshots = new List<MissionSnapshot>();
            public readonly List<CommandResult> Results = new List<CommandResult>();
            public readonly List<SessionCommand> Sent = new List<SessionCommand>();

            public Probe(ISessionPort port)
            {
                Port = port;
                port.SnapshotChanged += s => Snapshots.Add(s);
                port.CommandCompleted += r => Results.Add(r);
            }

            public MissionSnapshot Current
            {
                get
                {
                    MissionSnapshot snapshot;
                    Require(Port.TryGetSnapshot(out snapshot) && snapshot != null, "TryGetSnapshot returned no snapshot");
                    return snapshot;
                }
            }

            public CommandResult Send(SessionAction action, bool? value = null)
            {
                return Send(new SessionCommand(Guid.NewGuid(), Current.Epoch, action, value));
            }

            /// <summary>Synchronous ports only (the shared fake). Async transports need another harness.</summary>
            public CommandResult Send(SessionCommand command)
            {
                int before = Results.Count;
                Sent.Add(command);
                Port.Submit(command);
                Require(Results.Count == before + 1, "expected exactly one CommandCompleted, got " + (Results.Count - before));
                return Results[Results.Count - 1];
            }
        }

        public static List<CheckOutcome> RunFakeSessionChecks(Func<ISessionPort> factory)
        {
            const string kind = "fake";
            var outcomes = new List<CheckOutcome>();

            outcomes.Add(Run("InitialSnapshotIsValid", kind, () =>
            {
                var probe = new Probe(factory());
                MissionSnapshot s = probe.Current;
                RequireNoErrors(PhaseOneScenarioValidator.ValidateMissionSnapshot(s), "initial");
                Require(s.State == SessionState.Ready && s.Epoch == 0 && s.Revision == 0, "expected Ready, epoch 0, revision 0");
                return "Ready e0 r0";
            }));

            outcomes.Add(Run("RepeatedCommandIsNotReapplied", kind, () =>
            {
                var probe = new Probe(factory());
                var command = new SessionCommand(Guid.NewGuid(), 0, SessionAction.SetScienceEnabled, true);
                CommandResult first = probe.Send(command);
                MissionSnapshot after = probe.Current;
                int published = probe.Snapshots.Count;
                CommandResult replay = probe.Send(new SessionCommand(command.Id, 0, SessionAction.SetScienceEnabled, true));
                RequireNoErrors(PhaseOneScenarioValidator.ValidateReplay(first, replay), "replay");
                Require(probe.Snapshots.Count == published, "replay published a snapshot");
                Require(ReferenceEquals(probe.Current, after) && after.Revision == 1, "replay changed state");
                return "same result, no extra snapshot, revision stays 1";
            }));

            outcomes.Add(Run("ConflictingIdIsRejected", kind, () =>
            {
                var probe = new Probe(factory());
                var command = new SessionCommand(Guid.NewGuid(), 0, SessionAction.Resume);
                probe.Send(command);
                MissionSnapshot before = probe.Current;
                CommandResult conflict = probe.Send(new SessionCommand(command.Id, 0, SessionAction.Pause));
                Require(!conflict.Accepted && conflict.Reason == "CommandIdConflict", "got " + conflict.Reason);
                Require(ReferenceEquals(probe.Current, before), "state changed");
                return "CommandIdConflict, state unchanged";
            }));

            outcomes.Add(Run("StaleEpochHasNoEffectAfterRestart", kind, () =>
            {
                var probe = new Probe(factory());
                var restart = new SessionCommand(Guid.NewGuid(), 0, SessionAction.Restart);
                Require(probe.Send(restart).Accepted, "restart rejected");
                MissionSnapshot fresh = probe.Current;
                Require(fresh.Epoch == 1 && fresh.Revision == 0, "expected epoch 1 revision 0");
                int published = probe.Snapshots.Count;
                CommandResult stale = probe.Send(new SessionCommand(Guid.NewGuid(), 0, SessionAction.SetScienceEnabled, true));
                CommandResult future = probe.Send(new SessionCommand(Guid.NewGuid(), 2, SessionAction.Resume));
                Require(stale.Reason == "EpochMismatch" && future.Reason == "EpochMismatch", "got " + stale.Reason + "/" + future.Reason);
                Require(stale.Epoch == 0, "result must keep the command's original epoch");
                CommandResult replay = probe.Send(restart);
                Require(replay.Accepted && replay.Epoch == 0, "restart replay result changed");
                Require(probe.Snapshots.Count == published && ReferenceEquals(probe.Current, fresh), "stale/replayed command changed state");
                Require(probe.Current.Epoch == 1, "restart replay restarted again");
                return "old/future epoch rejected, restart replay not reapplied";
            }));

            outcomes.Add(Run("PauseResumeTransitions", kind, () =>
            {
                var probe = new Probe(factory());
                Require(probe.Send(SessionAction.Pause).Reason == "InvalidState", "Pause from Ready accepted");
                Require(probe.Send(SessionAction.Resume).Accepted && probe.Current.State == SessionState.Running, "Resume failed");
                double time = probe.Current.Power.SimulatedTimeSeconds;
                Require(probe.Send(SessionAction.Pause).Accepted && probe.Current.State == SessionState.Paused, "Pause failed");
                Require(probe.Send(SessionAction.Pause).Reason == "InvalidState", "double Pause accepted");
                Require(probe.Current.Power.SimulatedTimeSeconds == time, "time changed");
                Require(probe.Send(SessionAction.Resume).Accepted && probe.Current.State == SessionState.Running, "Resume from Paused failed");
                return "state machine OK; fake never advances time, so a stopped clock is NOT proven here";
            }));

            outcomes.Add(Run("RestartIsClean", kind, () =>
            {
                var probe = new Probe(factory());
                probe.Send(SessionAction.SetScienceEnabled, true);
                var fake = probe.Port as FakeSessionPort;
                Require(fake != null, "needs FakeSessionPort to script a Completed session");
                fake.PublishExample(SessionState.Completed, 42);
                MissionSnapshot before = probe.Current;
                Require(probe.Send(SessionAction.Resume).Reason == "SessionEnded", "ended session accepted Resume");
                Require(probe.Send(SessionAction.Restart).Accepted, "restart rejected");
                MissionSnapshot after = probe.Current;
                ValidationReport report = PhaseOneScenarioValidator.ValidateMissionTransition(before, after);
                Require(!report.Contains("RestartNotClean") && !report.HasErrors, report.ToString());
                Require(after.Epoch == before.Epoch + 1 && after.Revision == 0 && after.ScienceWorkSeconds == 0 && !after.ScienceEnabled,
                    "restart left state behind");
                return "epoch+1, revision 0, work 0, science off";
            }));

            outcomes.Add(Run("PublishedStreamIsMonotonic", kind, () =>
            {
                var probe = new Probe(factory());
                MissionSnapshot initial = probe.Current;
                probe.Send(SessionAction.Resume);
                probe.Send(SessionAction.SetScienceEnabled, true);
                probe.Send(SessionAction.SetScienceEnabled, true);   // same value, new ID: revision must still advance
                probe.Send(SessionAction.Pause);
                probe.Send(SessionAction.Resume);
                probe.Send(SessionAction.Restart);
                probe.Send(SessionAction.SetScienceEnabled, false);
                MissionSnapshot previous = initial;
                foreach (MissionSnapshot next in probe.Snapshots)
                {
                    ValidationReport report = PhaseOneScenarioValidator.ValidateMissionTransition(previous, next);
                    RequireNoErrors(report, "e" + next.Epoch + " r" + next.Revision);
                    previous = next;
                }
                return probe.Snapshots.Count + " snapshots validated";
            }));

            outcomes.Add(Run("ValidatorDetectsScriptedFakeWorkJump", kind, () =>
            {
                var probe = new Probe(factory());
                var fake = probe.Port as FakeSessionPort;
                Require(fake != null, "needs FakeSessionPort");
                MissionSnapshot before = probe.Current;
                fake.PublishExample(SessionState.Running, 42);
                RequireCode(PhaseOneScenarioValidator.ValidateMissionTransition(before, probe.Current), "ScienceWorkExceedsElapsedTime");
                return "expected: fake fixtures are illustrative, not physical (42 s of work in 0 s)";
            }));

            outcomes.Add(Run("MalformedCommandsMatchValidator", kind, () =>
            {
                var probe = new Probe(factory());
                MissionSnapshot before = probe.Current;
                var cases = new[]
                {
                    new SessionCommand(Guid.Empty, 0, SessionAction.Resume),
                    new SessionCommand(Guid.NewGuid(), 0, (SessionAction)999),
                    new SessionCommand(Guid.NewGuid(), 0, SessionAction.Resume, true),
                    new SessionCommand(Guid.NewGuid(), 0, SessionAction.SetScienceEnabled)
                };
                foreach (SessionCommand command in cases)
                {
                    ValidationReport local = PhaseOneScenarioValidator.ValidateCommand(command);
                    CommandResult result = probe.Send(command);
                    Require(!result.Accepted && local.Contains(result.Reason), "port " + result.Reason + " vs validator " + local);
                }
                Require(ReferenceEquals(probe.Current, before) && probe.Snapshots.Count == 0, "malformed command changed state");
                bool threw = false;
                try { probe.Port.Submit(null); } catch (ArgumentNullException) { threw = true; }
                Require(threw, "null command did not throw ArgumentNullException");
                return "InvalidCommandId, UnknownAction, InvalidActionValue x2; null throws";
            }));

            outcomes.Add(Run("ResultsCorrelateByIdAndEpoch", kind, () =>
            {
                var probe = new Probe(factory());
                probe.Send(SessionAction.Resume);
                probe.Send(SessionAction.Pause);
                probe.Send(SessionAction.Restart);
                probe.Send(new SessionCommand(Guid.NewGuid(), 0, SessionAction.Resume));
                for (int i = 0; i < probe.Sent.Count; i++)
                    RequireNoErrors(PhaseOneScenarioValidator.ValidateCommandResult(probe.Results[i], probe.Sent[i]), "result " + i);
                return probe.Sent.Count + " results correlated";
            }));

            return outcomes;
        }

        public static List<CheckOutcome> PendingIntegrationChecks()
        {
            const string kind = "real-pending";
            return new List<CheckOutcome>
            {
                // Balance, pause, objective, epochs and two-client consistency are now exercised by the
                // real-session and loopback checks; what remains needs pieces that do not exist yet.
                new CheckOutcome("PermissionPolicyPerRole", kind, CheckStatus.Pending, "No team policy for who may pause/restart/toggle science; bridge has connectionId but no roles."),
                new CheckOutcome("ConnectionFailureMessagesInHud", kind, CheckStatus.Pending, "Needs scene wiring (D2) of SessionConnectionController state into MissionHUD.SetConnectionStatus (D4)."),
                new CheckOutcome("MissionSceneClicksAndLegibility", kind, CheckStatus.Pending, "Needs the mission scene (D2); then manual run at target resolution with two players."),
                new CheckOutcome("RealBuildTwoInstances", kind, CheckStatus.Pending, "Needs mission scene, approved SDK binding and chosen topology; then BuildFromCommandLine and two running instances.")
            };
        }

        private static bool Throws(Action action)
        {
            try { action(); return false; }
            catch (ArgumentException) { return true; }
        }
    }

    /// <summary>
    /// Read-only audits of mission scenes, prefabs, runtime sources and test files. Scenes are opened
    /// additively and closed without saving; prefabs are loaded into an isolated stage and unloaded.
    /// Broken references are detected by resolving every serialized GUID through the AssetDatabase.
    /// </summary>
    public static class PhaseOneAssetAudit
    {
        public const string Kind = "static-audit";
        public const string InventoryKind = "test-inventory";
        public const string PrefabsFolder = "Assets/_Project/Prefabs";
        private static readonly System.Text.RegularExpressions.Regex GuidPattern =
            new System.Text.RegularExpressions.Regex(@"guid: ([0-9a-f]{32})");

        public static List<CheckOutcome> AuditMissionScenes(string[] explicitScenes)
        {
            var outcomes = new List<CheckOutcome>();
            string problem;
            string[] scenes = PhaseOneBuildChecks.ResolveMissionScenes(explicitScenes, out problem);
            if (scenes == null)
            {
                outcomes.Add(new CheckOutcome("MissionSceneAudit", Kind, CheckStatus.Pending, problem));
                return outcomes;
            }
            foreach (string path in scenes)
            {
                try { outcomes.AddRange(AuditScene(path)); }
                catch (Exception exception)
                {
                    outcomes.Add(new CheckOutcome("MissionSceneAudit " + path, Kind, CheckStatus.Failed,
                        exception.GetType().Name + ": " + exception.Message));
                }
            }
            return outcomes;
        }

        private static List<CheckOutcome> AuditScene(string path)
        {
            var outcomes = new List<CheckOutcome>();
            UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path);
            bool openedHere = !scene.IsValid() || !scene.isLoaded;
            if (openedHere)
                scene = UnityEditor.SceneManagement.EditorSceneManager.OpenScene(path, UnityEditor.SceneManagement.OpenSceneMode.Additive);
            try
            {
                var objects = new List<UnityEngine.GameObject>();
                foreach (UnityEngine.GameObject root in scene.GetRootGameObjects())
                    foreach (UnityEngine.Transform child in root.GetComponentsInChildren<UnityEngine.Transform>(true))
                        objects.Add(child.gameObject);
                string label = "Scene " + path;
                outcomes.Add(BrokenReferences(label, path, objects));

                var bootstraps = Components<Trainer.Unity.Bootstrap.MissionBootstrap>(objects);
                var runners = Components<Trainer.Unity.Bootstrap.SimulationRunner>(objects);
                var huds = Components<Trainer.Unity.Presentation.MissionHUD>(objects);
                var cameras = Components<UnityEngine.Camera>(objects);

                Trainer.Unity.Configuration.ScenarioDefinition scenario = null;
                string wiring;
                bool wired = false;
                if (bootstraps.Count != 1) wiring = bootstraps.Count + " MissionBootstrap (expected exactly 1 authority composition root)";
                else
                {
                    var serialized = new SerializedObject(bootstraps[0]);
                    SerializedProperty scenarioProperty = serialized.FindProperty("scenario");
                    SerializedProperty runnerProperty = serialized.FindProperty("runner");
                    scenario = scenarioProperty == null ? null
                        : scenarioProperty.objectReferenceValue as Trainer.Unity.Configuration.ScenarioDefinition;
                    bool hasRunner = runnerProperty != null && runnerProperty.objectReferenceValue != null;
                    wired = scenario != null && hasRunner;
                    wiring = "scenario " + (scenario != null ? AssetDatabase.GetAssetPath(scenario) : "UNASSIGNED")
                        + ", runner " + (hasRunner ? "assigned" : "UNASSIGNED");
                }
                outcomes.Add(new CheckOutcome("MissionBootstrapWired " + path, Kind, wired ? CheckStatus.Passed : CheckStatus.Failed, wiring));
                outcomes.Add(new CheckOutcome("SingleSimulationRunner " + path, Kind,
                    runners.Count == 1 ? CheckStatus.Passed : CheckStatus.Failed, runners.Count + " SimulationRunner"));
                outcomes.Add(new CheckOutcome("MissionHudInScene " + path, Kind,
                    huds.Count >= 1 ? CheckStatus.Passed : CheckStatus.Failed, huds.Count + " MissionHUD"));
                outcomes.Add(new CheckOutcome("CameraInScene " + path, Kind,
                    cameras.Count >= 1 ? CheckStatus.Passed : CheckStatus.Failed, cameras.Count + " Camera"));

                Type roomType = FindType("Trainer.Unity.World.PowerRoomView");
                if (roomType == null)
                    outcomes.Add(new CheckOutcome("PowerRoomInScene " + path, Kind, CheckStatus.Pending,
                        "PowerRoomView is not compiled in this checkout (D4 room visuals not integrated)."));
                else
                {
                    int rooms = objects.Sum(o => o.GetComponents(roomType).Length);
                    outcomes.Add(new CheckOutcome("PowerRoomInScene " + path, Kind,
                        rooms >= 1 ? CheckStatus.Passed : CheckStatus.Failed, rooms + " PowerRoomView"));
                }

                if (scenario != null)
                {
                    string problem = scenario.Validate();
                    if (problem != null)
                        outcomes.Add(new CheckOutcome("SceneScenarioValid " + path, Kind, CheckStatus.Failed, problem));
                    else
                    {
                        PowerConfiguration configuration = scenario.ToPowerConfiguration();
                        ValidationReport report = PhaseOneScenarioValidator.ValidateConfiguration(configuration);
                        outcomes.Add(new CheckOutcome("SceneScenarioValid " + path, Kind,
                            report.HasErrors ? CheckStatus.Failed : CheckStatus.Passed, report.ToString().Trim()));
                        outcomes.Add(PhaseOneIntegrationChecks.ReviewScenarioDecision("Scene " + path, configuration, scenario.ToMissionRules()));
                    }
                }
                return outcomes;
            }
            finally
            {
                if (openedHere && scene.IsValid())
                    UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
            }
        }

        public static List<CheckOutcome> AuditPrefabs()
        {
            var outcomes = new List<CheckOutcome>();
            if (!Directory.Exists(PrefabsFolder))
            {
                outcomes.Add(new CheckOutcome("PrefabAudit", Kind, CheckStatus.Pending, PrefabsFolder + " not found."));
                return outcomes;
            }
            string[] prefabs = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabsFolder })
                .Select(g => AssetDatabase.GUIDToAssetPath(g)).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray();
            if (prefabs.Length == 0)
                outcomes.Add(new CheckOutcome("PrefabAudit", Kind, CheckStatus.Pending, "No prefabs under " + PrefabsFolder + "."));
            foreach (string path in prefabs)
            {
                UnityEngine.GameObject root = null;
                try
                {
                    root = PrefabUtility.LoadPrefabContents(path);
                    var objects = root.GetComponentsInChildren<UnityEngine.Transform>(true).Select(t => t.gameObject).ToList();
                    outcomes.Add(BrokenReferences("Prefab " + path, path, objects));
                }
                catch (Exception exception)
                {
                    outcomes.Add(new CheckOutcome("Prefab " + path, Kind, CheckStatus.Failed, exception.GetType().Name + ": " + exception.Message));
                }
                finally
                {
                    if (root != null) PrefabUtility.UnloadPrefabContents(root);
                }
            }
            return outcomes;
        }

        /// <summary>FakeSessionPort must not appear in runtime code outside its own file and editor-only folders.</summary>
        public static CheckOutcome ScanProductionFakeUsage()
        {
            const string root = "Assets/_Project/Runtime";
            if (!Directory.Exists(root)) return new CheckOutcome("NoFakeInProductionCode", Kind, CheckStatus.Pending, root + " not found.");
            var offenders = new List<string>();
            foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string normalized = file.Replace('\\', '/');
                if (normalized.EndsWith("/Sessions/FakeSessionPort.cs", StringComparison.Ordinal)) continue;
                if (normalized.Contains("/Editor/")) continue;
                if (File.ReadAllText(file).Contains("FakeSessionPort")) offenders.Add(normalized);
            }
            return new CheckOutcome("NoFakeInProductionCode", Kind, offenders.Count == 0 ? CheckStatus.Passed : CheckStatus.Failed,
                offenders.Count == 0 ? "no runtime file references FakeSessionPort" : "referenced by: " + string.Join(", ", offenders));
        }

        /// <summary>Which test files Unity compiles today, and which only exercise the fake.</summary>
        public static List<CheckOutcome> InventoryDisabledTests()
        {
            const string root = "Assets/_Project/Tests";
            var outcomes = new List<CheckOutcome>();
            if (!Directory.Exists(root)) return outcomes;
            foreach (string file in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
            {
                string normalized = file.Replace('\\', '/');
                string text = File.ReadAllText(file);
                string guard = null;
                foreach (string line in text.Split('\n'))
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("#if ", StringComparison.Ordinal)) { guard = trimmed.Substring(4).Trim(); break; }
                    if (trimmed.StartsWith("namespace", StringComparison.Ordinal) || trimmed.StartsWith("public ", StringComparison.Ordinal)) break;
                }
                bool fake = text.Contains("FakeSessionPort");
                bool disabled = guard != null && guard.Contains("TRAINER_");
                string where = guard == null ? (normalized.Contains("/Editor/") ? "Assembly-CSharp-Editor, compiled" : "no guard")
                    : disabled ? "compiled out until define " + guard
                    : guard.Contains("UNITY_INCLUDE_TESTS") ? "compiled in the Editor (Test Runner)"
                    : "guard " + guard;
                outcomes.Add(new CheckOutcome("Tests " + normalized.Substring(root.Length + 1), InventoryKind,
                    disabled ? CheckStatus.Pending : CheckStatus.Info, where + (fake ? "; uses FakeSessionPort" : "")));
            }
            return outcomes;
        }

        private static CheckOutcome BrokenReferences(string label, string assetPath, List<UnityEngine.GameObject> objects)
        {
            int missingScripts = 0;
            foreach (UnityEngine.GameObject gameObject in objects)
                missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
            var unresolved = new List<string>();
            int references = 0;
            if (File.Exists(assetPath))
            {
                foreach (System.Text.RegularExpressions.Match match in GuidPattern.Matches(File.ReadAllText(assetPath)))
                {
                    string guid = match.Groups[1].Value;
                    references++;
                    if (string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid)) && !unresolved.Contains(guid)) unresolved.Add(guid);
                }
            }
            bool ok = missingScripts == 0 && unresolved.Count == 0;
            return new CheckOutcome(label, Kind, ok ? CheckStatus.Passed : CheckStatus.Failed,
                objects.Count + " objects, " + missingScripts + " missing scripts, " + references + " GUID references, "
                + unresolved.Count + " unresolved" + (unresolved.Count > 0 ? ": " + string.Join(", ", unresolved) : ""));
        }

        private static List<T> Components<T>(List<UnityEngine.GameObject> objects) where T : UnityEngine.Component
        {
            var found = new List<T>();
            foreach (UnityEngine.GameObject gameObject in objects) found.AddRange(gameObject.GetComponents<T>());
            return found;
        }

        private static Type FindType(string fullName)
        {
            foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null) return type;
            }
            return null;
        }
    }
}
