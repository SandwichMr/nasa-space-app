using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.BaseOperations;
using Trainer.Application.Development;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.LifeSupport;
using Trainer.Tooling.Validation.Phase04;
using Trainer.Unity.Validation.Phase04;

namespace Trainer.Tests.Phase04.Validation
{
    /// <summary>
    /// D5 phase 4 tooling on the labelled synthetic fixtures and the development fake (kind fixture-fake). They prove the
    /// validators and the pilot DETECT what they claim to detect; they are not evidence about D1–D4 implementations.
    /// </summary>
    public sealed class Phase04ValidationToolingTests
    {
        private static readonly PlayerId P1 = new PlayerId(1);

        // ---- check plumbing ---------------------------------------------------------------------------------------

        [Test]
        public void CheckRunnerMapsOutcomesAndNeverPassesByException()
        {
            Assert.That(Phase04Checks.Run("ok", Phase04Kinds.FixtureFake, () => "detail").Status, Is.EqualTo(Phase04CheckStatus.Passed));
            Assert.That(Phase04Checks.Run("fail", Phase04Kinds.FixtureFake, () => Phase04Checks.Require(false, "no")).Status, Is.EqualTo(Phase04CheckStatus.Failed));
            Assert.That(Phase04Checks.Run("pending", Phase04Kinds.FixtureFake, () => { throw new Phase04CheckPending("later"); }).Status,
                Is.EqualTo(Phase04CheckStatus.Pending));
            Assert.That(Phase04Checks.Run("boom", Phase04Kinds.FixtureFake, () => { throw new InvalidOperationException("x"); }).Status,
                Is.EqualTo(Phase04CheckStatus.Failed));
        }

        [Test]
        public void ExitCodeDistinguishesFailedFromPending()
        {
            var passed = Phase04Checks.Result("a", "k", true, "");
            var pending = Phase04Checks.Pending("b", "k", "");
            var failed = Phase04Checks.Result("c", "k", false, "");
            Assert.That(Phase04Checks.ExitCode(new[] { passed }), Is.EqualTo(Phase04Checks.ExitPassed));
            Assert.That(Phase04Checks.ExitCode(new[] { passed, pending }), Is.EqualTo(Phase04Checks.ExitPending));
            Assert.That(Phase04Checks.ExitCode(new[] { passed, pending, failed }), Is.EqualTo(Phase04Checks.ExitFailed));
        }

        // ---- scenario validator ---------------------------------------------------------------------------------------

        [Test]
        public void FixtureScenarioPassesThePhase4Rules()
        {
            List<Phase04Check> checks = BaseOperationsScenarioValidator.Validate(BaseOperationsFixtures.Scenario(), "fixture");
            List<Phase04Check> failed = checks.Where(c => c.Status == Phase04CheckStatus.Failed).ToList();
            Assert.That(failed, Is.Empty, string.Join("\n", failed));
        }

        [Test]
        public void ValidatorRejectsALogisticsConsoleOutsideTheBase()
        {
            BaseOperationsScenario outside = WithConsoleAt(new BasePosition(20, 2));
            List<Phase04Check> checks = BaseOperationsScenarioValidator.Validate(outside, "moved");
            Assert.That(checks.Any(c => c.Status == Phase04CheckStatus.Failed && c.Name.Contains("inside the base")), Is.True,
                Phase04Checks.Format(checks));
        }

        [Test]
        public void ValidatorRejectsAScenarioWithoutTheScheduledFault()
        {
            List<Phase04Check> checks = BaseOperationsScenarioValidator.Validate(BaseOperationsFixtures.Scenario(null), "no-fault");
            Assert.That(checks.Any(c => c.Status == Phase04CheckStatus.Failed && c.Name.Contains("fault scheduled")), Is.True);
        }

        // ---- navigator ------------------------------------------------------------------------------------------------

        [Test]
        public void NavigatorReachesEveryPhase4StationFromEverySpawnWithAdmissibleWaypoints()
        {
            BaseOperationsScenario s = BaseOperationsFixtures.Scenario();
            var navigator = new Phase04Navigator(s.Expedition);
            foreach (StationId station in new[] { s.WaterStation, s.AirStation, s.Logistics.Console })
                foreach (BasePosition spawn in s.Layout.SpawnPoints)
                {
                    List<BasePosition> path = navigator.PlanToStation(spawn, station);
                    Assert.That(path, Is.Not.Null, station + " from " + spawn);
                    Assert.That(path.All(p => navigator.Layout.IsAdmissible(p, navigator.CrewRadius)), Is.True);
                    s.Layout.TryGetStation(station, out StationPlacement placement);
                    Assert.That(placement.IsWithinReach(path[path.Count - 1]), Is.True, "last waypoint within reach of " + station);
                }
        }

