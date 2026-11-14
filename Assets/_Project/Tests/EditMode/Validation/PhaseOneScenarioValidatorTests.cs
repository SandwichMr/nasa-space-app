// Compiled out on purpose. This path is not under an Editor folder and the project has no test .asmdef,
// so without the gate it would land in Assembly-CSharp (no NUnit, no access to Editor code) and break the
// normal build. Integration must add test assemblies that can see Trainer.Tooling.Validation, then define
// TRAINER_PHASE1_VALIDATION_TESTS. Until then, the same cases run without NUnit through
// Tools > Trainer > Phase 1 > Run validation checks (PhaseOneBuildChecks).
#if TRAINER_PHASE1_VALIDATION_TESTS && UNITY_INCLUDE_TESTS
using NUnit.Framework;
using Trainer.Application;
using Trainer.Simulation;
using Trainer.Tooling.Validation;

namespace Trainer.Tests.Validation
{
    /// <summary>SYNTHETIC fixture, not NASA data. Expectations computed outside the production code:
    /// lossless deficit 10 W x 60 s = 600 J (18000 -> 17400 J); 30 W x 60 s = 1800 J (17400 -> 15600 J).</summary>
    public sealed class PhaseOneScenarioValidatorTests
    {
        private static PowerConfiguration Config() { return new PowerConfiguration(18000, 36000, 50, 60, 20, 40, 40); }

        private static PowerSnapshot Power(double t, double energy, double scienceRequested = 0, double scienceServed = 0, double essentialServed = 60)
        {
            return new PowerSnapshot(t, energy, 36000, 50, 60, essentialServed, scienceRequested, scienceServed);
        }

        private static MissionSnapshot Mission(long epoch, long revision, SessionState state, PowerSnapshot power, double work, bool science)
        {
            return new MissionSnapshot(epoch, revision, state, power, work, science);
        }

        private static MissionSnapshot[] Sequence()
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

        [Test] public void SyntheticConfigurationHasNoIssues()
        {
            Assert.That(PhaseOneScenarioValidator.ValidateConfiguration(Config()).Issues, Is.Empty);
        }

        [Test] public void NullConfigurationIsError()
        {
            Assert.That(PhaseOneScenarioValidator.ValidateConfiguration(null).Contains("ConfigurationNull"), Is.True);
        }

        [TestCase(double.NaN, 1.0)]
        [TestCase(0.0, double.PositiveInfinity)]
        [TestCase(-1.0, 1.0)]
        [TestCase(2.0, 1.0)]
        public void ContractConstructorRejectsInvalidValues(double initial, double capacity)
        {
            Assert.Throws(Is.InstanceOf<System.ArgumentException>(), () => new PowerConfiguration(initial, capacity, 0, 0, 0, 0, 0));
        }

        [Test] public void CapacityTypedInWattHoursIsFlagged()
        {
            Assert.That(PhaseOneScenarioValidator.ValidateConfiguration(new PowerConfiguration(5, 10, 50, 60, 20, 40, 40))
                .Contains("PossibleUnitMismatch"), Is.True);
        }

        [Test] public void UnservableDemandIsWarningNotError()
        {
            ValidationReport essential = PhaseOneScenarioValidator.ValidateConfiguration(new PowerConfiguration(0, 36000, 10, 60, 20, 40, 40));
            ValidationReport science = PhaseOneScenarioValidator.ValidateConfiguration(new PowerConfiguration(0, 36000, 50, 60, 40, 40, 40));
            Assert.That(essential.Contains("EssentialNotFullyServable"), Is.True);
            Assert.That(science.Contains("ScienceNotFullyServable"), Is.True);
            Assert.That(essential.HasErrors || science.HasErrors, Is.False);
        }

        [Test] public void MagnitudeAboveExactDoubleRangeIsError()
        {
            ValidationReport report = PhaseOneScenarioValidator.ValidateConfiguration(new PowerConfiguration(0, 1e16, 0, 0, 0, 0, 0));
            Assert.That(report.Contains("ExceedsExactRange") && !report.HasErrors, Is.True);
        }

        [Test] public void SyntheticSequenceBalancesWithoutIssues()
        {
            MissionSnapshot[] s = Sequence();
            Assert.That(PhaseOneScenarioValidator.ValidateMissionSnapshot(s[0], Config()).Issues, Is.Empty);
            for (int i = 1; i < s.Length; i++)
                Assert.That(PhaseOneScenarioValidator.ValidateMissionTransition(s[i - 1], s[i], Config()).Issues, Is.Empty, "step " + i);
        }

        [TestCase(21000.0, "EnergyGainExceedsLimits")]   // +3000 J > min(50, 40) W x 60 s = 2400 J
        [TestCase(15000.0, "EnergyLossExceedsLimits")]   // -3000 J > min(40, 80) W x 60 s = 2400 J
        public void EnergyChangeBeyondPowerLimitsIsError(double energyAt60, string code)
        {
            ValidationReport report = PhaseOneScenarioValidator.ValidatePowerTransition(Power(0, 18000), Power(60, energyAt60), Config());
            Assert.That(report.Contains(code) && report.HasErrors, Is.True, report.ToString());
        }

