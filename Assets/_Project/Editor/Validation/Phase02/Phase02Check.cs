using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Trainer.Tooling.Validation.Phase02
{
    public enum Phase02CheckStatus { Passed, Failed, Pending, Info }

    /// <summary>
    /// Kinds of evidence. They are reported separately and never added together: a validator on synthetic
    /// data is not a conformance run, a conformance run is not a scene audit, and none of them is a build
    /// played by two people.
    /// </summary>
    public static class Phase02CheckKinds
    {
        public const string Validator = "validator";
        public const string RosterConformance = "conformance-roster";
        public const string SimulationConformance = "conformance-simulation";
        public const string AuthorityConformance = "conformance-authority";
        public const string TwoPortSession = "session-two-ports";
        public const string Discovery = "discovery";
        public const string SceneAudit = "scene-audit";
        public const string Environment = "environment";
        public const string PhaseOneRegression = "phase1-regression";
        public const string TwoPlayerBuild = "two-player-build";
    }

    public sealed class Phase02Check
    {
        public string Name { get; }
        public string Kind { get; }
        public Phase02CheckStatus Status { get; }
        public string Detail { get; }

        public Phase02Check(string name, string kind, Phase02CheckStatus status, string detail)
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

    /// <summary>Thrown by a check body to report a contract violation (Failed), as opposed to Pending.</summary>
    public sealed class Phase02CheckFailed : Exception
    {
        public Phase02CheckFailed(string message) : base(message) { }
    }

    /// <summary>Thrown by a check body when it cannot run here (missing implementation, harness hook).</summary>
    public sealed class Phase02CheckPending : Exception
    {
        public Phase02CheckPending(string message) : base(message) { }
    }

    public static class Phase02Checks
    {
        /// <summary>Runs one check body. A body that returns normally passes; detail may be filled through the
        /// return value. Unexpected exceptions are failures with the exception type, never silent passes.</summary>
        public static Phase02Check Check(string name, string kind, Func<string> body)
        {
            try
            {
                string detail = body();
                return new Phase02Check(name, kind, Phase02CheckStatus.Passed, detail);
            }
            catch (Phase02CheckFailed failed)
            {
                return new Phase02Check(name, kind, Phase02CheckStatus.Failed, failed.Message);
            }
            catch (Phase02CheckPending pending)
            {
                return new Phase02Check(name, kind, Phase02CheckStatus.Pending, pending.Message);
            }
            catch (Exception exception)
            {
                return new Phase02Check(name, kind, Phase02CheckStatus.Failed,
                    "Unexpected " + exception.GetType().Name + ": " + exception.Message);
            }
        }

        public static Phase02Check Check(string name, string kind, Action body) =>
            Check(name, kind, () => { body(); return string.Empty; });

        public static void Require(bool condition, string message)
        {
            if (!condition) throw new Phase02CheckFailed(message);
        }

        public static void Near(double expected, double actual, double tolerance, string what)
        {
            if (double.IsNaN(actual) || Math.Abs(expected - actual) > tolerance)
                throw new Phase02CheckFailed(what + ": expected " + expected.ToString("R") + " ± " + tolerance.ToString("R")
                    + ", got " + actual.ToString("R"));
        }

        public static string Format(IEnumerable<Phase02Check> checks)
        {
            List<Phase02Check> list = checks.ToList();
            var text = new StringBuilder();
            foreach (IGrouping<string, Phase02Check> group in list.GroupBy(c => c.Kind))
            {
                text.Append("== ").Append(group.Key).Append(": ");
                text.Append(string.Join(", ", Enum.GetValues(typeof(Phase02CheckStatus)).Cast<Phase02CheckStatus>()
                    .Select(s => s + " " + group.Count(c => c.Status == s))));
                text.AppendLine();
                foreach (Phase02Check check in group) text.AppendLine("  " + check);
            }
            return text.ToString();
        }
    }
}