        [Test]
        public void NavigatorNeverPlansThroughClosedHatches()
        {
            BaseOperationsScenario s = BaseOperationsFixtures.Scenario();
            var navigator = new Phase04Navigator(s.Expedition);
            Assert.That(navigator.PlanToStation(s.Layout.SpawnPoints[0], ExpeditionFixtures.RegolithSite), Is.Null,
                "the exterior is unreachable with both hatches closed");
        }

        // ---- stream validator -----------------------------------------------------------------------------------------

        [Test]
        public void StreamAcceptsACoherentRunningSequence()
        {
            BaseOperationsScenario s = BaseOperationsFixtures.Scenario();
            var v = new BaseOperationsStreamValidator(s, Phase04Kinds.FixtureFake);
            BaseOperationsSnapshot s0 = BaseOperationsFixtures.Initial(s, 0, P1);
            BaseOperationsSnapshot s1 = BaseOperationsFixtures.Edit(s, s0).Expedition(e => e.Session(SessionState.Running).AtTime(10)).Build();
            BaseOperationsSnapshot s2 = BaseOperationsFixtures.Edit(s, s1).Expedition(e => e.AtTime(20).Resources(Water(s1, -0.1, 0))).Build();
            foreach (BaseOperationsSnapshot x in new[] { s0, s1, s2 }) v.Observe(x);
            Assert.That(v.HasViolations, Is.False, Phase04Checks.Format(v.Results("coherent")));
        }

        [Test]
        public void StreamDetectsTheClockGoingBack()
        {
            BaseOperationsScenario s = BaseOperationsFixtures.Scenario();
            BaseOperationsSnapshot s1 = Running(s, 10);
            BaseOperationsSnapshot back = BaseOperationsFixtures.Edit(s, s1).Expedition(e => e.AtTime(5)).Build();
            AssertViolation(s, "one clock", s1, back);
        }

        [Test]
        public void StreamDetectsPotableWaterAppearingWithoutRoutingOrUnloading()
        {
            BaseOperationsScenario s = BaseOperationsFixtures.Scenario();
            BaseOperationsSnapshot s1 = Running(s, 10);
            BaseOperationsSnapshot more = BaseOperationsFixtures.Edit(s, s1).Expedition(e => e.AtTime(11).Resources(Water(s1, +1.0, 0))).Build();
            AssertViolation(s, "no hidden recovery", s1, more);
        }

        [Test]
        public void StreamDetectsFoodAppearingWithoutUnloading()
        {
            BaseOperationsScenario s = BaseOperationsFixtures.Scenario();
            BaseOperationsSnapshot s1 = Running(s, 10);
            BaseOperationsSnapshot more = BaseOperationsFixtures.Edit(s, s1).Expedition(e => e.AtTime(11).Resources(Water(s1, 0, +1.0))).Build();
            AssertViolation(s, "no hidden recovery", s1, more);
        }

        [Test]
        public void StreamDetectsTheClockMovingWhilePaused()
        {
            BaseOperationsScenario s = BaseOperationsFixtures.Scenario();
            BaseOperationsSnapshot p1 = BaseOperationsFixtures.Edit(s, Running(s, 10)).Expedition(e => e.Session(SessionState.Paused)).Build();
            BaseOperationsSnapshot p2 = BaseOperationsFixtures.Edit(s, p1).Expedition(e => e.AtTime(12)).Build();
            AssertViolation(s, "pause", p1, p2);
        }

        [Test]
        public void StreamDetectsARestartThatKeepsOrders()
        {
            BaseOperationsScenario s = BaseOperationsFixtures.Scenario();
            BaseOperationsSnapshot s1 = Running(s, 10);
            BaseOperationsSnapshot restarted = BaseOperationsFixtures.Edit(s, BaseOperationsFixtures.Initial(s, 1, P1))
                .Order(new ResupplyOrder(1, BaseOperationsFixtures.StandardManifest, P1, 0, 30, 180, OrderState.Pending,
                    BaseOperationsFixtures.Manifest().Items, null)).Build();
            AssertViolation(s, "restart", s1, restarted);
        }

