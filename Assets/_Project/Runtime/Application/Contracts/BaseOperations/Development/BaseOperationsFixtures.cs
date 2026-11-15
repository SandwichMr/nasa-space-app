using System;
using System.Collections.Generic;
using Trainer.Application.BaseOperations;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.LifeSupport;
using Trainer.Simulation;

namespace Trainer.Application.Development
{
    /// <summary>
    /// DEVELOPMENT ONLY. Synthetic, labelled phase 4 scenario and snapshot builder for D4 presentation work, contract tests
    /// and D5 fixtures. Built on the phase 3 fixture (<see cref="ExpeditionFixtures"/>) without changing it: same base, airlock
    /// and exterior, plus the air unit and the logistics console, and without the phase 2 <c>treat-water</c> task (the water
    /// station now has the phase 4 meaning). Every value is illustrative; none is NASA, hardware or clinical data.
    /// </summary>
    public static class BaseOperationsFixtures
    {
        public const string ScenarioId = "fixture-living-base";
        public const string Provenance =
            "FIXTURE SINTÉTICA de desarrollo (fase 4): reciclador con lote de 2 kg y avería programada, unidad de aire de dos lechos, " +
            "consola logística con un manifiesto. Valores ilustrativos; no son datos de la NASA, de hardware ni umbrales clínicos.";

        public static readonly StationId WaterStation = ExpeditionFixtures.WaterRecycler;
        public static readonly StationId AirStation = new StationId("air-scrubber");
        public static readonly StationId LogisticsConsole = new StationId("logistics-console");

        public const string ProcessorFilter = "processor-filter";
        public const string InletFilter = "inlet-filter";
        public const string StandardManifest = "standard-resupply";
        public const string RegolithOpportunity = "regolith-sample";

        /// <summary>Synthetic equipment: batch 2 kg (recovery 0.90 nominal / 0.75 degraded), product 20 µS/cm nominal and
        /// 80 µS/cm degraded against a 40 µS/cm acceptance line; filter restricted at 120 s, processor failed 180 s later if
        /// not repaired. Beds: 0.002 loading per crew-second, knee 0.6, factor 0.2 when full; active bed starts at 0.3.</summary>
        public static BaseEquipmentConfiguration Equipment(double? degradeAtSeconds = 120) => new BaseEquipmentConfiguration(
            new WaterProcessorConfiguration(2.0, 2.0, 0.90, 0.75, 20, 80, 40, degradeAtSeconds, 180, ProcessorFilter),
            new AirRevitalizationConfiguration(0.002, 0.6, 0.2, 0.3, 0.0, null, 0.5, InletFilter),
            new[] { new SpareStock(ProcessorFilter, 1, 3), new SpareStock(InletFilter, 1, 3) },
            Provenance);

        public static OperationDefinition[] Operations() => new[]
        {
            new OperationDefinition(BaseOperationAction.Inspect, OperationChoice.None, 4, 0),
            new OperationDefinition(BaseOperationAction.ProcessBatch, OperationChoice.None, 20, 150),
            new OperationDefinition(BaseOperationAction.RouteProduct, OperationChoice.AcceptToPotable, 3, 20),
            new OperationDefinition(BaseOperationAction.RouteProduct, OperationChoice.Reprocess, 3, 20),
            new OperationDefinition(BaseOperationAction.RecheckPotable, OperationChoice.None, 15, 60),
            new OperationDefinition(BaseOperationAction.ServiceAir, OperationChoice.SwitchBed, 5, 30),
            new OperationDefinition(BaseOperationAction.ServiceAir, OperationChoice.RegenerateStandbyBed, 25, 200),
            new OperationDefinition(BaseOperationAction.Isolate, OperationChoice.None, 0, 0),
            new OperationDefinition(BaseOperationAction.Intervene, OperationChoice.ReplaceProcessorFilter, 20, 0),
            new OperationDefinition(BaseOperationAction.Intervene, OperationChoice.ReplaceInletFilter, 12, 0),
            new OperationDefinition(BaseOperationAction.TestEquipment, OperationChoice.None, 8, 50),
            new OperationDefinition(BaseOperationAction.Restore, OperationChoice.None, 0, 0),
            new OperationDefinition(BaseOperationAction.PlaceOrder, OperationChoice.None, 3, 10),
            new OperationDefinition(BaseOperationAction.UnloadDelivery, OperationChoice.None, 10, 0)
        };

