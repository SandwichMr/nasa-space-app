using System;
using System.Collections.Generic;
using System.Text;
using Trainer.Application;
using Trainer.Simulation;

namespace Trainer.Tooling.Validation
{
    public enum ValidationSeverity { Warning, Error }

    /// <summary>One finding. Errors contradict the frozen phase 1 contract or physical accounting;
    /// warnings are diagnostics (possible unit mistakes, unservable demand) and never product rules.</summary>
    public sealed class ValidationIssue
    {
        public ValidationSeverity Severity { get; }
        public string Code { get; }
        public string Message { get; }

        public ValidationIssue(ValidationSeverity severity, string code, string message)
        {
            Severity = severity;
            Code = code ?? string.Empty;
            Message = message ?? string.Empty;
        }

        public override string ToString() { return Severity + " " + Code + ": " + Message; }
    }

    public sealed class ValidationReport
    {
        private readonly List<ValidationIssue> issues = new List<ValidationIssue>();

        public IReadOnlyList<ValidationIssue> Issues { get { return issues; } }

        public bool HasErrors
        {
            get
            {
                foreach (ValidationIssue issue in issues)
                    if (issue.Severity == ValidationSeverity.Error) return true;
                return false;
            }
        }

        public bool Contains(string code)
        {
            foreach (ValidationIssue issue in issues)
                if (issue.Code == code) return true;
            return false;
        }

        public void Error(string code, string message) { issues.Add(new ValidationIssue(ValidationSeverity.Error, code, message)); }
        public void Warning(string code, string message) { issues.Add(new ValidationIssue(ValidationSeverity.Warning, code, message)); }

        public void Merge(ValidationReport other)
        {
            if (other == null) return;
            issues.AddRange(other.issues);
        }

        public override string ToString()
        {
            if (issues.Count == 0) return "OK";
            var text = new StringBuilder();
            foreach (ValidationIssue issue in issues) text.AppendLine(issue.ToString());
            return text.ToString();
        }
    }

    /// <summary>
    /// Phase 1 validator over the frozen contracts only (PowerConfiguration, PowerSnapshot, MissionSnapshot,
    /// SessionCommand, CommandResult). It never implements or imitates IPowerSimulation: transition checks
    /// use conservative bounds that any lossless model with constant configuration must respect.
    /// Units: joules (J), watts (W), seconds (s).
    /// </summary>
    public static class PhaseOneScenarioValidator
    {
        /// <summary>2^53: above this a double cannot represent every integer joule/watt exactly.</summary>
        public const double MaxExactDouble = 9007199254740992.0;
        public const double DefaultToleranceJoules = 1e-6;
        public const double DefaultToleranceWatts = 1e-9;
        public const double DefaultToleranceSeconds = 1e-9;

        // ---------------------------------------------------------------- configuration