        [Test]
        public void StreamDetectsBackToReadyInsideAnEpoch()
        {
            BaseOperationsScenario s = BaseOperationsFixtures.Scenario();
            BaseOperationsSnapshot s1 = Running(s, 10);
            BaseOperationsSnapshot ready = BaseOperationsFixtures.Edit(s, s1).Expedition(e => e.Session(SessionState.Ready).AtTime(11)).Build();
            AssertViolation(s, "continuity", s1, ready);
        }

        // ---- continuity pilot verdicts --------------------------------------------------------------------------------

        [Test]
        public void PilotFailsWhenTheBaseIsResetDuringTheRun()
        {
            BaseOperationsScenario s = BaseOperationsFixtures.Scenario();
            var actor = new ScriptedActor(P1) { State = Running(s, 10) };
            var pilot = new BaseOperationsContinuityScript(actor, s, ContinuityRole.Water, 600);
            pilot.Update(0.1);
            actor.State = BaseOperationsFixtures.Initial(s, 1, P1);
            pilot.Update(0.1);
            Assert.That(pilot.Finished && !pilot.Passed, Is.True, pilot.Verdict);
            StringAssert.Contains("epoch changed", pilot.Verdict);
        }

        [Test]
        public void PilotFailsWhenTheSessionIsInterrupted()
        {
            BaseOperationsScenario s = BaseOperationsFixtures.Scenario();
            BaseOperationsSnapshot running = Running(s, 70);
            var actor = new ScriptedActor(P1) { State = running };
            var pilot = new BaseOperationsContinuityScript(actor, s, ContinuityRole.AirAndLogistics, 600);
            pilot.Update(0.1);
            var end = new BaseSessionStatus(BaseSessionMode.Continuous, 60, 60, IrreversibleCause.EssentialPowerLost, 70, 0, 0);
            actor.State = BaseOperationsFixtures.Edit(s, running).Session(end)
                .Expedition(e => e.Ended(ExpeditionOutcome.EssentialServiceLost)).Build();
            pilot.Update(0.1);
            Assert.That(pilot.Finished && !pilot.Passed, Is.True, pilot.Verdict);
        }

        [Test]
        public void PilotDoesNotPassByTimeAloneWithoutProgress()
        {
            BaseOperationsScenario s = BaseOperationsFixtures.Scenario();
            var actor = new ScriptedActor(P1) { State = Running(s, 10) };
            var pilot = new BaseOperationsContinuityScript(actor, s, ContinuityRole.Water, 1);
            pilot.Update(0.5);
            actor.State = BaseOperationsFixtures.Edit(s, actor.State).Expedition(e => e.AtTime(20)).Build();
            pilot.Update(0.6);
            Assert.That(pilot.Finished, Is.True);
            Assert.That(pilot.Passed, Is.False, "no routed batch was confirmed: the pilot must not pass");
        }

        [Test]
        public void PilotOnlyRequestsAndNeverReportsItsOwnSuccess()
        {
            BaseOperationsScenario s = BaseOperationsFixtures.Scenario();
            BaseOperationsSnapshot atWater = BaseOperationsFixtures.Edit(s, Running(s, 10))
                .Expedition(e => e.Member(P1, new BasePosition(10, 2.2))).Build();
            var actor = new ScriptedActor(P1) { State = atWater };
            var pilot = new BaseOperationsContinuityScript(actor, s, ContinuityRole.Water, 600);
            pilot.Update(0.1);
            Assert.That(actor.Requests.Count, Is.EqualTo(1), "one request towards the water station");
            Assert.That(actor.Requests[0].Item1, Is.EqualTo(BaseOperationAction.Inspect));
            Assert.That(pilot.Metrics.Accepted, Is.EqualTo(0), "nothing counts until the authority answers");
            actor.Answer = BaseOperationReasons.StaleObservation;
            pilot.Update(0.1);
            Assert.That(pilot.Metrics.Accepted, Is.EqualTo(0));
            Assert.That(pilot.Metrics.Refusals.ContainsKey(BaseOperationReasons.StaleObservation), Is.True);
        }

        // ---- fake port and discovery ----------------------------------------------------------------------------------

