using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Trainer.Tooling.Validation.Phase04
{
    public enum Phase04CheckStatus { Passed, Failed, Pending, Info }

    /// <summary>
    /// Kinds of phase 4 evidence. They are reported separately and NEVER added together: a fixture or fake run is not a
    /// conformance run of a real implementation, an in-memory authority with two identities is not two clients over a
    /// network, an in-memory port is not a socket, nographics is not render or a human keyboard, and an automated pilot is
    /// not a session played by people.
    /// </summary>
    public static class Phase04Kinds
    {
        /// <summary>D5 tooling exercised on BaseOperationsFixtures / FakeBaseOperationsSessionPort (synthetic, labelled).</summary>
        public const string FixtureFake = "fixture-fake";
        public const string Discovery = "discovery";
        public const string ScenarioValidator = "validator-scenario";
        public const string EquipmentConformance = "conformance-equipment-D1";
        public const string AirCoupling = "conformance-air-coupling-D1";
        /// <summary>The REAL authority in memory, two identities from Join, no network and no port adapters.</summary>
        public const string AuthorityInMemory = "authority-in-memory-D2";
        /// <summary>The REAL authority through D3's local ports and intents, in memory (no sockets).</summary>
        public const string PortsInMemory = "ports-in-memory-D3";
        public const string SceneAudit = "scene-audit-D2";
        public const string Regression = "regression-phase2-3";
        public const string Build = "build";
        /// <summary>Authority + two client processes of the build (sockets on one machine).</summary>
        public const string BuildRun = "build-two-process";
        /// <summary>Thirty or more real minutes played by two people. Never produced by tooling.</summary>
        public const string HumanSession = "human-session";
    }

    public sealed class Phase04Check
    {
        public string Name { get; }
        public string Kind { get; }
        public Phase04CheckStatus Status { get; }
        public string Detail { get; }

        public Phase04Check(string name, string kind, Phase04CheckStatus status, string detail)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name required.", nameof(name));
            if (string.IsNullOrWhiteSpace(kind)) throw new ArgumentException("Kind required.", nameof(kind));
            Name = name;
            Kind = kind;
            Status = status;
            Detail = detail ?? string.Empty;
        }

        public override string ToString() =>
            "[" + Status + "] (" + Kind + ") " + Name + (Detail.Length > 0 ? " - " + Detail : string.Empty);
    }

    /// <summary>Thrown by a check body to report a contract violation (Failed).</summary>
    public sealed class Phase04CheckFailed : Exception
    {
        public Phase04CheckFailed(string message) : base(message) { }
    }

    /// <summary>Thrown by a check body that cannot run here (missing implementation or hook). Never a pass.</summary>
    public sealed class Phase04CheckPending : Exception
    {
        public Phase04CheckPending(string message) : base(message) { }
    }

    public static class Phase04Checks
    {
        /// <summary>Exit codes of the command-line gates.</summary>
        public const int ExitPassed = 0, ExitFailed = 1, ExitPending = 2;

        /// <summary>Runs one body: normal return = Passed (detail from the return value); Phase04CheckFailed = Failed;
        /// Phase04CheckPending = Pending; any other exception = Failed with its type (never a silent pass).</summary>
        public static Phase04Check Run(string name, string kind, Func<string> body)
        {
            try
            {
                return new Phase04Check(name, kind, Phase04CheckStatus.Passed, body());
            }
            catch (Phase04CheckFailed failed)
            {
                return new Phase04Check(name, kind, Phase04CheckStatus.Failed, failed.Message);
            }
            catch (Phase04CheckPending pending)
            {
                return new Phase04Check(name, kind, Phase04CheckStatus.Pending, pending.Message);
            }
            catch (Exception exception)
            {
                return new Phase04Check(name, kind, Phase04CheckStatus.Failed, "Unexpected " + exception.GetType().Name + ": " + exception.Message);
            }
        }

        public static Phase04Check Run(string name, string kind, Action body) => Run(name, kind, () => { body(); return string.Empty; });

        public static Phase04Check Pending(string name, string kind, string why) => new Phase04Check(name, kind, Phase04CheckStatus.Pending, why);
        public static Phase04Check Info(string name, string kind, string detail) => new Phase04Check(name, kind, Phase04CheckStatus.Info, detail);
        public static Phase04Check Result(string name, string kind, bool passed, string detail) =>
            new Phase04Check(name, kind, passed ? Phase04CheckStatus.Passed : Phase04CheckStatus.Failed, detail);

        public static void Require(bool condition, string message)
        {
            if (!condition) throw new Phase04CheckFailed(message);
        }

        public static void Near(double expected, double actual, double tolerance, string what)
        {
            if (double.IsNaN(actual) || Math.Abs(expected - actual) > tolerance)
                throw new Phase04CheckFailed(what + ": expected " + expected.ToString("R") + " ± " + tolerance.ToString("R") + ", got " + actual.ToString("R"));
        }

        /// <summary>Failed if any check failed; Pending if none failed but some are pending; Passed otherwise.</summary>
        public static int ExitCode(IEnumerable<Phase04Check> checks)
        {
            List<Phase04Check> list = checks.ToList();
            if (list.Any(c => c.Status == Phase04CheckStatus.Failed)) return ExitFailed;
            if (list.Any(c => c.Status == Phase04CheckStatus.Pending)) return ExitPending;
            return ExitPassed;
        }

        public static string Format(IEnumerable<Phase04Check> checks)
        {
            List<Phase04Check> list = checks.ToList();
            var text = new StringBuilder();
            text.Append("== Phase 4 D5: ");
            text.Append(string.Join(", ", Enum.GetValues(typeof(Phase04CheckStatus)).Cast<Phase04CheckStatus>()
                .Select(s => s + " " + list.Count(c => c.Status == s))));
            text.AppendLine(" (kinds are separate evidence; they are not added up)");
            foreach (IGrouping<string, Phase04Check> group in list.GroupBy(c => c.Kind))
            {
                text.Append("-- ").Append(group.Key).Append(": ");
                text.Append(string.Join(", ", Enum.GetValues(typeof(Phase04CheckStatus)).Cast<Phase04CheckStatus>()
                    .Select(s => s + " " + group.Count(c => c.Status == s))));
                text.AppendLine();
                foreach (Phase04Check check in group) text.AppendLine("  " + check);
            }
            return text.ToString();
        }
    }
}
