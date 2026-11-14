using System;
using Trainer.Application;
using Trainer.Simulation;

namespace Trainer.Tooling.Validation
{
    public enum FixtureStepKind { Resume, Pause, Science, Tick, Restart }

    /// <summary>One operation applied to the authoritative session (commands get fresh IDs and the current epoch).</summary>
    public sealed class FixtureStep
    {
        public FixtureStepKind Kind { get; }
        public double Seconds { get; }
        public bool Science { get; }

        private FixtureStep(FixtureStepKind kind, double seconds, bool science)
        {
            Kind = kind; Seconds = seconds; Science = science;
        }

        public static FixtureStep Resume() { return new FixtureStep(FixtureStepKind.Resume, 0, false); }
        public static FixtureStep Pause() { return new FixtureStep(FixtureStepKind.Pause, 0, false); }
        public static FixtureStep Restart() { return new FixtureStep(FixtureStepKind.Restart, 0, false); }
        public static FixtureStep SetScience(bool enabled) { return new FixtureStep(FixtureStepKind.Science, 0, enabled); }
        public static FixtureStep Tick(double seconds) { return new FixtureStep(FixtureStepKind.Tick, seconds, false); }

        public override string ToString()
        {
            switch (Kind)
            {
                case FixtureStepKind.Tick: return "Tick(" + Seconds + " s)";
                case FixtureStepKind.Science: return "SetScienceEnabled(" + Science + ")";
                default: return Kind.ToString();
            }
        }
    }

    /// <summary>Expected confirmed snapshot after a step (or the initial one).</summary>
    public sealed class FixtureExpectation
    {
        public long Epoch { get; }
        public long Revision { get; }
        public SessionState State { get; }
        public double TimeSeconds { get; }
        public double EnergyJoules { get; }
        public double WorkSeconds { get; }
        public bool ScienceEnabled { get; }
        public double EssentialServedWatts { get; }
        public double ScienceRequestedWatts { get; }
        public double ScienceServedWatts { get; }

        public FixtureExpectation(long epoch, long revision, SessionState state, double time, double energy,
            double work, bool science, double essentialServed, double scienceRequested, double scienceServed)
        {
            Epoch = epoch; Revision = revision; State = state; TimeSeconds = time; EnergyJoules = energy;
            WorkSeconds = work; ScienceEnabled = science; EssentialServedWatts = essentialServed;
            ScienceRequestedWatts = scienceRequested; ScienceServedWatts = scienceServed;
        }

        /// <summary>Null when the snapshot matches within tolerances, otherwise a description of the mismatch.</summary>
        public string Compare(MissionSnapshot actual, double toleranceSeconds = 1e-6, double toleranceJoules = 1e-6,
            double toleranceWatts = 1e-9)
        {
            if (actual == null) return "no snapshot";
            var problems = new System.Text.StringBuilder();
            if (actual.Epoch != Epoch) problems.Append(" epoch " + actual.Epoch + "!=" + Epoch);
            if (actual.Revision != Revision) problems.Append(" revision " + actual.Revision + "!=" + Revision);
            if (actual.State != State) problems.Append(" state " + actual.State + "!=" + State);
            if (actual.ScienceEnabled != ScienceEnabled) problems.Append(" science " + actual.ScienceEnabled + "!=" + ScienceEnabled);
            Near(problems, "time", actual.Power.SimulatedTimeSeconds, TimeSeconds, toleranceSeconds);
            Near(problems, "energy", actual.Power.EnergyJoules, EnergyJoules, toleranceJoules);
            Near(problems, "work", actual.ScienceWorkSeconds, WorkSeconds, toleranceSeconds);
            Near(problems, "essentialServed", actual.Power.EssentialServedWatts, EssentialServedWatts, toleranceWatts);
            Near(problems, "scienceRequested", actual.Power.ScienceRequestedWatts, ScienceRequestedWatts, toleranceWatts);
            Near(problems, "scienceServed", actual.Power.ScienceServedWatts, ScienceServedWatts, toleranceWatts);
            return problems.Length == 0 ? null : problems.ToString().Trim();
        }