        /// <summary>Scenario lines (synthetic, not clinical): battery 4000 J, potable 8 kg, food 4 kg, O2 19 kPa, CO2 0.7 kPa.</summary>
        public static GaugeRule[] Gauges() => new[]
        {
            new GaugeRule(GaugeKind.BatteryEnergy, 4000, null),
            new GaugeRule(GaugeKind.PowerBalance, null, null),
            new GaugeRule(GaugeKind.PotableWater, 8, null),
            new GaugeRule(GaugeKind.UntreatedWater, null, null),
            new GaugeRule(GaugeKind.ProcessorHold, null, null),
            new GaugeRule(GaugeKind.StoredFood, 4, null),
            new GaugeRule(GaugeKind.CabinOxygen, 19, null),
            new GaugeRule(GaugeKind.CabinCarbonDioxide, null, 0.7)
        };

        public static ResupplyManifest Manifest() => new ResupplyManifest(StandardManifest, new[]
        {
            ManifestItem.Mass(ResourceKind.StoredFood, 6), ManifestItem.Mass(ResourceKind.PotableWater, 10),
            ManifestItem.Spares(ProcessorFilter, 1)
        }, 180, 30);

        public static BaseOperationsScenario Scenario(double? degradeAtSeconds = 120)
        {
            ExpeditionScenario p3 = ExpeditionFixtures.Scenario(false);
            OutpostScenario o = p3.Outpost;
            var stations = new List<StationPlacement>(o.Layout.Stations)
            {
                new StationPlacement(AirStation, new BasePosition(2.0, 6.0), 1.2),
                new StationPlacement(LogisticsConsole, new BasePosition(5.0, 6.0), 1.2)
            };
            var layout = new OutpostLayout(o.Layout.Bounds, o.Layout.Obstacles, stations, o.Layout.SpawnPoints);
            var tasks = new List<TaskDefinition>();
            foreach (TaskDefinition task in o.Tasks)
                if (task.Station != WaterStation) tasks.Add(task);
            var outpost = new OutpostScenario(ScenarioId, Provenance, layout, o.Movement, tasks, o.Simulation);
            var expedition = new ExpeditionScenario(outpost, p3.Zones, p3.Airlock, p3.Racks, p3.Eva, p3.TaskRules, p3.Shelter, p3.Comms,
                null, p3.Rules, p3.SuitedSpeedFactor);
            return new BaseOperationsScenario(expedition, Equipment(degradeAtSeconds), WaterStation, AirStation, Operations(), 60,
                new LogisticsRules(LogisticsConsole, 1, 120), new[] { Manifest() }, Gauges(), new IrreversibleRule(60), 120,
                new[] { new ScienceOpportunity(RegolithOpportunity, new[] { ExpeditionFixtures.CollectRegolith }) }, Provenance);
        }

        /// <summary>Ready at revision 0: players idle at the spawn points, machines online and unknown, empty hold, initial spares.</summary>
        public static BaseOperationsSnapshot Initial(BaseOperationsScenario scenario, long epoch, params PlayerId[] players)
        {
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            if (players == null) throw new ArgumentNullException(nameof(players));
            OutpostSessionSnapshot outpost = OutpostFixtures.Initial(scenario.Expedition.Outpost, epoch, players);
            return new BaseOperationsSnapshotBuilder(scenario, new ExpeditionSnapshotBuilder(scenario.Expedition, outpost, epoch)).Build();
        }

        /// <summary>A builder starting from <paramref name="previous"/> whose Build has Revision + 1.</summary>
        public static BaseOperationsSnapshotBuilder Edit(BaseOperationsScenario scenario, BaseOperationsSnapshot previous) =>
            new BaseOperationsSnapshotBuilder(scenario, previous);