        public static ValidationReport ValidateConfiguration(PowerConfiguration configuration)
        {
            var report = new ValidationReport();
            if (configuration == null) { report.Error("ConfigurationNull", "PowerConfiguration is null."); return report; }

            Magnitude(report, "InitialEnergyJoules", configuration.InitialEnergyJoules, "J");
            Magnitude(report, "CapacityJoules", configuration.CapacityJoules, "J");
            Magnitude(report, "GenerationWatts", configuration.GenerationWatts, "W");
            Magnitude(report, "EssentialConsumptionWatts", configuration.EssentialConsumptionWatts, "W");
            Magnitude(report, "ScienceConsumptionWatts", configuration.ScienceConsumptionWatts, "W");
            Magnitude(report, "MaxChargeWatts", configuration.MaxChargeWatts, "W");
            Magnitude(report, "MaxDischargeWatts", configuration.MaxDischargeWatts, "W");
            if (report.HasErrors) return report;

            if (configuration.InitialEnergyJoules > configuration.CapacityJoules)
                report.Error("InitialExceedsCapacity", "InitialEnergyJoules is greater than CapacityJoules.");

            double load = configuration.EssentialConsumptionWatts + configuration.ScienceConsumptionWatts;
            double supply = configuration.GenerationWatts + configuration.MaxDischargeWatts;

            if (configuration.CapacityJoules == 0)
                report.Warning("ZeroCapacity", "No storage: demand can only be served from instantaneous generation.");
            else
            {
                double largestPower = Math.Max(load, Math.Max(configuration.MaxDischargeWatts, configuration.MaxChargeWatts));
                if (largestPower > 0 && configuration.CapacityJoules < largestPower * 1.0)
                    report.Warning("PossibleUnitMismatch",
                        "Capacity is below one second of the largest power (" + configuration.CapacityJoules + " J < "
                        + largestPower + " W x 1 s). Check that capacity is in joules (1 Wh = 3600 J), not Wh or kWh.");
            }

            if (configuration.MaxChargeWatts == 0 && configuration.GenerationWatts > 0
                && configuration.CapacityJoules > configuration.InitialEnergyJoules)
                report.Warning("ChargingDisabled", "MaxChargeWatts is zero: surplus generation can never be stored.");
            if (configuration.MaxDischargeWatts == 0 && configuration.InitialEnergyJoules > 0)
                report.Warning("DischargeDisabled", "MaxDischargeWatts is zero: stored energy can never serve demand.");

            if (configuration.EssentialConsumptionWatts > supply)
                report.Warning("EssentialNotFullyServable",
                    "Essential demand exceeds generation + max discharge even with a charged battery ("
                    + configuration.EssentialConsumptionWatts + " W > " + supply + " W).");
            else if (load > supply)
                report.Warning("ScienceNotFullyServable",
                    "Essential + science demand exceeds generation + max discharge (" + load + " W > " + supply + " W).");

            return report;
        }

        // ---------------------------------------------------------------- power snapshots

        /// <summary>Static checks of one observation. Configuration is optional; when given, the snapshot
        /// is compared with it (phase 1 configuration is constant during an epoch).</summary>
        public static ValidationReport ValidatePowerSnapshot(PowerSnapshot snapshot, PowerConfiguration configuration = null,
            double toleranceJoules = DefaultToleranceJoules, double toleranceWatts = DefaultToleranceWatts)
        {
            var report = new ValidationReport();
            if (snapshot == null) { report.Error("PowerSnapshotNull", "PowerSnapshot is null."); return report; }

            Magnitude(report, "SimulatedTimeSeconds", snapshot.SimulatedTimeSeconds, "s");
            Magnitude(report, "EnergyJoules", snapshot.EnergyJoules, "J");
            Magnitude(report, "CapacityJoules", snapshot.CapacityJoules, "J");
            Magnitude(report, "GenerationWatts", snapshot.GenerationWatts, "W");
            Magnitude(report, "EssentialRequestedWatts", snapshot.EssentialRequestedWatts, "W");
            Magnitude(report, "EssentialServedWatts", snapshot.EssentialServedWatts, "W");
            Magnitude(report, "ScienceRequestedWatts", snapshot.ScienceRequestedWatts, "W");
            Magnitude(report, "ScienceServedWatts", snapshot.ScienceServedWatts, "W");
            if (report.HasErrors) return report;

            if (snapshot.EnergyJoules > snapshot.CapacityJoules + toleranceJoules)
                report.Error("EnergyAboveCapacity", "EnergyJoules " + snapshot.EnergyJoules + " > CapacityJoules " + snapshot.CapacityJoules + ".");
            if (snapshot.EssentialServedWatts > snapshot.EssentialRequestedWatts + toleranceWatts)
                report.Error("ServedExceedsRequested", "Essential served " + snapshot.EssentialServedWatts + " W > requested " + snapshot.EssentialRequestedWatts + " W.");
            if (snapshot.ScienceServedWatts > snapshot.ScienceRequestedWatts + toleranceWatts)
                report.Error("ServedExceedsRequested", "Science served " + snapshot.ScienceServedWatts + " W > requested " + snapshot.ScienceRequestedWatts + " W.");

            if (configuration == null) return report;

            if (!Near(snapshot.CapacityJoules, configuration.CapacityJoules, toleranceJoules))
                report.Error("CapacityMismatch", "Snapshot capacity differs from configuration.");
            double served = snapshot.EssentialServedWatts + snapshot.ScienceServedWatts;
            double supply = snapshot.GenerationWatts + configuration.MaxDischargeWatts;
            if (served > supply + toleranceWatts)
                report.Error("ServedExceedsSupply", "Served " + served + " W > generation + max discharge " + supply + " W.");
            if (snapshot.EnergyJoules <= toleranceJoules && served > snapshot.GenerationWatts + toleranceWatts)
                report.Warning("ServedExceedsGenerationWhileEmpty", "Battery is empty but served power exceeds generation.");
            if (!Near(snapshot.GenerationWatts, configuration.GenerationWatts, toleranceWatts))
                report.Warning("GenerationDiffersFromConfiguration", "Generation differs from the constant phase 1 configuration.");
            if (!Near(snapshot.EssentialRequestedWatts, configuration.EssentialConsumptionWatts, toleranceWatts))
                report.Warning("EssentialDemandDiffersFromConfiguration", "Essential demand differs from configuration.");
            if (!Near(snapshot.ScienceRequestedWatts, 0, toleranceWatts)
                && !Near(snapshot.ScienceRequestedWatts, configuration.ScienceConsumptionWatts, toleranceWatts))
                report.Warning("ScienceDemandDiffersFromConfiguration", "Science demand is neither 0 nor the configured value.");
            return report;
        }