        [Test] public void SnapshotBoundsAreChecked()
        {
            Assert.That(PhaseOneScenarioValidator.ValidatePowerSnapshot(Power(0, 36001)).Contains("EnergyAboveCapacity"), Is.True);
            Assert.That(PhaseOneScenarioValidator.ValidatePowerSnapshot(Power(0, 18000, 0, 0, 70)).Contains("ServedExceedsRequested"), Is.True);
            Assert.That(PhaseOneScenarioValidator.ValidatePowerSnapshot(Power(0, 18000, 20, 20),
                new PowerConfiguration(18000, 36000, 50, 60, 20, 40, 10)).Contains("ServedExceedsSupply"), Is.True);
            Assert.That(PhaseOneScenarioValidator.ValidatePowerTransition(Power(60, 17400), Power(0, 18000), Config())
                .Contains("TimeWentBackwards"), Is.True);
        }

        [Test] public void ClockMustNotAdvanceWhilePaused()
        {
            MissionSnapshot paused = Sequence()[5];
            var later = Mission(0, 6, SessionState.Paused, Power(180, 15600, 20, 20), 60, true);
            Assert.That(PhaseOneScenarioValidator.ValidateMissionTransition(paused, later, Config()).Contains("ClockAdvancedWhileNotRunning"), Is.True);
        }

        [Test] public void ScienceWorkMustFollowPoweredTime()
        {
            MissionSnapshot[] s = Sequence();
            Assert.That(PhaseOneScenarioValidator.ValidateMissionTransition(s[3],
                Mission(0, 4, SessionState.Running, Power(120, 15600, 20, 20), 90, true), Config()).Contains("ScienceWorkExceedsElapsedTime"), Is.True);
            Assert.That(PhaseOneScenarioValidator.ValidateMissionTransition(s[2],
                Mission(0, 3, SessionState.Running, Power(90, 17100), 30, false), Config()).Contains("ScienceWorkWithoutServedPower"), Is.True);
        }

        [Test] public void EpochAndRevisionOrderingIsChecked()
        {
            MissionSnapshot[] s = Sequence();
            Assert.That(PhaseOneScenarioValidator.ValidateMissionTransition(Mission(1, 0, SessionState.Ready, Power(0, 18000), 0, false), s[2])
                .Contains("EpochRegressed"), Is.True);
            Assert.That(PhaseOneScenarioValidator.ValidateMissionTransition(s[2], s[1]).Contains("RevisionRegressed"), Is.True);
            Assert.That(PhaseOneScenarioValidator.ValidateMissionTransition(s[2],
                Mission(0, 2, SessionState.Running, Power(60, 17000), 0, false)).Contains("RevisionReusedWithDifferentContent"), Is.True);
        }

        [Test] public void RestartMustStartClean()
        {
            MissionSnapshot last = Sequence()[5];
            Assert.That(PhaseOneScenarioValidator.ValidateMissionTransition(last,
                Mission(1, 0, SessionState.Ready, Power(0, 18000), 0, false), Config()).Issues, Is.Empty);
            Assert.That(PhaseOneScenarioValidator.ValidateMissionTransition(last,
                Mission(1, 0, SessionState.Ready, Power(0, 18000), 5, false), Config()).Contains("RestartNotClean"), Is.True);
        }

        [Test] public void CommandShapeUsesContractReasonCodes()
        {
            Assert.That(PhaseOneScenarioValidator.ValidateCommand(new SessionCommand(System.Guid.Empty, 0, SessionAction.Resume)).Contains("InvalidCommandId"), Is.True);
            Assert.That(PhaseOneScenarioValidator.ValidateCommand(new SessionCommand(System.Guid.NewGuid(), 0, (SessionAction)999)).Contains("UnknownAction"), Is.True);
            Assert.That(PhaseOneScenarioValidator.ValidateCommand(new SessionCommand(System.Guid.NewGuid(), 0, SessionAction.Pause, true)).Contains("InvalidActionValue"), Is.True);
            Assert.That(PhaseOneScenarioValidator.ValidateCommand(new SessionCommand(System.Guid.NewGuid(), 0, SessionAction.SetScienceEnabled)).Contains("InvalidActionValue"), Is.True);
            Assert.That(PhaseOneScenarioValidator.ValidateCommand(new SessionCommand(System.Guid.NewGuid(), -1, SessionAction.Restart)).Contains("NegativeEpoch"), Is.True);
            Assert.That(PhaseOneScenarioValidator.ValidateCommand(new SessionCommand(System.Guid.NewGuid(), 3, SessionAction.SetScienceEnabled, false)).Issues, Is.Empty);
        }

        [Test] public void ResultCorrelationAndReplayAreChecked()
        {
            var command = new SessionCommand(System.Guid.NewGuid(), 4, SessionAction.Restart);
            var ok = new CommandResult(command.Id, 4, true, "Accepted");
            Assert.That(PhaseOneScenarioValidator.ValidateCommandResult(ok, command).Issues, Is.Empty);
            Assert.That(PhaseOneScenarioValidator.ValidateCommandResult(new CommandResult(command.Id, 5, true, "Accepted"), command)
                .Contains("ResultEpochMismatch"), Is.True);
            Assert.That(PhaseOneScenarioValidator.ValidateReplay(ok, new CommandResult(command.Id, 4, false, "EpochMismatch"))
                .Contains("ReplayResultDiffers"), Is.True);
        }
    }
}
#endif