        /// <summary>
        /// DEVELOPMENT gauges from a phase 2 snapshot (no trend). The authority publishes its own, with trends; this exists so
        /// fixtures and D4 previews follow the same unit, status and band rules.
        /// </summary>
        public static ResourceGauge[] GaugesFrom(BaseOperationsScenario scenario, OutpostSessionSnapshot outpost, double holdKilograms)
        {
            double t = outpost.SimulatedTimeSeconds;
            OutpostSimulationSnapshot r = outpost.Resources;
            PowerSnapshot p = r.Power;
            double balance = p.GenerationWatts - p.EssentialRequestedWatts - p.ScienceRequestedWatts - r.ServiceLoadRequestedWatts;
            WaterStock w = r.LifeSupport.Water;
            return new[]
            {
                ResourceGauge.Of(scenario.GaugeRuleOf(GaugeKind.BatteryEnergy), InstrumentReading.Valid(ReadingUnit.Joules, p.EnergyJoules, t), p.CapacityJoules, null),
                ResourceGauge.Of(scenario.GaugeRuleOf(GaugeKind.PowerBalance), InstrumentReading.Valid(ReadingUnit.Watts, balance, t), null, null),
                ResourceGauge.Of(scenario.GaugeRuleOf(GaugeKind.PotableWater), InstrumentReading.Valid(ReadingUnit.Kilograms, w.PotableKilograms, t), w.PotableCapacityKilograms, null),
                ResourceGauge.Of(scenario.GaugeRuleOf(GaugeKind.UntreatedWater), InstrumentReading.Valid(ReadingUnit.Kilograms, w.UntreatedKilograms, t), w.UntreatedCapacityKilograms, null),
                ResourceGauge.Of(scenario.GaugeRuleOf(GaugeKind.ProcessorHold), InstrumentReading.Valid(ReadingUnit.Kilograms, holdKilograms, t),
                    scenario.Equipment.WaterProcessor.HoldCapacityKilograms, null),
                ResourceGauge.Of(scenario.GaugeRuleOf(GaugeKind.StoredFood), InstrumentReading.Valid(ReadingUnit.Kilograms, r.LifeSupport.Food.StoredKilograms, t),
                    r.LifeSupport.Food.CapacityKilograms, null),
                ResourceGauge.Of(scenario.GaugeRuleOf(GaugeKind.CabinOxygen), FromGas(r.LifeSupport.Cabin.Oxygen), null, null),
                ResourceGauge.Of(scenario.GaugeRuleOf(GaugeKind.CabinCarbonDioxide), FromGas(r.LifeSupport.Cabin.CarbonDioxide), null, null)
            };
        }

        /// <summary>Phase 2 gas reading as a phase 4 instrument reading (same status, value and time).</summary>
        public static InstrumentReading FromGas(GasReading gas) =>
            new InstrumentReading(gas.Status, ReadingUnit.Kilopascals, gas.PartialPressureKilopascals, gas.ObservedAtSeconds);
    }

    /// <summary>
    /// DEVELOPMENT ONLY. Mutable copy of a phase 4 snapshot for fixtures. The phase 2 station availability follows the
    /// operations (Unavailable while one is in progress) and gauges are recomputed from the phase 2 resources unless set
    /// explicitly; everything else is checked by the snapshot constructor at <see cref="Build"/>.
    /// </summary>
    public sealed class BaseOperationsSnapshotBuilder
    {
        private readonly BaseOperationsScenario scenario;
        private readonly ExpeditionSnapshotBuilder expedition;
        private BaseSessionStatus session;
        private ResourceGauge[] gauges;
        private readonly EquipmentStatus[] equipment;
        private WaterTreatmentStatus water;
        private readonly List<SpareStock> spares;
        private readonly List<OperationProgress> operations;
        private readonly List<ResupplyOrder> orders;
        private readonly List<ActivityRecord> activities;
        private readonly List<DecisionRecord> decisions;
        private OperationsReport report;

        internal BaseOperationsSnapshotBuilder(BaseOperationsScenario scenario, ExpeditionSnapshotBuilder initial)
        {
            this.scenario = scenario;
            expedition = initial;
            session = BaseSessionStatus.Start(BaseSessionMode.Continuous, scenario.Irreversible);
            equipment = new[]
            {
                new EquipmentStatus(EquipmentKind.WaterProcessor, scenario.WaterStation, EquipmentOperatingState.Online, EquipmentKnowledge.Unknown,
                    Findings.None, 0, null, null),
                new EquipmentStatus(EquipmentKind.AirRevitalization, scenario.AirStation, EquipmentOperatingState.Online, EquipmentKnowledge.Unknown,
                    Findings.None, 0, null, null)
            };
            water = new WaterTreatmentStatus(0, InstrumentReading.Unavailable(ReadingUnit.MicrosiemensPerCentimeter),
                scenario.Equipment.WaterProcessor.ProductSpecLimitMicrosiemens, PotableQuality.Verified, 0, 0);
            spares = new List<SpareStock>(scenario.Equipment.InitialSpares);
            operations = new List<OperationProgress>();
            orders = new List<ResupplyOrder>();
            activities = new List<ActivityRecord>();
            decisions = new List<DecisionRecord>();
        }