        /// <summary>Conservative accounting between two observations of the same epoch with constant
        /// configuration and no losses declared in the contract:
        /// gain &lt;= min(generation, maxCharge) * dt and loss &lt;= min(maxDischarge, essential + science) * dt.</summary>
        public static ValidationReport ValidatePowerTransition(PowerSnapshot previous, PowerSnapshot next,
            PowerConfiguration configuration, double toleranceJoules = DefaultToleranceJoules)
        {
            var report = new ValidationReport();
            if (previous == null || next == null) { report.Error("PowerSnapshotNull", "Transition endpoint is null."); return report; }
            if (configuration == null) { report.Error("ConfigurationNull", "Transition checks need the configuration."); return report; }

            double dt = next.SimulatedTimeSeconds - previous.SimulatedTimeSeconds;
            if (double.IsNaN(dt) || double.IsInfinity(dt)) { report.Error("NotFinite", "Elapsed time is not finite."); return report; }
            if (dt < -DefaultToleranceSeconds) report.Error("TimeWentBackwards", "Simulated time decreased by " + (-dt) + " s.");
            if (!Near(previous.CapacityJoules, next.CapacityJoules, toleranceJoules))
                report.Error("CapacityChanged", "Capacity changed inside one epoch.");
            if (dt < -DefaultToleranceSeconds) return report;
            dt = Math.Max(0, dt);

            double delta = next.EnergyJoules - previous.EnergyJoules;
            double maxGain = Math.Min(configuration.GenerationWatts, configuration.MaxChargeWatts) * dt;
            double maxLoss = Math.Min(configuration.MaxDischargeWatts,
                configuration.EssentialConsumptionWatts + configuration.ScienceConsumptionWatts) * dt;
            if (delta > maxGain + toleranceJoules)
                report.Error("EnergyGainExceedsLimits", "Energy rose " + delta + " J in " + dt + " s; bound is " + maxGain + " J.");
            if (-delta > maxLoss + toleranceJoules)
                report.Error("EnergyLossExceedsLimits", "Energy fell " + (-delta) + " J in " + dt + " s; bound is " + maxLoss + " J.");
            return report;
        }

        // ---------------------------------------------------------------- mission snapshots

        public static ValidationReport ValidateMissionSnapshot(MissionSnapshot snapshot, PowerConfiguration configuration = null)
        {
            var report = new ValidationReport();
            if (snapshot == null) { report.Error("MissionSnapshotNull", "MissionSnapshot is null."); return report; }
            if (snapshot.Epoch < 0) report.Error("NegativeEpoch", "Epoch must be nonnegative.");
            if (snapshot.Revision < 0) report.Error("NegativeRevision", "Revision must be nonnegative.");
            if (!Enum.IsDefined(typeof(SessionState), snapshot.State)) report.Error("UnknownState", "State " + (int)snapshot.State + " is not defined.");
            Magnitude(report, "ScienceWorkSeconds", snapshot.ScienceWorkSeconds, "s");
            report.Merge(ValidatePowerSnapshot(snapshot.Power, configuration));
            if (report.HasErrors) return report;

            if (snapshot.ScienceWorkSeconds > snapshot.Power.SimulatedTimeSeconds + DefaultToleranceSeconds)
                report.Warning("ScienceWorkExceedsSimulatedTime",
                    "Accumulated science work is longer than the epoch's simulated time (expected for scripted fake fixtures only).");
            if (!snapshot.ScienceEnabled && snapshot.Power.ScienceRequestedWatts > DefaultToleranceWatts)
                report.Warning("ScienceRequestedWhileDisabled", "Science demand is present while science is disabled.");
            return report;
        }