        private static void Near(System.Text.StringBuilder problems, string name, double actual, double expected, double tolerance)
        {
            if (double.IsNaN(actual) || Math.Abs(actual - expected) > tolerance)
                problems.Append(" " + name + " " + actual + "!=" + expected);
        }
    }

    /// <summary>
    /// SYNTHETIC fixture (not NASA data). Expected values come from an independent event-driven reference
    /// model written from the contract text with exact fractions (docs/phase-01/handoffs/d5-evidence/
    /// reference_model.py), not from PowerSystem or MissionSession. Mission policy follows the D2 handoff:
    /// work only while science is fully served; end priority essential loss > objective > duration.
    /// </summary>
    public sealed class PhaseOneFixture
    {
        public string Name { get; }
        public string Purpose { get; }
        public PowerConfiguration Configuration { get; }
        public MissionRules Rules { get; }
        public FixtureStep[] Steps { get; }
        /// <summary>Index 0 is the initial snapshot; index i+1 is the snapshot after Steps[i].</summary>
        public FixtureExpectation[] Expected { get; }
        public MissionOutcome FinalOutcome { get; }

        public PhaseOneFixture(string name, string purpose, PowerConfiguration configuration, MissionRules rules,
            FixtureStep[] steps, FixtureExpectation[] expected, MissionOutcome finalOutcome)
        {
            if (steps == null || expected == null || expected.Length != steps.Length + 1)
                throw new ArgumentException("Need one expectation for the initial state and one per step.");
            Name = name; Purpose = purpose; Configuration = configuration; Rules = rules;
            Steps = steps; Expected = expected; FinalOutcome = finalOutcome;
        }
    }

    public static class PhaseOneFixtures
    {
        private static FixtureExpectation E(long epoch, long revision, SessionState state, double time, double energy,
            double work, bool science, double essentialServed, double scienceRequested, double scienceServed)
        {
            return new FixtureExpectation(epoch, revision, state, time, energy, work, science,
                essentialServed, scienceRequested, scienceServed);
        }

        /// <summary>Battery covers a deficit, science work accrues, pause freezes the clock, the objective
        /// ends the mission and Restart returns to a clean epoch.
        /// cap 36000 J, E0 18000 J, gen 50 W, essential 60 W, science 20 W, charge/discharge 40 W;
        /// objective 60 s of served science, duration 600 s.</summary>
        public static PhaseOneFixture BalanceObjectiveRestart()
        {
            const SessionState Ready = SessionState.Ready, Running = SessionState.Running,
                Paused = SessionState.Paused, Completed = SessionState.Completed;
            return new PhaseOneFixture("S1_BalanceObjectiveRestart",
                "deficit 10 W (60 s) then 30 W with science; pause; objective; restart",
                new PowerConfiguration(18000, 36000, 50, 60, 20, 40, 40), new MissionRules(60, 600),
                new[]
                {
                    FixtureStep.Resume(), FixtureStep.Tick(60), FixtureStep.SetScience(true), FixtureStep.Tick(30),
                    FixtureStep.Pause(), FixtureStep.Tick(30), FixtureStep.Resume(), FixtureStep.Tick(40),
                    FixtureStep.Restart()
                },
                new[]
                {
                    E(0, 0, Ready, 0, 18000, 0, false, 60, 0, 0),
                    E(0, 1, Running, 0, 18000, 0, false, 60, 0, 0),
                    E(0, 2, Running, 60, 17400, 0, false, 60, 0, 0),
                    E(0, 3, Running, 60, 17400, 0, true, 60, 0, 0),
                    E(0, 4, Running, 90, 16500, 30, true, 60, 20, 20),
                    E(0, 5, Paused, 90, 16500, 30, true, 60, 20, 20),
                    E(0, 5, Paused, 90, 16500, 30, true, 60, 20, 20),    // Tick while paused: no change
                    E(0, 6, Running, 90, 16500, 30, true, 60, 20, 20),
                    E(0, 7, Completed, 120, 15600, 60, true, 60, 20, 20), // Tick(40) stops at the objective (30 s)
                    E(1, 0, Ready, 0, 18000, 0, false, 60, 0, 0)
                },
                MissionOutcome.InProgress); // after Restart
        }

