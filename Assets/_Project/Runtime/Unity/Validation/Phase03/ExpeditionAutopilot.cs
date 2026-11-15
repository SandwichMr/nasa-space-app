using System;
using System.Globalization;
using Trainer.Unity.Bootstrap;
using Trainer.Unity.Controls;
using UnityEngine;

namespace Trainer.Unity.Validation.Phase03
{
    /// <summary>
    /// D5 acceptance pilot for unattended two-process runs of the phase 3 BUILD. INERT unless the player is launched with
    /// <c>-trainer-autopilot A|B</c>; then it plays <c>-trainer-autopilot-role explorer|support</c> through the scene's own
    /// port and intents (the same ones the keyboard uses, which it disables so there is a single controller), logs every
    /// step as <c>[Autopilot] …</c>, ends with <c>[Autopilot] RESULT PASS|FAIL …</c> and quits with exit code 0 or 1.
    /// Optional: <c>-trainer-autopilot-crew N</c> (default 2), <c>-trainer-autopilot-timeout SECONDS</c> (default 900).
    /// It sends only player intents: no positions, no results, no authority shortcuts.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ExpeditionAutopilot : MonoBehaviour
    {
        [SerializeField] private ExpeditionSceneComposition composition;
        [SerializeField] private CrewKeyboardInput keyboard;
        [SerializeField] private ExpeditionInteractionInput interaction;

        private bool active;
        private AcceptanceFlow flow;
        private AcceptanceRole role;
        private int crew = 2;
        private double timeout = 900;
        private double elapsed;
        private ExpeditionAcceptanceScript script;
        private int logged;
        private double quitAt = -1;

        public bool IsActive => active;

        private void Awake()
        {
            string[] args = Environment.GetCommandLineArgs();
            string flowArg = Value(args, "-trainer-autopilot");
            if (flowArg == null) return; // inert in normal play
            active = Enum.TryParse(flowArg, true, out flow);
            if (!active)
            {
                Debug.LogError($"[Autopilot] Unknown flow '{flowArg}' (use A or B).");
                return;
            }
            role = string.Equals(Value(args, "-trainer-autopilot-role"), "support", StringComparison.OrdinalIgnoreCase)
                ? AcceptanceRole.Support : AcceptanceRole.Explorer;
            if (int.TryParse(Value(args, "-trainer-autopilot-crew"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n > 0) crew = n;
            if (double.TryParse(Value(args, "-trainer-autopilot-timeout"), NumberStyles.Float, CultureInfo.InvariantCulture, out double t) && t > 0) timeout = t;
            Debug.Log($"[Autopilot] Active: flow {flow}, role {role}, crew {crew}, timeout {timeout:0} s.");
        }

        private void Start()
        {
            if (!active) return;
            if (keyboard != null) keyboard.enabled = false; // one controller: the pilot replaces the keyboard
            if (interaction != null) interaction.enabled = false;
        }

        private void Update()
        {
            if (!active) return;
            if (quitAt >= 0)
            {
                if (Time.unscaledTime >= quitAt) UnityEngine.Application.Quit(script != null && script.Passed ? 0 : 1);
                return;
            }
            elapsed += Time.unscaledDeltaTime;
            if (script == null && composition != null && composition.PresentedPort != null && composition.Intents != null
                && composition.Scenario != null && composition.PresentedPort.Membership.IsJoined)
                script = new ExpeditionAcceptanceScript(composition.PresentedPort, composition.Intents, composition.Scenario, flow, role, crew);
            if (script != null)
            {
                script.Update(Time.unscaledDeltaTime);
                for (; logged < script.Log.Count; logged++) Debug.Log("[Autopilot] " + script.Log[logged]);
                if (script.Finished) End(script.Passed, script.Verdict);
            }
            if (quitAt < 0 && elapsed > timeout)
                End(false, $"timeout after {timeout:0} s at step '{script?.CurrentStep ?? "join"}'");
        }

        private void End(bool passed, string verdict)
        {
            Debug.Log($"[Autopilot] RESULT {(passed ? "PASS" : "FAIL")} flow={flow} role={role} {verdict}");
            quitAt = Time.unscaledTime + 3f; // let the last snapshot and logs flush
        }

        private static string Value(string[] args, string name)
        {
            for (int i = 0; i + 1 < args.Length; i++)
                if (string.Equals(args[i], name, StringComparison.Ordinal)) return args[i + 1];
            return null;
        }
    }
}
