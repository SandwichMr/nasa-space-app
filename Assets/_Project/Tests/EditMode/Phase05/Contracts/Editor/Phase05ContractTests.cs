using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.BaseOperations;
using Trainer.Application.Development;
using Trainer.Application.Expedition;
using Trainer.Application.Incidents;
using Trainer.Application.Outpost;
using Trainer.Simulation.Incidents;

namespace Trainer.Tests.Phase05.Contracts
{
    /// <summary>Phase 5 common base: value rules, data/time rules, snapshot invariants, immutability and the fake.</summary>
    public sealed class Phase05ContractTests
    {
        private static readonly Guid A = new Guid("00000000-0000-0000-0000-0000000000a1");
        private static readonly Guid B = new Guid("00000000-0000-0000-0000-0000000000b2");

        // ------------------------------------------------------------ power modifiers (D1 contract)

        [Test]
        public void PowerModifiersComposeByMultiplyingFactorsAndAddingWatts()
        {
            var state = PowerCapacityState.Compose(new[]
            {
                new PowerModifier(A, PowerModifierTarget.GenerationFactor, 0.5, "array-converter-trip"),
                new PowerModifier(B, PowerModifierTarget.GenerationFactor, 0.5, "other"),
                new PowerModifier(Guid.NewGuid(), PowerModifierTarget.EssentialLoadWatts, 15, "upgrade"),
                new PowerModifier(Guid.NewGuid(), PowerModifierTarget.DischargeFactor, 0.0, "isolated")
            });
            Assert.AreEqual(0.25, state.GenerationFactor, 1e-12);
            Assert.AreEqual(0.0, state.DischargeFactor);
            Assert.AreEqual(15, state.AddedEssentialWatts);
            Assert.AreEqual(1, PowerCapacityState.Compose(Array.Empty<PowerModifier>()).GenerationFactor);
        }