        [TestCase(SessionState.Ready, 1)]
        [TestCase(SessionState.Running, 0)]
        [TestCase(SessionState.Paused, 0)]
        public void UnattendedPilotStartsOnlyAReadyJoinedSession(SessionState state, int expectedCommands)
        {
            var scenario = BaseOperationsFixtures.Scenario();
            var fake = new FakeBaseOperationsSessionPort();
            fake.Publish(BaseOperationsFixtures.Edit(scenario, BaseOperationsFixtures.Initial(scenario, 7, P1))
                .Expedition(e => e.Session(state)).Build());
            BaseOperationsAutopilot.RequestInitialStart(fake);
            Assert.That(fake.PendingExpeditionCommands, Is.Empty, "no start before confirmed membership");
            fake.Join(P1);
            BaseOperationsAutopilot.RequestInitialStart(fake);
            Assert.That(fake.PendingExpeditionCommands.Count, Is.EqualTo(expectedCommands));
            if (expectedCommands == 1)
            {
                Assert.That(fake.PendingExpeditionCommands[0].Action, Is.EqualTo(ExpeditionAction.Resume));
                Assert.That(fake.PendingExpeditionCommands[0].Epoch, Is.EqualTo(7));
            }
        }

        [Test]
        public void StreamValidatorRunsOverTheFakePortWithoutCountingItAsAnImplementation()
        {
            BaseOperationsScenario s = BaseOperationsFixtures.Scenario();
            var fake = new FakeBaseOperationsSessionPort();
            var v = new BaseOperationsStreamValidator(s, Phase04Kinds.FixtureFake);
            fake.BaseOperationsChanged += v.Observe;
            BaseOperationsSnapshot s0 = BaseOperationsFixtures.Initial(s, 0, P1);
            fake.Publish(s0);
            fake.Join(P1);
            fake.Publish(BaseOperationsFixtures.Edit(s, s0).Expedition(e => e.Session(SessionState.Running).AtTime(5)).Build());
            Assert.That(v.Observed, Is.EqualTo(2));
            Assert.That(v.HasViolations, Is.False);
            Phase04ImplementationDiscovery d = Phase04ImplementationDiscovery.Discover();
            Assert.That(d.LocalPortType, Is.Not.EqualTo(typeof(FakeBaseOperationsSessionPort)));
            Assert.That(Phase04ImplementationDiscovery.IsProductionCandidate(typeof(FakeBaseOperationsSessionPort)), Is.False);
        }

        [Test]
        public void DiscoveryReportsMissingImplementationsAsPendingNeverPassed()
        {
            Phase04ImplementationDiscovery d = Phase04ImplementationDiscovery.Discover();
            Assert.That(d.Checks.Where(c => c.Name.StartsWith("phase 4 contracts implemented only", StringComparison.Ordinal))
                .All(c => c.Status == Phase04CheckStatus.Passed), Is.True, Phase04Checks.Format(d.Checks));
            if (d.EquipmentModelType == null)
                Assert.That(d.Checks.Any(c => c.Status == Phase04CheckStatus.Pending && c.Detail.Contains("D1")), Is.True);
            if (d.AuthorityType == null)
                Assert.That(d.Checks.Any(c => c.Status == Phase04CheckStatus.Pending && c.Detail.Contains("D2")), Is.True);
            Assert.That(d.Checks.Any(c => c.Status == Phase04CheckStatus.Passed && c.Kind == Phase04Kinds.Discovery && c.Name.Contains("IBaseEquipmentModel")),
                Is.False, "discovery lists implementations (Info); it never 'passes' one");
        }

        [Test]
        public void ToolingTypesAreNeverProductionCandidates()
        {
            Assert.That(Phase04ImplementationDiscovery.IsProductionCandidate(typeof(RecordingBaseSimulation)), Is.False);
            Assert.That(Phase04ImplementationDiscovery.IsProductionCandidate(typeof(RecordingEquipmentModel)), Is.False);
            Assert.That(Phase04ImplementationDiscovery.IsProductionCandidate(typeof(ScriptedActor)), Is.False);
        }

        [Test]
        public void RecordingBaseSimulationForwardsWithoutChangingTheModel()
        {
            BaseOperationsScenario s = BaseOperationsFixtures.Scenario();
            Phase04ImplementationDiscovery d = Phase04ImplementationDiscovery.Discover();
            if (d.BaseModelType == null) Assert.Inconclusive("no single production IOutpostSimulation");
            IOutpostSimulation plain = d.CreateBaseModel(s);
            var spy = new RecordingBaseSimulation(d.CreateBaseModel(s));
            plain.Reset(s.Expedition.Outpost.Simulation);
            spy.Reset(s.Expedition.Outpost.Simulation);
            var demand = new OutpostDemand(2, false, 0);
            plain.Advance(30, demand);
            spy.Advance(30, demand);
            Assert.That(spy.Snapshot.LifeSupport.Water.PotableKilograms, Is.EqualTo(plain.Snapshot.LifeSupport.Water.PotableKilograms));
            Assert.That(spy.Advances.Count, Is.EqualTo(1));
            Assert.That(spy.Advances[0].After.Potable, Is.LessThanOrEqualTo(spy.Advances[0].Before.Potable));
            if (!(spy.Inner is IAirServiceCapacity))
                Assert.Throws<InvalidOperationException>(() => spy.SetCarbonDioxideRemovalFactor(1), "a spy never fakes the D1 coupling");
        }