        /// <summary>Checks a published stream: epochs never regress; inside an epoch revision orders changes,
        /// time and science work never decrease, the clock does not run between two non-running snapshots,
        /// and work cannot grow faster than simulated time. Snapshots of a new epoch with revision 0 must
        /// look like a clean reset.</summary>
        public static ValidationReport ValidateMissionTransition(MissionSnapshot previous, MissionSnapshot next,
            PowerConfiguration configuration = null)
        {
            var report = new ValidationReport();
            if (previous == null || next == null) { report.Error("MissionSnapshotNull", "Transition endpoint is null."); return report; }
            report.Merge(ValidateMissionSnapshot(next, configuration));

            if (next.Epoch < previous.Epoch)
            {
                report.Error("EpochRegressed", "Epoch went from " + previous.Epoch + " to " + next.Epoch + ".");
                return report;
            }

            if (next.Epoch > previous.Epoch)
            {
                if (next.Revision == 0) CheckCleanRestart(report, next, configuration);
                return report;
            }

            if (next.Revision < previous.Revision)
                report.Error("RevisionRegressed", "Revision went from " + previous.Revision + " to " + next.Revision + " in epoch " + next.Epoch + ".");
            else if (next.Revision == previous.Revision && !SameContent(previous, next))
                report.Error("RevisionReusedWithDifferentContent", "Revision " + next.Revision + " published twice with different content.");

            double dt = next.Power.SimulatedTimeSeconds - previous.Power.SimulatedTimeSeconds;
            double dw = next.ScienceWorkSeconds - previous.ScienceWorkSeconds;
            if (dt < -DefaultToleranceSeconds) report.Error("TimeWentBackwards", "Simulated time decreased inside epoch " + next.Epoch + ".");
            if (dw < -DefaultToleranceSeconds) report.Error("ScienceWorkDecreased", "Science work decreased inside epoch " + next.Epoch + ".");
            if (previous.State != SessionState.Running && next.State != SessionState.Running && dt > DefaultToleranceSeconds)
                report.Error("ClockAdvancedWhileNotRunning", "Simulated time advanced " + dt + " s between " + previous.State + " and " + next.State + ".");
            if (dw > Math.Max(0, dt) + DefaultToleranceSeconds)
                report.Error("ScienceWorkExceedsElapsedTime", "Science work grew " + dw + " s in " + dt + " s of simulated time.");
            if (dw > DefaultToleranceSeconds && previous.Power.ScienceServedWatts <= DefaultToleranceWatts
                && next.Power.ScienceServedWatts <= DefaultToleranceWatts)
                report.Warning("ScienceWorkWithoutServedPower", "Science work grew while no science power was served at either end.");

            if (configuration != null && dt >= -DefaultToleranceSeconds)
                report.Merge(ValidatePowerTransition(previous.Power, next.Power, configuration));
            return report;
        }

        private static void CheckCleanRestart(ValidationReport report, MissionSnapshot next, PowerConfiguration configuration)
        {
            if (next.State != SessionState.Ready) report.Warning("RestartNotClean", "First snapshot of a new epoch is " + next.State + ", not Ready.");
            if (next.Power.SimulatedTimeSeconds > DefaultToleranceSeconds) report.Warning("RestartNotClean", "First snapshot of a new epoch has nonzero time.");
            if (next.ScienceWorkSeconds > DefaultToleranceSeconds) report.Warning("RestartNotClean", "First snapshot of a new epoch has science work.");
            if (next.ScienceEnabled) report.Warning("RestartNotClean", "First snapshot of a new epoch has science enabled.");
            if (configuration != null && !Near(next.Power.EnergyJoules, configuration.InitialEnergyJoules, DefaultToleranceJoules))
                report.Warning("RestartNotClean", "First snapshot of a new epoch does not start at the initial energy.");
        }

