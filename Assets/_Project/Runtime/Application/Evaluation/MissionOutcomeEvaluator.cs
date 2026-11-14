using System;
using Trainer.Simulation;

namespace Trainer.Application
{
    /// <summary>Mission rules in simulated seconds. Values are scenario inputs, not approved mission data.</summary>
    public sealed class MissionRules
    {
        /// <summary>Science work (seconds of fully served science demand) needed to complete the objective.</summary>
        public double ScienceWorkTargetSeconds { get; }
        /// <summary>Simulated time after which the mission ends even if the objective was not reached.</summary>
        public double MissionDurationSeconds { get; }

        public MissionRules(double scienceWorkTargetSeconds, double missionDurationSeconds)
        {
            if (!IsPositiveFinite(scienceWorkTargetSeconds))
                throw new ArgumentOutOfRangeException(nameof(scienceWorkTargetSeconds));
            if (!IsPositiveFinite(missionDurationSeconds))
                throw new ArgumentOutOfRangeException(nameof(missionDurationSeconds));
            ScienceWorkTargetSeconds = scienceWorkTargetSeconds;
            MissionDurationSeconds = missionDurationSeconds;
        }

        private static bool IsPositiveFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
    }

    public enum MissionOutcome
    {
        /// <summary>Mission continues.</summary>
        InProgress,
        /// <summary>Science objective reached while essential demand stayed served. Maps to Completed.</summary>
        ObjectiveAchieved,
        /// <summary>Duration elapsed without reaching the objective. Maps to Completed.</summary>
        TimeExpired,
        /// <summary>Essential demand was not fully served. Maps to Interrupted.</summary>
        EssentialServiceLost,
        /// <summary>The injected simulation failed (threw or returned no snapshot). Maps to Interrupted;
        /// only Restart is accepted afterwards.</summary>
        SimulationFault
    }

    /// <summary>Pure evaluation of served science work and mission end conditions.
    /// Works only from post-step PowerSnapshot observations (instantaneous watts).</summary>
    public static class MissionOutcomeEvaluator
    {
        /// <summary>Relative tolerance used when comparing served with requested watts.</summary>
        public const double ServiceTolerance = 1e-9;
        /// <summary>Absolute tolerance (simulated seconds) for reaching the duration and the objective,
        /// so a simulation that lands a rounding error short of a limit still ends the mission.</summary>
        public const double TimeToleranceSeconds = 1e-6;

        /// <summary>True when the load has positive demand and that demand is fully served.</summary>
        public static bool IsFullyServed(double requestedWatts, double servedWatts)
        {
            if (!(requestedWatts > 0) || double.IsInfinity(requestedWatts)) return false;
            return servedWatts >= requestedWatts * (1 - ServiceTolerance);
        }

        /// <summary>
        /// Science work credited for one simulation step: the simulated seconds that elapsed between
        /// the two observations, only if science was requested and the post-step observation shows
        /// its demand fully served. Partial service earns nothing (an instrument without full power
        /// does not progress). Because PowerSnapshot is instantaneous, changes inside the step are
        /// not visible; callers must keep steps short.
        /// </summary>
        public static double ServedScienceSeconds(PowerSnapshot before, PowerSnapshot after, bool scienceRequested)
        {
            if (before == null) throw new ArgumentNullException(nameof(before));
            if (after == null) throw new ArgumentNullException(nameof(after));
            if (!scienceRequested) return 0;
            double elapsed = after.SimulatedTimeSeconds - before.SimulatedTimeSeconds;
            if (!(elapsed > 0) || double.IsInfinity(elapsed)) return 0;
            return IsFullyServed(after.ScienceRequestedWatts, after.ScienceServedWatts) ? elapsed : 0;
        }

        /// <summary>
        /// Evaluates a post-step observation. Essential loss has priority over the objective and the
        /// time limit. Essential demand of zero counts as served (nothing is required).
        /// </summary>
        public static MissionOutcome Evaluate(MissionRules rules, PowerSnapshot power, double scienceWorkSeconds)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (power == null) throw new ArgumentNullException(nameof(power));
            bool essentialServed = power.EssentialRequestedWatts <= 0
                || IsFullyServed(power.EssentialRequestedWatts, power.EssentialServedWatts);
            if (!essentialServed) return MissionOutcome.EssentialServiceLost;
            if (scienceWorkSeconds >= rules.ScienceWorkTargetSeconds - TimeToleranceSeconds)
                return MissionOutcome.ObjectiveAchieved;
            if (power.SimulatedTimeSeconds >= rules.MissionDurationSeconds - TimeToleranceSeconds)
                return MissionOutcome.TimeExpired;
            return MissionOutcome.InProgress;
        }

        public static SessionState ToSessionState(MissionOutcome outcome)
        {
            switch (outcome)
            {
                case MissionOutcome.ObjectiveAchieved:
                case MissionOutcome.TimeExpired:
                    return SessionState.Completed;
                case MissionOutcome.EssentialServiceLost:
                case MissionOutcome.SimulationFault:
                    return SessionState.Interrupted;
                case MissionOutcome.InProgress:
                    return SessionState.Running;
                default:
                    throw new ArgumentOutOfRangeException(nameof(outcome));
            }
        }
    }
}
