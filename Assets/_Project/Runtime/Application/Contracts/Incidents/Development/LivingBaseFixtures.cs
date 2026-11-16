using System;
using System.Collections.Generic;
using Trainer.Application.BaseOperations;
using Trainer.Application.Expedition;
using Trainer.Application.Incidents;
using Trainer.Application.Outpost;
using Trainer.Simulation.BaseOperations;

namespace Trainer.Application.Development
{
    /// <summary>
    /// DEVELOPMENT fixtures of phase 5 (labeled synthetic). They reuse the phase 4 fixture plan and add the power panel, the
    /// power spare and the incident catalog. Not the shipped scenario, not NASA data, never mounted in production.
    /// </summary>
    public static class LivingBaseFixtures
    {
        public const string ScenarioId = "fixture-living-base-p5";
        public const string Provenance =
            "FIXTURE SINTÉTICA de desarrollo (fase 5): plano de la fixture de fase 4 + panel eléctrico; catálogo de contratiempos y " +
            "ritmo del director ilustrativos. No son datos de la NASA, probabilidades, MTBF ni umbrales clínicos.";

        public static readonly StationId PowerPanel = new StationId("power-panel");
        public const string PowerRelay = "power-relay";
        public const string CraterOpportunity = "crater-core";
        public const string ProcessorKey = "processor-filter-clog";
        public const string TripKey = "array-converter-trip";
        public const string ControllerKey = "discharge-controller-fault";
        public const string WeatherKey = "space-weather";
        public const string UpgradeKey = "high-recovery-regulator";

        public static BaseOperationsScenario BaseScenario()
        {
            BaseOperationsScenario p4 = BaseOperationsFixtures.Scenario(null);
            var stations = new List<StationPlacement>(p4.Layout.Stations) { new StationPlacement(PowerPanel, new BasePosition(8.0, 6.0), 1.2) };
            OutpostScenario o = p4.Expedition.Outpost;
            var layout = new OutpostLayout(o.Layout.Bounds, o.Layout.Obstacles, stations, o.Layout.SpawnPoints);
            var outpost = new OutpostScenario(ScenarioId, Provenance, layout, o.Movement, o.Tasks, o.Simulation);
            ExpeditionScenario e = p4.Expedition;
            var expedition = new ExpeditionScenario(outpost, e.Zones, e.Airlock, e.Racks, e.Eva, e.TaskRules, e.Shelter, e.Comms, null, e.Rules,
                e.SuitedSpeedFactor);
            var spares = new List<SpareStock>(p4.Equipment.InitialSpares) { new SpareStock(PowerRelay, 1, 2) };
            var equipment = new BaseEquipmentConfiguration(p4.Equipment.WaterProcessor, p4.Equipment.Air, spares, Provenance);
            var science = new List<ScienceOpportunity>(p4.Science)
            {
                new ScienceOpportunity(CraterOpportunity, new[] { ExpeditionFixtures.CraterCore })
            };
            return new BaseOperationsScenario(expedition, equipment, p4.WaterStation, p4.AirStation, p4.Operations, p4.ObservationValiditySeconds,
                p4.Logistics, p4.Manifests, p4.Gauges, p4.Irreversible, p4.ReportIntervalSeconds, science, Provenance);
        }

        public static IncidentDefinition[] Catalog() => new[]
        {
            new IncidentDefinition(ProcessorKey, IncidentType.ProcessorDegradation, 3, 240, 20, 300, 1, 1, null),
            new IncidentDefinition(TripKey, IncidentType.PowerConverterTrip, 2, 240, 10, null, 0.5, 1, null),
            new IncidentDefinition(ControllerKey, IncidentType.PowerControllerFault, 1, 300, 10, null, 0.4, 0.0, PowerRelay),
            new IncidentDefinition(WeatherKey, IncidentType.SpaceWeather, 2, 420, 0, null, 1, 1, null)
        };

        public static LivingBaseScenario Scenario(SpaceWeatherEpisodeDefinition historical = null) => new LivingBaseScenario(
            BaseScenario(), PowerPanel, Catalog(),
            new DirectorRules(30, 120, 90, 2, 0.5, 0.25, 10),
            new PowerOperationRules(4, 6, 15, 60),
            new[]
            {
                new AssignmentTemplate("regolith-survey", BaseOperationsFixtures.RegolithOpportunity, true, 2, 600, 300),
                new AssignmentTemplate("crater-core-drill", CraterOpportunity, true, 3, 900, 420)
            },
            new AssignmentRules(2, 300, 120, 1),
            new UpgradeDefinition(UpgradeKey, BaseOperationsFixtures.WaterStation, 3, BaseOperationsFixtures.ProcessorFilter, 20, 0.95, 15),
            historical,
            new SyntheticEpisodeRules(30, 60, 45, 90, 150, 60),
            Provenance);

