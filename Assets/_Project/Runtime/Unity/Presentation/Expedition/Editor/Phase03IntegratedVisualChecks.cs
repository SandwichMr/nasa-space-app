using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Trainer.Application;
using Trainer.Application.Crew;
using Trainer.Application.Expedition;
using Trainer.Tooling.Composition.Phase03;
using Trainer.Unity.Bootstrap;
using Trainer.Unity.Controls;
using Trainer.Unity.Networking.Expedition;
using Trainer.Unity.Networking.Outpost;
using Trainer.Unity.Validation.Phase03;
using Trainer.Unity.World.Expedition;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Trainer.Unity.Presentation.Expedition.Editor
{
    /// <summary>Runs D5's two pilots against the real production scene and two host-local ports in PlayMode.
    /// No fake, alternate scene, camera, positions, resource changes or extra simulation clock. Captures the actual
    /// D4 views bound by D2 and avatars driven by D3. Cross-process networking is verified separately on the build.</summary>
    public static class Phase03IntegratedVisualChecks
    {
        private const string Key = "Trainer.Phase03.IntegratedVisualChecks";
        private static readonly HashSet<string> captures = new HashSet<string>();
        private static ExpeditionSceneComposition composition;
        private static ExpeditionPanel panel;
        private static ExpeditionWorldView world;
        private static CrewAvatarSync avatars;
        private static LocalExpeditionSessionPort support;
        private static ExpeditionInteractionClient supportIntents;
        private static ExpeditionAcceptanceScript explorerScript, supportScript;
        private static double started;
        private static int frame, finishFrame = -1;
        private static bool ending;
        private static string Evidence => Path.GetFullPath("docs/phase-03/integration-evidence");

        [MenuItem("Tools/Trainer/Phase 3/Run integrated production-scene visual checks")]
        public static void Run()
        {
            EditorSceneManager.OpenScene(Phase03SceneBuilder.ScenePath, OpenSceneMode.Single);
            UnityEditor.SessionState.SetBool(Key, true);
            EditorApplication.EnterPlaymode();
        }
        [InitializeOnLoadMethod] private static void Register() => EditorApplication.playModeStateChanged += OnMode;
        private static void OnMode(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredPlayMode || !UnityEditor.SessionState.GetBool(Key, false)) return;
            UnityEditor.SessionState.SetBool(Key, false);
            captures.Clear(); ending = false; finishFrame = -1; explorerScript = supportScript = null;
            started = EditorApplication.timeSinceStartup;
            frame = -1;
            Directory.CreateDirectory(Evidence);
            if (!UnityEngine.Application.isBatchMode)
            {
                var type = Type.GetType("UnityEditor.GameView,UnityEditor");
                var window = (EditorWindow)ScriptableObject.CreateInstance(type);
                window.ShowUtility(); window.position = new Rect(20, 20, 1440, 920); window.Focus();
            }
            UnityEngine.Application.targetFrameRate = 60;
            UnityEngine.Application.logMessageReceived += OnLog;
            EditorApplication.update += Tick;
        }
        private static void OnLog(string message, string trace, LogType type)
        {
            if (ending || (type != LogType.Error && type != LogType.Exception && type != LogType.Assert)) return;
            if (message.StartsWith("ArgumentOutOfRangeException:") && trace.Contains("UnityEditor.Search.SearchDatabase")) return;
            End(false, message + "\n" + trace);
        }
        private static void Tick()
        {
            if (frame == Time.frameCount || ending) return;
            frame = Time.frameCount;
            try
            {
                if (EditorApplication.timeSinceStartup - started > 300) throw new Exception("Integrated visual check timeout");
                if (explorerScript == null)
                {
                    composition = UnityEngine.Object.FindFirstObjectByType<ExpeditionSceneComposition>();
                    if (composition == null || composition.Authority == null || composition.PresentedPort == null
                        || !composition.PresentedPort.Membership.IsJoined) return;
                    panel = UnityEngine.Object.FindFirstObjectByType<ExpeditionPanel>();
                    world = UnityEngine.Object.FindFirstObjectByType<ExpeditionWorldView>();
                    avatars = UnityEngine.Object.FindFirstObjectByType<CrewAvatarSync>();
                    Require(panel != null && world != null && panel.Live && world.Live, "D4 production views bound by composition");
                    UnityEngine.Object.FindFirstObjectByType<CrewKeyboardInput>().enabled = false;
                    UnityEngine.Object.FindFirstObjectByType<ExpeditionInteractionInput>().enabled = false;
                    // Same runner/authority. Only the isolated test's pacing changes; no client ticks a simulation.
                    UnityEngine.Object.FindFirstObjectByType<ExpeditionRunner>().Bind(composition.Authority, 3);
                    support = new LocalExpeditionSessionPort(composition.Authority); support.Join();
                    supportIntents = new ExpeditionInteractionClient(support, composition.Scenario);
                    explorerScript = new ExpeditionAcceptanceScript(composition.PresentedPort, composition.Intents,
                        composition.Scenario, AcceptanceFlow.B, AcceptanceRole.Explorer);
                    supportScript = new ExpeditionAcceptanceScript(support, supportIntents,
                        composition.Scenario, AcceptanceFlow.B, AcceptanceRole.Support);
                    Require(avatars.AvatarCount == 2, "two D3 avatars with D4 phase3 prefab");
                    Capture("01-real-base.png");
                }
                if (finishFrame >= 0)
                {
                    if (frame >= finishFrame) End(true, explorerScript.Verdict + " | " + supportScript.Verdict);
                    return;
                }
                var s = panel.Shown;
                Require(s != null && s.Outpost.ScenarioId == composition.Scenario.Id, "same production scenario");
                s.TryGetCrew(composition.PresentedPort.Membership.Player, out var me);
                if (s.Airlock.IsCycling) Capture("02-real-airlock.png");
                if (me.Zone == CrewZone.Exterior) Capture("03-real-exterior.png");
                if (s.Outpost.Stations.Any(x => x.ActiveTask != null && x.Station.Value == "regolith-site")) Capture("04-real-science.png");
                if (panel.AdvisoryText.Contains("Recibido")) Capture("05-real-advisory.png");
                if (me.Protection == ProtectionState.Protected) Capture("06-real-shelter.png");
                explorerScript.Update(Time.unscaledDeltaTime); supportScript.Update(Time.unscaledDeltaTime);
                if (explorerScript.Finished && supportScript.Finished)
                {
                    Require(explorerScript.Passed && supportScript.Passed, explorerScript.Verdict + " | " + supportScript.Verdict);
                    Capture("07-real-result.png");
                    finishFrame = frame + 20;
                }
            }
            catch (Exception e) { End(false, e.ToString()); }
        }
        private static void Require(bool value, string message) { if (!value) throw new Exception(message); }
        private static void Capture(string name)
        {
            if (!captures.Add(name)) return;
            ScreenCapture.CaptureScreenshot(Path.Combine(Evidence, name));
            Debug.Log("[IntegratedVisualChecks] Capture " + name);
        }
        private static void End(bool passed, string detail)
        {
            if (ending) return; ending = true;
            EditorApplication.update -= Tick; UnityEngine.Application.logMessageReceived -= OnLog;
            var report = (passed ? "PASS" : "FAIL") + ": production scene, real authority, two local ports (no network)\n" + detail + "\n";
            if (explorerScript != null) report += string.Join("\n", explorerScript.Log) + "\n";
            if (supportScript != null) report += string.Join("\n", supportScript.Log) + "\n";
            File.WriteAllText(Path.Combine(Evidence, "production-scene-visual.txt"), report);
            Debug.Log("[IntegratedVisualChecks] " + report);
            supportIntents?.Dispose(); support?.Dispose(); supportIntents = null; support = null;
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-phase3-ci") >= 0) EditorApplication.Exit(passed ? 0 : 1);
            else EditorApplication.ExitPlaymode();
        }
    }
}