        // ---- helpers --------------------------------------------------------------------------------------------------

        private static BaseOperationsSnapshot Running(BaseOperationsScenario s, double t) =>
            BaseOperationsFixtures.Edit(s, BaseOperationsFixtures.Initial(s, 0, P1)).Expedition(e => e.Session(SessionState.Running).AtTime(t)).Build();

        private static OutpostSimulationSnapshot Water(BaseOperationsSnapshot from, double potableDelta, double foodDelta)
        {
            OutpostSimulationSnapshot r = from.Outpost.Resources;
            WaterStock w = r.LifeSupport.Water;
            FoodStock f = r.LifeSupport.Food;
            var life = new LifeSupportSnapshot(new WaterStock(w.PotableKilograms + potableDelta, w.UntreatedKilograms, w.PotableCapacityKilograms,
                w.UntreatedCapacityKilograms), new FoodStock(f.StoredKilograms + foodDelta, f.CapacityKilograms), r.LifeSupport.Cabin);
            return new OutpostSimulationSnapshot(r.Power, r.ServiceLoadRequestedWatts, r.ServiceLoadServedWatts, life);
        }

        private static void AssertViolation(BaseOperationsScenario s, string invariant, params BaseOperationsSnapshot[] stream)
        {
            var v = new BaseOperationsStreamValidator(s, Phase04Kinds.FixtureFake);
            foreach (BaseOperationsSnapshot x in stream) v.Observe(x);
            List<Phase04Check> results = v.Results("t");
            Assert.That(results.Any(r => r.Status == Phase04CheckStatus.Failed && r.Name.Contains(invariant)), Is.True, Phase04Checks.Format(results));
        }

        private static BaseOperationsScenario WithConsoleAt(BasePosition position)
        {
            BaseOperationsScenario f = BaseOperationsFixtures.Scenario();
            OutpostScenario o = f.Expedition.Outpost;
            var stations = o.Layout.Stations.Select(st => st.Id == f.Logistics.Console ? new StationPlacement(st.Id, position, st.InteractionRadiusMeters) : st);
            var layout = new OutpostLayout(o.Layout.Bounds, o.Layout.Obstacles, stations, o.Layout.SpawnPoints);
            var outpost = new OutpostScenario(o.Id, o.Provenance, layout, o.Movement, o.Tasks, o.Simulation);
            ExpeditionScenario p3 = f.Expedition;
            var expedition = new ExpeditionScenario(outpost, p3.Zones, p3.Airlock, p3.Racks, p3.Eva, p3.TaskRules, p3.Shelter, p3.Comms,
                null, p3.Rules, p3.SuitedSpeedFactor);
            return new BaseOperationsScenario(expedition, f.Equipment, f.WaterStation, f.AirStation, f.Operations, f.ObservationValiditySeconds,
                f.Logistics, f.Manifests, f.Gauges, f.Irreversible, f.ReportIntervalSeconds, f.Science, f.Provenance);
        }

        /// <summary>Test double of a player (validation only): records requests, answers what the test sets.</summary>
        public sealed class ScriptedActor : IBaseOperationsActor
        {
            public ScriptedActor(PlayerId player) => Membership = OutpostMembership.Joined(player);
            public OutpostMembership Membership { get; }
            public BaseOperationsSnapshot State;
            public string Answer;
            public readonly List<Tuple<BaseOperationAction, StationId, OperationChoice>> Requests = new List<Tuple<BaseOperationAction, StationId, OperationChoice>>();
            public bool HasPending => false;
            public bool TryGetState(out BaseOperationsSnapshot snapshot)
            {
                snapshot = State;
                return State != null;
            }
            public void Move(double x, double y) { }
            public bool Request(BaseOperationAction action, StationId station, OperationChoice choice, string manifestKey, long orderSequence)
            {
                Requests.Add(Tuple.Create(action, station, choice));
                return true;
            }
            public string TakeAnswer()
            {
                string a = Answer;
                Answer = null;
                return a;
            }
        }
    }
}