        internal BaseOperationsSnapshotBuilder(BaseOperationsScenario scenario, BaseOperationsSnapshot previous)
        {
            this.scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
            if (previous == null) throw new ArgumentNullException(nameof(previous));
            expedition = ExpeditionFixtures.Edit(scenario.Expedition, previous.Expedition);
            session = previous.Session;
            equipment = new[] { previous.Equipment[0], previous.Equipment[1] };
            water = previous.Water;
            spares = new List<SpareStock>(previous.Spares);
            operations = new List<OperationProgress>(previous.Operations);
            orders = new List<ResupplyOrder>(previous.Orders);
            activities = new List<ActivityRecord>(previous.Activities);
            decisions = new List<DecisionRecord>(previous.Decisions);
            report = previous.LatestReport;
        }

        public double Now => expedition.Now;

        /// <summary>Edits the contained phase 3 state (members, suits, airlock, stations, resources, clock...).</summary>
        public BaseOperationsSnapshotBuilder Expedition(Action<ExpeditionSnapshotBuilder> edit)
        {
            (edit ?? throw new ArgumentNullException(nameof(edit)))(expedition);
            return this;
        }

        public BaseOperationsSnapshotBuilder Session(BaseSessionStatus value) { session = value ?? throw new ArgumentNullException(nameof(value)); return this; }

        /// <summary>Sets explicit gauges (otherwise recomputed from the phase 2 resources at Build).</summary>
        public BaseOperationsSnapshotBuilder Gauges(params ResourceGauge[] value) { gauges = value; return this; }

        public BaseOperationsSnapshotBuilder Equipment(EquipmentStatus value)
        {
            equipment[(int)(value ?? throw new ArgumentNullException(nameof(value))).Equipment] = value;
            return this;
        }

        public BaseOperationsSnapshotBuilder Water(WaterTreatmentStatus value) { water = value ?? throw new ArgumentNullException(nameof(value)); return this; }

        public BaseOperationsSnapshotBuilder Spare(SpareStock value)
        {
            int i = spares.FindIndex(s => s.Key == value.Key);
            if (i < 0) spares.Add(value);
            else spares[i] = value;
            return this;
        }

        /// <summary>Starts or updates an operation; its phase 2 station becomes Unavailable.</summary>
        public BaseOperationsSnapshotBuilder Operation(OperationProgress value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            int i = operations.FindIndex(o => o.Station == value.Station);
            if (i < 0) operations.Add(value);
            else operations[i] = value;
            expedition.Station(new StationState(value.Station, StationAvailability.Unavailable, null, null));
            return this;
        }

        /// <summary>Removes the operation at <paramref name="station"/>; the phase 2 station becomes Available.</summary>
        public BaseOperationsSnapshotBuilder EndOperation(StationId station)
        {
            operations.RemoveAll(o => o.Station == station);
            expedition.Station(StationState.Free(station));
            return this;
        }

        public BaseOperationsSnapshotBuilder Order(ResupplyOrder value)
        {
            int i = orders.FindIndex(o => o.Sequence == value.Sequence);
            if (i < 0) orders.Add(value);
            else orders[i] = value;
            return this;
        }

        /// <summary>Adds or replaces an activity (by sequence); the session's activity sequence follows the largest.</summary>
        public BaseOperationsSnapshotBuilder Activity(ActivityRecord value)
        {
            int i = activities.FindIndex(a => a.Sequence == value.Sequence);
            if (i < 0) activities.Add(value);
            else activities[i] = value;
            activities.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
            if (value.Sequence > session.ActivitySequence)
                session = new BaseSessionStatus(session.Mode, session.EssentialUnservedSeconds, session.EssentialUnservedGraceSeconds,
                    session.EndCause, session.EndedAtSeconds, value.Sequence, session.ExpeditionsFinished);
            return this;
        }

        public BaseOperationsSnapshotBuilder Decision(DecisionRecord value) { decisions.Add(value ?? throw new ArgumentNullException(nameof(value))); return this; }
        public BaseOperationsSnapshotBuilder Report(OperationsReport value) { report = value; return this; }

        public BaseOperationsSnapshot Build()
        {
            ExpeditionSessionSnapshot built = expedition.Build();
            ResourceGauge[] g = gauges ?? BaseOperationsFixtures.GaugesFrom(scenario, built.Outpost, water.HoldKilograms);
            return new BaseOperationsSnapshot(built, session, g, equipment, water, spares, operations, orders, activities, decisions, report);
        }
    }
}
