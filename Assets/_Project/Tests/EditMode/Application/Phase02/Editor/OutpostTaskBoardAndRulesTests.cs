using System;
using System.Linq;
using NUnit.Framework;
using Trainer.Application.Development;
using Trainer.Application.Outpost;
using Trainer.Simulation;
using Trainer.Simulation.LifeSupport;

namespace Trainer.Tests.Phase02
{
    public sealed class OutpostTaskBoardTests
    {
        private OutpostScenario scenario;
        private OutpostTaskBoard board;
        private TaskDefinition treat, experiment;
        private static readonly PlayerId Ana = new PlayerId(1), Leo = new PlayerId(2);

        [SetUp]
        public void SetUp()
        {
            scenario = OutpostFixtures.Scenario();
            board = new OutpostTaskBoard(scenario.Layout);
            scenario.TryGetTask(OutpostFixtures.TreatWater, out treat);
            scenario.TryGetTask(OutpostFixtures.RunExperiment, out experiment);
        }

        [Test]
        public void ReservationBindsStationAndPersonTogether()
        {
            board.Reserve(OutpostFixtures.WaterProcessor, treat, Ana, Guid.NewGuid());
            Assert.That(board.TryGetAtStation(OutpostFixtures.WaterProcessor, out var byStation), Is.True);
            Assert.That(board.TryGetForPlayer(Ana, out var byPlayer), Is.True);
            Assert.That(byPlayer, Is.SameAs(byStation));
            Assert.Throws<InvalidOperationException>(() => board.Reserve(OutpostFixtures.WaterProcessor, treat, Leo, Guid.NewGuid()),
                "an exclusive station never takes two occupants");
            Assert.Throws<InvalidOperationException>(() => board.Reserve(OutpostFixtures.ScienceBench, experiment, Ana, Guid.NewGuid()),
                "a person never holds two tasks");
            Assert.Throws<ArgumentException>(() => board.Reserve(OutpostFixtures.ScienceBench, treat, Leo, Guid.NewGuid()),
                "task offered at another station");
        }

        [Test]
        public void EndReleasesBothReservationsAndRecordsTheLastEnd()
        {
            board.Reserve(OutpostFixtures.WaterProcessor, treat, Ana, Guid.NewGuid());
            TaskEnd end = board.End(OutpostFixtures.WaterProcessor, TaskEndReason.CancelledByPerformer, 4);
            Assert.That((end.Performer, end.Reason, end.EndedAtSeconds), Is.EqualTo((Ana, TaskEndReason.CancelledByPerformer, 4d)));
            Assert.That(board.ActiveCount, Is.EqualTo(0));
            Assert.That(board.TryGetForPlayer(Ana, out _), Is.False);
            Assert.Throws<InvalidOperationException>(() => board.End(OutpostFixtures.WaterProcessor, TaskEndReason.Completed, 5),
                "no double end, so no double charge");
            StationState state = board.BuildStates().First(s => s.Station == OutpostFixtures.WaterProcessor);
            Assert.That(state.Availability, Is.EqualTo(StationAvailability.Available));
            Assert.That(state.LastEnd, Is.SameAs(end));
            board.Reserve(OutpostFixtures.ScienceBench, experiment, Ana, Guid.NewGuid());
        }

        [Test]
        public void EndAllAndClear()
        {
            board.Reserve(OutpostFixtures.WaterProcessor, treat, Ana, Guid.NewGuid());
            board.Reserve(OutpostFixtures.ScienceBench, experiment, Leo, Guid.NewGuid());
            Assert.That(board.EndAll(TaskEndReason.MissionEnded, 9), Is.EqualTo(2));
            Assert.That(board.BuildStates().Count(s => s.LastEnd?.Reason == TaskEndReason.MissionEnded), Is.EqualTo(2));
            board.Clear();
            Assert.That(board.BuildStates().All(s => s.LastEnd == null && s.ActiveTask == null));
        }

        [Test]
        public void StatesFollowLayoutOrder()
        {
            board.Reserve(OutpostFixtures.AirMonitor, scenario.Tasks.First(t => t.Station == OutpostFixtures.AirMonitor), Leo, Guid.NewGuid());
            Assert.That(board.BuildStates().Select(s => s.Station), Is.EqualTo(scenario.Layout.Stations.Select(s => s.Id)));
            Assert.That(board.Active().Single().Station, Is.EqualTo(OutpostFixtures.AirMonitor));
        }
    }

    public sealed class OutpostMissionRulesTests
    {
        private static readonly TaskTypeId Treat = OutpostFixtures.TreatWater;