        [Test]
        public void PowerModifiersRefuseOutOfRangeValues()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PowerModifier(A, PowerModifierTarget.GenerationFactor, 1.2, "x"));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PowerModifier(A, PowerModifierTarget.DischargeFactor, -0.1, "x"));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PowerModifier(A, PowerModifierTarget.GenerationFactor, double.NaN, "x"));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PowerModifier(A, PowerModifierTarget.EssentialLoadWatts, 101, "x"));
            Assert.Throws<ArgumentException>(() => new PowerModifier(Guid.Empty, PowerModifierTarget.GenerationFactor, 0.5, "x"));
            Assert.Throws<ArgumentException>(() => new PowerModifier(A, PowerModifierTarget.GenerationFactor, 0.5, "Not A Key"));
        }

        [Test]
        public void PowerModifierContentComparisonDetectsReusedIdsWithOtherContent()
        {
            var first = new PowerModifier(A, PowerModifierTarget.GenerationFactor, 0.5, "trip");
            Assert.IsTrue(first.HasSameContent(new PowerModifier(A, PowerModifierTarget.GenerationFactor, 0.5, "trip")));
            Assert.IsFalse(first.HasSameContent(new PowerModifier(A, PowerModifierTarget.GenerationFactor, 0.4, "trip")));
        }

        // ------------------------------------------------------------ data, provenance and time

        [Test]
        public void SourceRecordsKeepUnknownTimesAsNullAndRefuseMalformedOnes()
        {
            var r = new SourceRecord("NASA CCMC DONKI", SourceRecordKind.Notification, "20170910-AL-002", 1, "https://example", null, "2017-09-10T16:41Z",
                null, "summary", null);
            Assert.IsNull(r.EventTimeUtc);
            Assert.IsNull(r.RetrievedUtc);
            Assert.Throws<ArgumentException>(() => new SourceRecord("p", SourceRecordKind.Flare, "id", 1, "u", "yesterday", null, null, "s", null));
            Assert.Throws<ArgumentException>(() => new SourceRecord("p", SourceRecordKind.Flare, "id with space", 1, "u", null, null, null, "s", null));
            Assert.Throws<ArgumentException>(() => new SourceRecord("p", SourceRecordKind.Flare, "id", 1, "u", null, null, null, new string('x', 300), null));
            Assert.Throws<ArgumentException>(() => new SourceRecord("p", SourceRecordKind.Flare, "id", 1, "u", null, null, null, "bell\u0007", null));
        }

        [Test]
        public void MissingMeasurementIsNullNeverZero()
        {
            var m = new SourceMeasurement("proton flux", null, "pfu", "GEO", ValueClassification.Observation);
            Assert.IsNull(m.Value);
            Assert.Throws<ArgumentOutOfRangeException>(() => new SourceMeasurement("q", double.PositiveInfinity, "u", "l", ValueClassification.Observation));
        }

        [Test]
        public void HistoricalDatasetNeedsItsHash()
        {
            Assert.Throws<ArgumentException>(() => new DatasetManifest("d", DataMode.Historical, 1, null, null, "terms"));
            Assert.Throws<ArgumentException>(() => new DatasetManifest("d", DataMode.Historical, 1, "XYZ", null, "terms"));
            Assert.DoesNotThrow(() => new DatasetManifest("d", DataMode.Synthetic, 1, null, null, "generated"));
        }

        [Test]
        public void TheEndOfAShelterPeriodIsNeverQuotedAsANasaAllClear()
        {
            Assert.Throws<ArgumentException>(() => new EpisodeMessage("end", EpisodeMessageKind.InstructionEnd, MessageBasis.SourceNotification, 10, 5, null,
                "ins", "t", new[] { "X" }, null));
        }

        [Test]
        public void SyntheticEpisodesQuoteNoSourceAndCurrentModeIsRefused()
        {
            Assert.Throws<ArgumentException>(() => new EpisodeMessage("w", EpisodeMessageKind.Watch, MessageBasis.SyntheticTraining, 0, 5, null, null, "t",
                new[] { "X" }, null));
            EpisodeMessage[] messages = SyntheticMessages();
            Assert.Throws<ArgumentException>(() => new SpaceWeatherEpisodeDefinition("e", DataMode.Current, "l", "p", "n", null, null, messages, 1, 2));
            Assert.DoesNotThrow(() => new SpaceWeatherEpisodeDefinition("e", DataMode.Synthetic, "l", "p", "n", null, null, messages, 1, 2));
            Assert.Throws<ArgumentException>(() => new LivingBaseSetup(DataMode.Current, 1));
        }

        [Test]
        public void EpisodeStructureIsWatchThenOneInstructionChainThenOneEnd()
        {
            EpisodeMessage watch = Message("w", EpisodeMessageKind.Watch, 0, null, null);
            EpisodeMessage ins = Message("i", EpisodeMessageKind.ShelterInstruction, 10, 30, null);
            EpisodeMessage upd = Message("u", EpisodeMessageKind.InstructionUpdate, 20, 30, "i");
            EpisodeMessage end = Message("e", EpisodeMessageKind.InstructionEnd, 60, null, "u");
            Assert.DoesNotThrow(() => Episode(watch, ins, upd, end));
            Assert.Throws<ArgumentException>(() => Episode(watch, ins), "no end");
            Assert.Throws<ArgumentException>(() => Episode(ins, watch, end), "watch after instruction");
            Assert.Throws<ArgumentException>(() => Episode(watch, end), "end without instruction");
            Assert.Throws<ArgumentException>(() => Episode(ins, end, upd), "nothing after the end");
            Assert.Throws<ArgumentException>(() => Episode(upd, ins, end), "out of order");
        }

        [Test]
        public void HistoricalMessagesMustQuoteRecordsOfTheirOwnDataset()
        {
            SpaceWeatherEpisodeDefinition fixture = LivingBaseFixtures.FixtureHistoricalEpisode();
            Assert.AreEqual(DataMode.Historical, fixture.Mode);
            var orphan = new EpisodeMessage("w", EpisodeMessageKind.Watch, MessageBasis.SourceNotification, 0, 10, null, null, "t", new[] { "MISSING" }, null);
            Assert.Throws<ArgumentException>(() => new SpaceWeatherEpisodeDefinition("e", DataMode.Historical, "l", "p", "n", fixture.Dataset, fixture.Records,
                new[] { orphan, fixture.Messages[1], fixture.Messages[2] }, 1, 2));
        }

        [Test]
        public void DataStatusSeparatesLoadedFallbackAndSynthetic()
        {
            var dataset = LivingBaseFixtures.FixtureHistoricalEpisode().Dataset;
            Assert.DoesNotThrow(() => new DataStatus(DataMode.Historical, DataMode.Historical, DataAvailability.Loaded, dataset, null));
            Assert.DoesNotThrow(() => new DataStatus(DataMode.Historical, DataMode.Synthetic, DataAvailability.FallbackSynthetic, null, "dataset-missing"));
            Assert.Throws<ArgumentException>(() => new DataStatus(DataMode.Historical, DataMode.Synthetic, DataAvailability.FallbackSynthetic, null, null),
                "a fallback must say why");
            Assert.Throws<ArgumentException>(() => new DataStatus(DataMode.Historical, DataMode.Historical, DataAvailability.NotRequested, null, null));
        }

        // ------------------------------------------------------------ scenario and catalog

        [Test]
        public void ScenarioRefusesThePhase4ScriptedFaultAndUnknownReferences()
        {
            Assert.DoesNotThrow(() => LivingBaseFixtures.Scenario());
            Assert.DoesNotThrow(() => LivingBaseFixtures.Scenario(LivingBaseFixtures.FixtureHistoricalEpisode()));
            LivingBaseScenario s = LivingBaseFixtures.Scenario();
            Assert.Throws<ArgumentException>(() => new LivingBaseScenario(BaseOperationsFixtures.Scenario(120), s.PowerStation, s.Catalog, s.Director,
                s.PowerOperations, s.Templates, s.Assignments, s.Upgrade, null, s.SyntheticEpisodes, "p"), "scripted fault must be off");
            Assert.Throws<ArgumentException>(() => new LivingBaseScenario(s.BaseOperations, new StationId("nowhere"), s.Catalog, s.Director,
                s.PowerOperations, s.Templates, s.Assignments, s.Upgrade, null, s.SyntheticEpisodes, "p"));
            var badTemplate = new AssignmentTemplate("x", "no-such-opportunity", false, 1, 10, 10);
            Assert.Throws<ArgumentException>(() => new LivingBaseScenario(s.BaseOperations, s.PowerStation, s.Catalog, s.Director,
                s.PowerOperations, new[] { badTemplate }, s.Assignments, s.Upgrade, null, s.SyntheticEpisodes, "p"));
        }

        [Test]
        public void CatalogEntriesCarryOnlyTheParametersOfTheirKind()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new IncidentDefinition("t", IncidentType.PowerConverterTrip, 1, 0, 0, null, 1.0, 1, null),
                "a fault derates");
            Assert.Throws<ArgumentException>(() => new IncidentDefinition("p", IncidentType.ProcessorDegradation, 1, 0, 0, null, 0.5, 1, null));
            Assert.Throws<ArgumentException>(() => new IncidentDefinition("c", IncidentType.PowerControllerFault, 1, 0, 0, null, 0.4, 0, null));
            Assert.Throws<ArgumentException>(() => new IncidentDefinition("w", IncidentType.SpaceWeather, 1, 0, 0, 30, 1, 1, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DirectorRules(30, 0, 0, 4, 0.5, 0.2, 0));
        }

        [Test]
        public void UpgradeMustRaiseRecoveryAndCostAVisibleLoad()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new UpgradeDefinition("u", new StationId("water-recycler"), 3, "processor-filter", 20, 0.99, 15));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new UpgradeDefinition("u", new StationId("water-recycler"), 3, "processor-filter", 20, 0.95, 0));
        }

        // ------------------------------------------------------------ commands

        [Test]
        public void CommandShapeSeparatesBoardActionsFromStationActions()
        {
            var panel = new StationId("power-panel");
            Assert.IsNull(new IncidentCommand(A, 0, IncidentAction.AcceptAssignment, default, 3, 0, 0).ShapeError());
            Assert.AreEqual(OutpostReasons.InvalidActionValue, new IncidentCommand(A, 0, IncidentAction.AcceptAssignment, panel, 3, 0, 0).ShapeError());
            Assert.AreEqual(OutpostReasons.InvalidActionValue, new IncidentCommand(A, 0, IncidentAction.AcceptAssignment, default, 0, 0, 0).ShapeError());
            Assert.IsNull(new IncidentCommand(A, 0, IncidentAction.InspectPower, panel, 0, 0, 2).ShapeError());
            Assert.AreEqual(OutpostReasons.InvalidActionValue, new IncidentCommand(A, 0, IncidentAction.ResetConverter, panel, 0, 0, 2).ShapeError(),
                "a reset needs the observation it is based on");
            Assert.IsNull(new IncidentCommand(A, 0, IncidentAction.ResetConverter, panel, 0, 5, 2).ShapeError());
            Assert.AreEqual(OutpostReasons.UnknownAction, new IncidentCommand(A, 0, (IncidentAction)99, panel, 0, 0, 0).ShapeError());
            Assert.AreEqual(OutpostReasons.InvalidActionValue, new IncidentCommand(A, -1, IncidentAction.InspectPower, panel, 0, 0, 0).ShapeError());
        }

        // ------------------------------------------------------------ snapshot invariants

        [Test]
        public void SnapshotPublishesOnlyRecordsQuotedByIssuedMessages()
        {
            SpaceWeatherEpisodeDefinition e = LivingBaseFixtures.FixtureHistoricalEpisode();
            var watchView = new EpisodeMessageView(e.Messages[0], 10, 70, null, AdvisoryValidity.Current);
            Assert.DoesNotThrow(() => Weather(e, new[] { e.Records[0] }, watchView));
            Assert.Throws<ArgumentException>(() => Weather(e, new[] { e.Records[0], e.Records[1] }, watchView),
                "the particle record belongs to a message not issued yet");
        }

        [Test]
        public void SnapshotRefusesFutureIncidentsAndCreditsAboveEarned()
        {
            LivingBaseScenario s = LivingBaseFixtures.Scenario();
            var player = new PlayerId(1);
            LivingBaseSnapshot initial = LivingBaseFixtures.Initial(s, LivingBaseFixtures.SyntheticSetup(), 0, player);
            Assert.AreEqual(0, initial.Revision);
            var future = new IncidentRecord(A, 1, IncidentType.PowerConverterTrip, "array-converter-trip", IncidentPhase.Warning, 500, null, null, null);
            Assert.Throws<ArgumentException>(() => new LivingBaseSnapshotBuilder(s, LivingBaseFixtures.SyntheticSetup(), initial.BaseOperations).Incident(future).Build());
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new LivingBaseSnapshotBuilder(s, LivingBaseFixtures.SyntheticSetup(), initial.BaseOperations).Credits(3, 2).Build());
        }

        [Test]
        public void IncidentAndAssignmentRecordsEnforceTheirLifecycle()
        {
            Assert.Throws<ArgumentException>(() => new IncidentRecord(A, 1, IncidentType.SpaceWeather, "w", IncidentPhase.Resolved, 0, 1, null, null));
            Assert.Throws<ArgumentException>(() => new IncidentRecord(A, 1, IncidentType.SpaceWeather, "w", IncidentPhase.Active, 0, null, null, null));
            Assert.DoesNotThrow(() => new IncidentRecord(A, 1, IncidentType.SpaceWeather, "w", IncidentPhase.Cancelled, 0, null, 3, "epoch-restarted"));
            var p = new PlayerId(1);
            Assert.Throws<ArgumentException>(() => new AssignmentRecord(1, "t", "a", false, AssignmentState.Completed, 0, 10, 1, 5, p, 2, false),
                "a completed assignment is rewarded");
            Assert.Throws<ArgumentException>(() => new AssignmentRecord(1, "t", "a", false, AssignmentState.Cancelled, 0, 10, 1, 5, p, 2, true),
                "only completion rewards");
            Assert.Throws<ArgumentException>(() => new AssignmentRecord(1, "t", "a", false, AssignmentState.Accepted, 0, 10, null, null, p, 2, false));
            Assert.DoesNotThrow(() => new AssignmentRecord(1, "t", "a", false, AssignmentState.Offered, 0, 10, null, null, PlayerId.None, 2, false));
        }

        [Test]
        public void PowerKnowledgeComesOnlyFromACurrentObservation()
        {
            var panel = new StationId("power-panel");
            Assert.Throws<ArgumentException>(() => new PowerEquipmentStatus(panel, false, false, EquipmentKnowledge.Degraded, 0, null, 100, 100, 0));
            var stale = new PowerObservation(1, new PlayerId(1), 0, 10, 0, PowerFindings.GenerationLow, 50, 100, false);
            Assert.Throws<ArgumentException>(() => new PowerEquipmentStatus(panel, false, false, EquipmentKnowledge.Degraded, 0, stale, 100, 100, 0));
            Assert.Throws<ArgumentException>(() => new PowerEquipmentStatus(panel, false, true, EquipmentKnowledge.Unknown, 0, null, 100, 100, 0),
                "replacing a part requires isolation");
        }

        // ------------------------------------------------------------ immutability

        [Test]
        public void PublishedPhase5TypesAreImmutable()
        {
            Type[] types = typeof(LivingBaseSnapshot).Assembly.GetTypes()
                .Where(t => t.IsPublic && t.IsClass && (t.Namespace == "Trainer.Application.Incidents" || t.Namespace == "Trainer.Simulation.Incidents")
                    && !t.IsAbstract && !typeof(Delegate).IsAssignableFrom(t))
                .ToArray();
            Assert.Greater(types.Length, 20);
            foreach (Type t in types)
            {
                foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                    Assert.IsTrue(p.SetMethod == null || !p.SetMethod.IsPublic, $"{t.Name}.{p.Name} has a public setter");
                foreach (FieldInfo f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                    Assert.IsTrue(f.IsInitOnly, $"{t.Name}.{f.Name} is a mutable public field");
            }
        }

        // ------------------------------------------------------------ fake

        [Test]
        public void FakeRaisesEventsInPhaseOrderAndNeverInventsResults()
        {
            LivingBaseScenario s = LivingBaseFixtures.Scenario();
            var player = new PlayerId(1);
            var fake = new FakeLivingBaseSessionPort();
            var order = new List<string>();
            fake.SnapshotChanged += _ => order.Add("2");
            fake.ExpeditionChanged += _ => order.Add("3");
            fake.BaseOperationsChanged += _ => order.Add("4");
            fake.LivingBaseChanged += _ => order.Add("5");
            fake.Publish(LivingBaseFixtures.Initial(s, LivingBaseFixtures.SyntheticSetup(), 0, player));
            CollectionAssert.AreEqual(new[] { "2", "3", "4", "5" }, order);
            fake.Join(player);

            var command = new IncidentCommand(A, 0, IncidentAction.AcceptAssignment, default, 1, 0, 0);
            fake.Submit(command);
            Assert.AreEqual(1, fake.PendingIncidentCommands.Count);
            Assert.Throws<InvalidOperationException>(() => fake.Accept(B), "unknown id is never answered");
            var results = new List<CommandResult>();
            fake.CommandCompleted += results.Add;
            fake.Accept(A);
            Assert.AreEqual(1, results.Count);
            Assert.IsTrue(results[0].Accepted);

            var abandoned = new List<string>();
            fake.IncidentCommandAbandoned += (c, r) => abandoned.Add(r);
            fake.Submit(new IncidentCommand(B, 0, IncidentAction.InspectPower, new StationId("power-panel"), 0, 0, 0));
            fake.Disconnect(OutpostReasons.ConnectionLost);
            CollectionAssert.AreEqual(new[] { OutpostReasons.ConnectionLost }, abandoned);
            fake.Submit(new IncidentCommand(Guid.NewGuid(), 0, IncidentAction.InspectPower, new StationId("power-panel"), 0, 0, 0));
            Assert.AreEqual(2, abandoned.Count, "not joined: abandoned with NotConnected");
            Assert.AreEqual(OutpostReasons.NotConnected, abandoned[1]);
        }

        // ------------------------------------------------------------ helpers

        private static EpisodeMessage Message(string id, EpisodeMessageKind kind, double offset, double? shelterBy, string supersedes) =>
            new EpisodeMessage(id, kind, MessageBasis.SyntheticTraining, offset, 20, shelterBy, supersedes, "t", null, null);

        private static SpaceWeatherEpisodeDefinition Episode(params EpisodeMessage[] messages) =>
            new SpaceWeatherEpisodeDefinition("e", DataMode.Synthetic, "l", "p", "n", null, null, messages, 1, 2);

        private static EpisodeMessage[] SyntheticMessages() => new[]
        {
            Message("w", EpisodeMessageKind.Watch, 0, null, null),
            Message("i", EpisodeMessageKind.ShelterInstruction, 10, 30, null),
            Message("e", EpisodeMessageKind.InstructionEnd, 60, null, "i")
        };

        private static SpaceWeatherStatus Weather(SpaceWeatherEpisodeDefinition e, SourceRecord[] visible, params EpisodeMessageView[] issued) =>
            new SpaceWeatherStatus(A, e.Key, e.Mode, e.Label, e.TimelineNote, e.Dataset, visible, issued, Array.Empty<AdvisoryReception>(),
                ShelterInstructionStatus.None, Array.Empty<MemberProtection>(), Array.Empty<ProtectionAssessment>());
    }
}