        /// <summary>A FIXTURE "historical" episode for tests of the historical path. Its records imitate the DONKI shape but the
        /// dataset id and hash are fixture values: it is NOT the shipped NASA dataset.</summary>
        public static SpaceWeatherEpisodeDefinition FixtureHistoricalEpisode()
        {
            var dataset = new DatasetManifest("fixture-historical", DataMode.Historical, 1, new string('0', 64), "2026-01-01T00:00:00Z",
                "FIXTURE: no distribuible como dato real.");
            var records = new[]
            {
                new SourceRecord("FIXTURE", SourceRecordKind.Notification, "FX-AL-001", 1, "fixture://al-001", null, "2000-01-01T00:00Z",
                    "2026-01-01T00:00:00Z", "FIXTURE: flare notice.", null),
                new SourceRecord("FIXTURE", SourceRecordKind.Notification, "FX-AL-002", 1, "fixture://al-002", "2000-01-01T00:30Z",
                    "2000-01-01T00:40Z", "2026-01-01T00:00:00Z", "FIXTURE: particle notice.",
                    new[] { new SourceMeasurement("proton flux >10 MeV", 10, "pfu", "GEO (Earth orbit)", ValueClassification.Observation) })
            };
            var messages = new[]
            {
                new EpisodeMessage("fx-watch", EpisodeMessageKind.Watch, MessageBasis.SourceNotification, 0, 60, null, null, "sw-watch-flare",
                    new[] { "FX-AL-001" }, "2000-01-01T00:00Z"),
                new EpisodeMessage("fx-instruction", EpisodeMessageKind.ShelterInstruction, MessageBasis.DidacticDecision, 30, 60, 75, null,
                    "sw-instruction", new[] { "FX-AL-002" }, "2000-01-01T00:40Z"),
                new EpisodeMessage("fx-end", EpisodeMessageKind.InstructionEnd, MessageBasis.DidacticDecision, 150, 60, null, "fx-instruction",
                    "sw-period-end", null, null)
            };
            return new SpaceWeatherEpisodeDefinition("fixture-historical-episode", DataMode.Historical, "FIXTURE histórica",
                "FIXTURE de desarrollo con forma DONKI; no es el dataset enviado.", "Cronología didáctica de fixture.", dataset, records, messages, 25, 140);
        }

        public static LivingBaseSetup SyntheticSetup(ulong seed = 42) => new LivingBaseSetup(DataMode.Synthetic, seed);

        public static DataStatus SyntheticData() => new DataStatus(DataMode.Synthetic, DataMode.Synthetic, DataAvailability.NotRequested, null, null);

        public static PowerEquipmentStatus NominalPower(LivingBaseScenario scenario)
        {
            var power = scenario.BaseOperations.Expedition.Outpost.Simulation.Power;
            return new PowerEquipmentStatus(scenario.PowerStation, false, false, EquipmentKnowledge.Unknown, 0, null, power.GenerationWatts,
                power.MaxDischargeWatts, 0);
        }

        public static UpgradeStatus UpgradeOf(LivingBaseScenario scenario, double? installedAt = null)
        {
            UpgradeDefinition u = scenario.Upgrade;
            return new UpgradeStatus(u.Key, installedAt.HasValue, u.CreditCost, u.SpareKey, u.RecoveryFraction, u.AddedEssentialWatts, installedAt);
        }

        /// <summary>Ready at revision 0, no incident, no offer yet, director in Grace.</summary>
        public static LivingBaseSnapshot Initial(LivingBaseScenario scenario, LivingBaseSetup setup, long epoch, params PlayerId[] players)
        {
            BaseOperationsSnapshot p4 = BaseOperationsFixtures.Initial(scenario.BaseOperations, epoch, players);
            return new LivingBaseSnapshotBuilder(scenario, setup, p4).Build();
        }

        public const string FixtureConfigurationHash = "f1x7f1x7f1x7f1x7f1x7f1x7f1x7f1x7f1x7f1x7f1x7f1x7f1x7f1x7f1x7f1x7";
    }

