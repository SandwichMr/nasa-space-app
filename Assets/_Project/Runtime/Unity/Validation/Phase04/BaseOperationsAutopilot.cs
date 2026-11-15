using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Trainer.Application.BaseOperations;
using Trainer.Unity.Controls;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Trainer.Unity.Validation.Phase04
{
    /// <summary>
    /// D5 phase 4 acceptance pilot for unattended runs of the BUILD. INERT unless the player is launched with
    /// <c>-trainer-autopilot continuity</c>. Then it plays <c>-trainer-autopilot-role water|air</c> for
    /// <c>-trainer-autopilot-minutes M</c> REAL minutes (default 30) through the scene's own port and D3 intents, disabling
    /// the keyboard so there is a single controller. It logs every step as <c>[Autopilot] …</c>, real and simulated time
    /// separately, ends with <c>[Autopilot] RESULT PASS|FAIL …</c> and quits with exit code 0 or 1.
    /// <c>-trainer-autopilot-timeout SECONDS</c> (default: duration + 600) aborts a run that never joins or never ends.
    ///
    /// It finds the port, intents and scenario on the scene composition by their CONTRACT types (any component exposing a
    /// non-null <see cref="IBaseOperationsSessionPort"/>, <see cref="IBaseOperationsIntents"/> and
    /// <see cref="BaseOperationsScenario"/> property), so it does not depend on D2's class names. It sends only player
    /// intents: no positions, no stocks, no results, no authority shortcuts. A PASS is evidence of continuity, never of a
    /// session played by people.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BaseOperationsAutopilot : MonoBehaviour
    {
        public const string FlowArgument = "-trainer-autopilot";
        public const string FlowName = "continuity";

        private bool active;
        private ContinuityRole role;
        private double minutes = 30;
        private double timeout = -1;
        private double elapsed;
        private double quitAt = -1;
        private int logged;
        private float nextStatus;
        private BaseOperationsContinuityScript script;
        private PortIntentsActor actor;
        private string verdict;

        public bool IsActive => active;

        private void Awake()
        {
            string[] args = Environment.GetCommandLineArgs();
            string flow = Value(args, FlowArgument);
            if (flow == null) return; // inert in normal play
            active = string.Equals(flow, FlowName, StringComparison.OrdinalIgnoreCase);
            if (!active)
            {
                // Phase 3 flows (A/B) belong to the phase 3 pilot; this one stays inert for them.
                Debug.Log($"[Autopilot] Phase 4 pilot inert for flow '{flow}' (use '{FlowName}').");
                return;
            }
            role = string.Equals(Value(args, "-trainer-autopilot-role"), "air", StringComparison.OrdinalIgnoreCase)
                ? ContinuityRole.AirAndLogistics : ContinuityRole.Water;
            if (double.TryParse(Value(args, "-trainer-autopilot-minutes"), NumberStyles.Float, CultureInfo.InvariantCulture, out double m) && m > 0)
                minutes = m;
            if (double.TryParse(Value(args, "-trainer-autopilot-timeout"), NumberStyles.Float, CultureInfo.InvariantCulture, out double t) && t > 0)
                timeout = t;
            if (timeout < 0) timeout = minutes * 60 + 600;
            Debug.Log($"[Autopilot] Active: flow {FlowName}, role {role}, duration {minutes:0.##} real min, timeout {timeout:0} s.");
        }

        private void Start()
        {
            if (!active) return;
            // One controller: the pilot replaces the keyboard (phase 2 movement and any interaction input component).
            foreach (MonoBehaviour behaviour in SceneBehaviours())
                if (behaviour is CrewKeyboardInput || behaviour.GetType().Name.EndsWith("InteractionInput", StringComparison.Ordinal))
                    behaviour.enabled = false;
        }

        private void Update()
        {
            if (!active) return;
            if (quitAt >= 0)
            {
                if (Time.unscaledTime >= quitAt) UnityEngine.Application.Quit(script != null && script.Passed && verdict == null ? 0 : 1);
                return;
            }
            elapsed += Time.unscaledDeltaTime;
            if (script == null && TryFindSession(out IBaseOperationsSessionPort port, out IBaseOperationsIntents intents, out BaseOperationsScenario scenario)
                && port.Membership.IsJoined)
            {
                actor = new PortIntentsActor(port, intents);
                script = new BaseOperationsContinuityScript(actor, scenario, role, minutes * 60);
                Debug.Log($"[Autopilot] Bound to {port.GetType().Name} / {intents.GetType().Name}, scenario '{scenario.Id}'.");
            }
            if (script != null)
            {
                script.Update(Time.unscaledDeltaTime);
                for (; logged < script.Log.Count; logged++) Debug.Log("[Autopilot] " + script.Log[logged]);
                if (Time.unscaledTime >= nextStatus)
                {
                    nextStatus = Time.unscaledTime + 60f;
                    Debug.Log("[Autopilot] status real=" + elapsed.ToString("0.0", CultureInfo.InvariantCulture) + "s activity='"
                              + script.Activity + "' " + script.Metrics.Summary());
                }
                if (script.Finished) End(script.Passed, script.Verdict);
            }
            if (quitAt < 0 && elapsed > timeout)
            {
                verdict = $"timeout after {timeout:0} s ({(script == null ? "never joined or no phase 4 port/intents in the scene" : script.Activity)})";
                End(false, verdict);
            }
        }

        private void End(bool passed, string text)
        {
            Debug.Log($"[Autopilot] RESULT {(passed ? "PASS" : "FAIL")} flow={FlowName} role={role} real={elapsed.ToString("0.0", CultureInfo.InvariantCulture)}s {text}");
            quitAt = Time.unscaledTime + 3f; // let the last snapshot and logs flush
        }

        private void OnDestroy() => actor?.Dispose();

        /// <summary>First component in the loaded scenes exposing the port, the intents and the scenario (all non-null).</summary>
        public static bool TryFindSession(out IBaseOperationsSessionPort port, out IBaseOperationsIntents intents, out BaseOperationsScenario scenario)
        {
            port = null;
            intents = null;
            scenario = null;
            foreach (MonoBehaviour behaviour in SceneBehaviours())
            {
                if (behaviour == null || !behaviour.isActiveAndEnabled) continue;
                IBaseOperationsSessionPort p = null;
                IBaseOperationsIntents i = null;
                BaseOperationsScenario s = null;
                foreach (PropertyInfo property in behaviour.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (property.GetIndexParameters().Length != 0 || !property.CanRead) continue;
                    Type type = property.PropertyType;
                    if (type != typeof(IBaseOperationsSessionPort) && type != typeof(IBaseOperationsIntents) && type != typeof(BaseOperationsScenario)) continue;
                    object value;
                    try { value = property.GetValue(behaviour); }
                    catch (Exception) { continue; }
                    if (value is IBaseOperationsSessionPort vp && p == null) p = vp;
                    else if (value is IBaseOperationsIntents vi && i == null) i = vi;
                    else if (value is BaseOperationsScenario vs && s == null) s = vs;
                }
                if (p != null && i != null && s != null)
                {
                    port = p;
                    intents = i;
                    scenario = s;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Every MonoBehaviour (active or not) under the roots of the loaded scenes.</summary>
        public static List<MonoBehaviour> SceneBehaviours()
        {
            var found = new List<MonoBehaviour>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                    found.AddRange(root.GetComponentsInChildren<MonoBehaviour>(true));
            }
            return found;
        }

        private static string Value(string[] args, string name)
        {
            for (int i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], name, StringComparison.Ordinal)) return args[i + 1];
            return null;
        }
    }
}