        [Test]
        public void RulesValidateTheirInputs()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new OutpostMissionRules(0, new[] { new OutpostObjectiveRule("a", new[] { Treat }, 1, true) }));
            Assert.Throws<ArgumentException>(() => new OutpostMissionRules(10, new[] { new OutpostObjectiveRule("a", new[] { Treat }, 1, false) }),
                "at least one required objective");
            Assert.Throws<ArgumentException>(() => new OutpostMissionRules(10, new[]
                { new OutpostObjectiveRule("a", new[] { Treat }, 1, true), new OutpostObjectiveRule("a", new[] { Treat }, 2, true) }));
            Assert.Throws<ArgumentException>(() => new OutpostObjectiveRule("Bad Key", new[] { Treat }, 1, true));
            Assert.Throws<ArgumentOutOfRangeException>(() => new OutpostObjectiveRule("a", new[] { Treat }, 0, true));
            Assert.Throws<ArgumentException>(() => new OutpostObjectiveRule("a", new TaskTypeId[0], 1, true));
            Assert.Throws<ArgumentException>(() => new OutpostObjectiveRule("a", new[] { Treat, Treat }, 1, true));
            var rules = new OutpostMissionRules(10, new[] { new OutpostObjectiveRule("a", new[] { new TaskTypeId("ghost") }, 1, true) });
            Assert.That(rules.ProblemWith(OutpostFixtures.Scenario()), Does.Contain("ghost"));
        }

        [Test]
        public void EvaluationPriorityIsEssentialThenObjectivesThenTime()
        {
            var rules = new OutpostMissionRules(100, new[]
            {
                new OutpostObjectiveRule("water", new[] { Treat }, 2, true),
                new OutpostObjectiveRule("science", new[] { OutpostFixtures.RunExperiment }, 1, false)
            });
            PowerSnapshot Power(double t, bool essential) => new PowerSnapshot(t, 0, 1, 0, 60, essential ? 60 : 30, 0, 0);

            Assert.That(OutpostMissionEvaluator.Evaluate(rules, Power(5, true), new[] { 1, 5 }), Is.EqualTo(OutpostOutcome.InProgress),
                "optional objectives never end the mission");
            Assert.That(OutpostMissionEvaluator.Evaluate(rules, Power(5, true), new[] { 2, 0 }), Is.EqualTo(OutpostOutcome.ObjectiveAchieved));
            Assert.That(OutpostMissionEvaluator.Evaluate(rules, Power(100, true), new[] { 2, 0 }), Is.EqualTo(OutpostOutcome.ObjectiveAchieved));
            Assert.That(OutpostMissionEvaluator.Evaluate(rules, Power(100 - 1e-7, true), new[] { 0, 0 }), Is.EqualTo(OutpostOutcome.TimeExpired));
            Assert.That(OutpostMissionEvaluator.Evaluate(rules, Power(100, false), new[] { 2, 0 }), Is.EqualTo(OutpostOutcome.EssentialServiceLost));
            Assert.That(OutpostMissionEvaluator.IsEssentialServed(new PowerSnapshot(0, 0, 1, 0, 0, 0, 0, 0)), Is.True, "no essential demand");
        }

        [Test]
        public void CanApplyPredictsStockAndCapacityWithoutChangingAnything()
        {
            var ledger = new LifeSupportSnapshot(new WaterStock(39, 1.5, 40, 20), new FoodStock(1, 2),
                new CabinAirObservation(GasReading.Unavailable, GasReading.Unavailable));
            var treat = new ResourceEffect(new[]
                { new ResourceChange(ResourceKind.UntreatedWater, -1.5), new ResourceChange(ResourceKind.PotableWater, 1.0) }, 0.5);
            Assert.That(OutpostMissionEvaluator.CanApply(ledger, treat), Is.True);
            var tooMuch = new ResourceEffect(new[]
                { new ResourceChange(ResourceKind.UntreatedWater, -2), new ResourceChange(ResourceKind.PotableWater, 1.8) }, 0.2);
            Assert.That(OutpostMissionEvaluator.CanApply(ledger, tooMuch), Is.False, "untreated would go negative");
            var overflow = new ResourceEffect(new[]
                { new ResourceChange(ResourceKind.UntreatedWater, -1.5), new ResourceChange(ResourceKind.PotableWater, 1.5) }, 0);
            Assert.That(OutpostMissionEvaluator.CanApply(ledger, overflow), Is.False, "potable would exceed its 40 kg capacity");
            Assert.That(OutpostMissionEvaluator.CanApply(ledger, ResourceEffect.None), Is.True);
            Assert.That(ledger.Water.UntreatedKilograms, Is.EqualTo(1.5));
        }
    }
}