    /// <summary>DEVELOPMENT builder of phase 5 snapshots around a phase 4 snapshot (for fakes, previews and tests).</summary>
    public sealed class LivingBaseSnapshotBuilder
    {
        private readonly LivingBaseScenario scenario;
        private readonly LivingBaseSetup setup;
        private BaseOperationsSnapshot baseOperations;
        private DataStatus data;
        private DirectorStatus director;
        private readonly List<IncidentRecord> incidents = new List<IncidentRecord>();
        private PowerEquipmentStatus power;
        private SpaceWeatherStatus spaceWeather;
        private readonly List<AssignmentRecord> assignments = new List<AssignmentRecord>();
        private int credits, earned;
        private UpgradeStatus upgrade;
        private readonly List<IncidentOperationProgress> operations = new List<IncidentOperationProgress>();
        private readonly List<IncidentFeedback> feedback = new List<IncidentFeedback>();
        private readonly List<DirectorDecisionRecord> decisions = new List<DirectorDecisionRecord>();

        public LivingBaseSnapshotBuilder(LivingBaseScenario scenario, LivingBaseSetup setup, BaseOperationsSnapshot baseOperations)
        {
            this.scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
            this.setup = setup ?? throw new ArgumentNullException(nameof(setup));
            this.baseOperations = baseOperations ?? throw new ArgumentNullException(nameof(baseOperations));
            data = setup.RequestedMode == DataMode.Synthetic
                ? LivingBaseFixtures.SyntheticData()
                : new DataStatus(DataMode.Historical, DataMode.Synthetic, DataAvailability.FallbackSynthetic, null, "dataset-missing");
            director = new DirectorStatus(DirectorPhase.Halted, setup.Seed, DirectorStatus.CurrentPrngVersion, 0, LivingBaseFixtures.FixtureConfigurationHash);
            power = LivingBaseFixtures.NominalPower(scenario);
            upgrade = LivingBaseFixtures.UpgradeOf(scenario);
        }

        public LivingBaseSnapshotBuilder(LivingBaseScenario scenario, LivingBaseSnapshot previous, BaseOperationsSnapshot nextBase)
            : this(scenario, previous.Setup, nextBase)
        {
            data = previous.Data;
            director = previous.Director;
            incidents.AddRange(previous.Incidents);
            power = previous.Power;
            spaceWeather = previous.SpaceWeather;
            assignments.AddRange(previous.Assignments);
            credits = previous.Credits;
            earned = previous.CreditsEarned;
            upgrade = previous.Upgrade;
            operations.AddRange(previous.Operations);
            feedback.AddRange(previous.Feedback);
            decisions.AddRange(previous.Decisions);
        }

        public LivingBaseSnapshotBuilder Data(DataStatus value) { data = value ?? throw new ArgumentNullException(nameof(value)); return this; }
        public LivingBaseSnapshotBuilder Director(DirectorStatus value) { director = value ?? throw new ArgumentNullException(nameof(value)); return this; }
        public LivingBaseSnapshotBuilder Incident(IncidentRecord value)
        {
            incidents.RemoveAll(i => i.Id == value.Id);
            incidents.Add(value ?? throw new ArgumentNullException(nameof(value)));
            incidents.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
            return this;
        }
        public LivingBaseSnapshotBuilder Power(PowerEquipmentStatus value) { power = value ?? throw new ArgumentNullException(nameof(value)); return this; }
        public LivingBaseSnapshotBuilder SpaceWeather(SpaceWeatherStatus value) { spaceWeather = value; return this; }
        public LivingBaseSnapshotBuilder Assignment(AssignmentRecord value)
        {
            assignments.RemoveAll(a => a.Sequence == value.Sequence);
            assignments.Add(value ?? throw new ArgumentNullException(nameof(value)));
            assignments.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
            return this;
        }
        public LivingBaseSnapshotBuilder Credits(int value, int earnedTotal) { credits = value; earned = earnedTotal; return this; }
        public LivingBaseSnapshotBuilder Upgrade(UpgradeStatus value) { upgrade = value ?? throw new ArgumentNullException(nameof(value)); return this; }
        public LivingBaseSnapshotBuilder Operation(IncidentOperationProgress value) { operations.Add(value ?? throw new ArgumentNullException(nameof(value))); return this; }
        public LivingBaseSnapshotBuilder ClearOperations() { operations.Clear(); return this; }
        public LivingBaseSnapshotBuilder Feedback(IncidentFeedback value) { feedback.Add(value ?? throw new ArgumentNullException(nameof(value))); return this; }
        public LivingBaseSnapshotBuilder Decision(DirectorDecisionRecord value) { decisions.Add(value ?? throw new ArgumentNullException(nameof(value))); return this; }

        public LivingBaseSnapshot Build() => new LivingBaseSnapshot(baseOperations, setup, data, director, incidents, power, spaceWeather, assignments,
            credits, earned, upgrade, operations, feedback, decisions);
    }
}