        /// <summary>Science drains the battery to empty at 20 s; then generation (50 W) cannot cover the
        /// essential load (60 W). cap 1000 J, E0 600 J, gen 50, essential 60, science 20, charge/discharge 40.</summary>
        public static PhaseOneFixture DepletionEssentialLost()
        {
            return new PhaseOneFixture("S2_DepletionEssentialLost",
                "battery empties at 20 s (30 W deficit); essential then served 50/60 W",
                new PowerConfiguration(600, 1000, 50, 60, 20, 40, 40), new MissionRules(100, 600),
                new[] { FixtureStep.SetScience(true), FixtureStep.Resume(), FixtureStep.Tick(50) },
                new[]
                {
                    E(0, 0, SessionState.Ready, 0, 600, 0, false, 60, 0, 0),
                    E(0, 1, SessionState.Ready, 0, 600, 0, true, 60, 0, 0),
                    E(0, 2, SessionState.Running, 0, 600, 0, true, 60, 0, 0),
                    E(0, 3, SessionState.Interrupted, 20, 0, 20, true, 50, 20, 0)
                },
                MissionOutcome.EssentialServiceLost);
        }

        /// <summary>Empty battery, generation 70 W: essential 60 W served, science gets 10 of 20 W, so no work
        /// is credited and the duration expires (Completed without the objective).</summary>
        public static PhaseOneFixture PartialScienceTimeExpired()
        {
            return new PhaseOneFixture("S3_PartialScienceTimeExpired",
                "partial science service earns no work; duration expires",
                new PowerConfiguration(0, 1000, 70, 60, 20, 40, 40), new MissionRules(10, 30),
                new[] { FixtureStep.SetScience(true), FixtureStep.Resume(), FixtureStep.Tick(45) },
                new[]
                {
                    E(0, 0, SessionState.Ready, 0, 0, 0, false, 60, 0, 0),
                    E(0, 1, SessionState.Ready, 0, 0, 0, true, 60, 0, 0),
                    E(0, 2, SessionState.Running, 0, 0, 0, true, 60, 0, 0),
                    E(0, 3, SessionState.Completed, 30, 0, 0, true, 60, 20, 10)
                },
                MissionOutcome.TimeExpired);
        }

        /// <summary>Surplus 40 W limited to 30 W charge: full after 3.33 s, then curtailed; science keeps it full.</summary>
        public static PhaseOneFixture ChargeToFullCurtailment()
        {
            return new PhaseOneFixture("S4_ChargeToFullCurtailment",
                "charge limit, full battery inside a step, curtailed generation",
                new PowerConfiguration(900, 1000, 100, 60, 20, 30, 40), new MissionRules(100, 600),
                new[] { FixtureStep.Resume(), FixtureStep.Tick(10), FixtureStep.SetScience(true), FixtureStep.Tick(10) },
                new[]
                {
                    E(0, 0, SessionState.Ready, 0, 900, 0, false, 60, 0, 0),
                    E(0, 1, SessionState.Running, 0, 900, 0, false, 60, 0, 0),
                    E(0, 2, SessionState.Running, 10, 1000, 0, false, 60, 0, 0),
                    E(0, 3, SessionState.Running, 10, 1000, 0, true, 60, 0, 0),
                    E(0, 4, SessionState.Running, 20, 1000, 10, true, 60, 20, 20)
                },
                MissionOutcome.InProgress);
        }

        public static PhaseOneFixture[] All()
        {
            return new[] { BalanceObjectiveRestart(), DepletionEssentialLost(), PartialScienceTimeExpired(), ChargeToFullCurtailment() };
        }
    }
}