        // ---------------------------------------------------------------- commands

        /// <summary>Shape checks only; codes match the contract's rejection reasons. Identity and permissions
        /// are not in the payload and cannot be validated here.</summary>
        public static ValidationReport ValidateCommand(SessionCommand command)
        {
            var report = new ValidationReport();
            if (command == null) { report.Error("CommandNull", "SessionCommand is null."); return report; }
            if (command.Id == Guid.Empty) report.Error("InvalidCommandId", "Command ID is empty.");
            if (command.Epoch < 0) report.Error("NegativeEpoch", "Command epoch must be nonnegative.");
            if (!Enum.IsDefined(typeof(SessionAction), command.Action)) report.Error("UnknownAction", "Action " + (int)command.Action + " is not defined.");
            else if ((command.Action == SessionAction.SetScienceEnabled) != command.RequestedScienceEnabled.HasValue)
                report.Error("InvalidActionValue", "Only SetScienceEnabled carries a value, and it requires one.");
            return report;
        }

        public static ValidationReport ValidateCommandResult(CommandResult result, SessionCommand command)
        {
            var report = new ValidationReport();
            if (result == null || command == null) { report.Error("CommandResultNull", "Result or command is null."); return report; }
            if (result.CommandId != command.Id) report.Error("ResultIdMismatch", "Result does not correlate with the command ID.");
            if (result.Epoch != command.Epoch) report.Error("ResultEpochMismatch", "Result epoch must be the command's original epoch.");
            if (string.IsNullOrEmpty(result.Reason)) report.Warning("MissingReason", "Result has no reason.");
            return report;
        }

        /// <summary>A repeated identical command must reproduce the original outcome.</summary>
        public static ValidationReport ValidateReplay(CommandResult original, CommandResult replay)
        {
            var report = new ValidationReport();
            if (original == null || replay == null) { report.Error("CommandResultNull", "Result is null."); return report; }
            if (original.CommandId != replay.CommandId || original.Epoch != replay.Epoch
                || original.Accepted != replay.Accepted || original.Reason != replay.Reason)
                report.Error("ReplayResultDiffers", "Replay produced (" + replay.Accepted + ", " + replay.Reason
                    + ") instead of (" + original.Accepted + ", " + original.Reason + ").");
            return report;
        }

        // ---------------------------------------------------------------- helpers

        private static void Magnitude(ValidationReport report, string name, double value, string unit)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) report.Error("NotFinite", name + " is not finite.");
            else if (value < 0) report.Error("Negative", name + " is negative (" + value + " " + unit + ").");
            else if (value > MaxExactDouble) report.Error("ExceedsExactRange", name + " (" + value + " " + unit + ") exceeds 2^53; integer precision is lost.");
        }

        private static bool Near(double a, double b, double tolerance) { return Math.Abs(a - b) <= tolerance; }

        private static bool SameContent(MissionSnapshot a, MissionSnapshot b)
        {
            if (ReferenceEquals(a, b)) return true;
            PowerSnapshot p = a.Power, q = b.Power;
            return a.Epoch == b.Epoch && a.Revision == b.Revision && a.State == b.State
                && a.ScienceWorkSeconds.Equals(b.ScienceWorkSeconds) && a.ScienceEnabled == b.ScienceEnabled
                && p.SimulatedTimeSeconds.Equals(q.SimulatedTimeSeconds) && p.EnergyJoules.Equals(q.EnergyJoules)
                && p.CapacityJoules.Equals(q.CapacityJoules) && p.GenerationWatts.Equals(q.GenerationWatts)
                && p.EssentialRequestedWatts.Equals(q.EssentialRequestedWatts) && p.EssentialServedWatts.Equals(q.EssentialServedWatts)
                && p.ScienceRequestedWatts.Equals(q.ScienceRequestedWatts) && p.ScienceServedWatts.Equals(q.ScienceServedWatts);
        }
    }
}
