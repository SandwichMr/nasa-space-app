using System;
using System.Collections.Generic;
using Trainer.Application.BaseOperations;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.LifeSupport;
using UnityEngine;

namespace Trainer.Unity.Configuration
{
    /// <summary>
    /// Authoring data of the phase 4 living base (D2). It REUSES the phase 3 scenario asset (plan, zones, airlock, suits,
    /// tasks) instead of copying it, and adds only what phase 4 needs: the logistics console, the equipment of the two
    /// phase 2 stations that change meaning (water recycler and air scrubber lose their phase 2 tasks), operations, gauge
    /// lines, manifests, the irreversible rule, reports and science opportunities. The phase 3 particle episode is not used
    /// by default (no event director in phase 4).
    /// The field defaults ARE the shipped synthetic scenario. Every number is a labelled synthetic game value: not NASA data,
    /// hardware, water-quality data or a clinical threshold. Conversion builds new immutable contract objects.
    /// </summary>
    [CreateAssetMenu(menuName = "Trainer/Base Operations Scenario Definition", fileName = "BaseOperationsScenarioDefinition")]
    public sealed class BaseOperationsScenarioDefinition : ScriptableObject
    {
        public enum ItemKind { PotableWater, StoredFood, Spare }

        [Serializable] public sealed class SpareData { public string key; public int count; public int capacity; }
        [Serializable] public sealed class OperationData { public BaseOperationAction action; public OperationChoice choice; public double durationSeconds; public double powerWatts; }
        [Serializable] public sealed class ItemData { public ItemKind kind; public double kilograms; public string spareKey; public int count; }
        [Serializable] public sealed class ManifestData { public string key; public double leadSeconds; public double cancelWindowSeconds; public ItemData[] items = Array.Empty<ItemData>(); }
        [Serializable] public sealed class GaugeData { public GaugeKind kind; public bool hasReserve; public double reserveAt; public bool hasLimit; public double limitAt; }
        [Serializable] public sealed class OpportunityData { public string key; public string[] tasks = Array.Empty<string>(); }

        public const string SyntheticProvenance =
            "ESCENARIO SINTÉTICO de fase 4 (base viva): plano de fase 3 + consola logística; reciclador con lote de 2 kg y una " +
            "avería programada reproducible; unidad de aire de dos lechos; un manifiesto de reposición. Valores de juego " +
            "ilustrativos: no son datos de la NASA, de hardware, de calidad de agua ni umbrales clínicos.";

        private static OperationData Op(BaseOperationAction a, OperationChoice c, double s, double w) =>
            new OperationData { action = a, choice = c, durationSeconds = s, powerWatts = w };
        private static GaugeData G(GaugeKind k, double? reserve, double? limit) =>
            new GaugeData { kind = k, hasReserve = reserve.HasValue, reserveAt = reserve ?? 0, hasLimit = limit.HasValue, limitAt = limit ?? 0 };

        [Header("Label")]
        [SerializeField] private string scenarioId = "living-base-synthetic";
        [SerializeField] private string displayName = "Fase 4 · Base viva (escenario sintético)";
        [SerializeField, TextArea(3, 8)] private string provenance = SyntheticProvenance;

        [Header("Phase 3 plan (reused, not copied)")]
        [SerializeField] private ExpeditionScenarioDefinition expedition;
        [SerializeField] private bool keepPhase3Episode;
        [SerializeField] private string[] removedPhase2Tasks = { "treat-water", "check-cabin-air" };

        [Header("Stations")]
        [SerializeField] private string waterStation = "water-recycler";
        [SerializeField] private string airStation = "air-scrubber";
        [SerializeField] private string logisticsStation = "logistics-console";
        [SerializeField] private double logisticsX = 9.0;
        [SerializeField] private double logisticsY = 1.4;
        [SerializeField] private double logisticsRadius = 1.2;

        [Header("Water processor (synthetic)")]
        [SerializeField] private double batchKilograms = 2.0;
        [SerializeField] private double holdCapacityKilograms = 2.0;
        [SerializeField] private double nominalRecovery = 0.90;
        [SerializeField] private double degradedRecovery = 0.75;
        [SerializeField] private double nominalConductivity = 20;
        [SerializeField] private double degradedConductivity = 80;
        [SerializeField] private double productSpecLimit = 40;
        [SerializeField] private bool scheduleFault = true;
        [SerializeField] private double degradeAtSeconds = 240;
        [SerializeField] private bool failAfterDegraded = true;
        [SerializeField] private double failAfterDegradedSeconds = 300;
        [SerializeField] private string processorFilterKey = "processor-filter";

        [Header("Air revitalization (synthetic)")]
        [SerializeField] private double loadingPerCrewSecond = 0.002;
        [SerializeField] private double kneeLoading = 0.6;
        [SerializeField] private double saturatedFactor = 0.2;
        [SerializeField] private double initialActiveLoading = 0.3;
        [SerializeField] private double initialStandbyLoading = 0.0;
        [SerializeField] private bool scheduleInletRestriction;
        [SerializeField] private double inletRestrictedAtSeconds = 600;
        [SerializeField] private double restrictedFlowFraction = 0.5;
        [SerializeField] private string inletFilterKey = "inlet-filter";

        [Header("Spares")]
        [SerializeField] private SpareData[] spares =
        {
            new SpareData { key = "processor-filter", count = 1, capacity = 3 },
            new SpareData { key = "inlet-filter", count = 1, capacity = 3 }
        };

        [Header("Operations (time s, power W)")]
        [SerializeField] private OperationData[] operations =
        {
            Op(BaseOperationAction.Inspect, OperationChoice.None, 4, 0),
            Op(BaseOperationAction.ProcessBatch, OperationChoice.None, 20, 150),
            Op(BaseOperationAction.RouteProduct, OperationChoice.AcceptToPotable, 3, 20),
            Op(BaseOperationAction.RouteProduct, OperationChoice.Reprocess, 3, 20),
            Op(BaseOperationAction.RecheckPotable, OperationChoice.None, 15, 60),
            Op(BaseOperationAction.ServiceAir, OperationChoice.SwitchBed, 5, 30),
            Op(BaseOperationAction.ServiceAir, OperationChoice.RegenerateStandbyBed, 25, 200),
            Op(BaseOperationAction.Isolate, OperationChoice.None, 0, 0),
            Op(BaseOperationAction.Intervene, OperationChoice.ReplaceProcessorFilter, 20, 0),
            Op(BaseOperationAction.Intervene, OperationChoice.ReplaceInletFilter, 12, 0),
            Op(BaseOperationAction.TestEquipment, OperationChoice.None, 8, 50),
            Op(BaseOperationAction.Restore, OperationChoice.None, 0, 0),
            Op(BaseOperationAction.PlaceOrder, OperationChoice.None, 3, 10),
            Op(BaseOperationAction.UnloadDelivery, OperationChoice.None, 10, 0)
        };
        [SerializeField] private double observationValiditySeconds = 60;

        [Header("Logistics")]
        [SerializeField] private int maxOrdersInFlight = 1;
        [SerializeField] private double minSecondsBetweenOrders = 120;
        [SerializeField] private ManifestData[] manifests =
        {
            new ManifestData
            {
                key = "standard-resupply", leadSeconds = 180, cancelWindowSeconds = 30,
                items = new[]
                {
                    new ItemData { kind = ItemKind.StoredFood, kilograms = 6 },
                    new ItemData { kind = ItemKind.PotableWater, kilograms = 10 },
                    new ItemData { kind = ItemKind.Spare, spareKey = "processor-filter", count = 1 }
                }
            }
        };

        [Header("Gauge lines (synthetic, not clinical)")]
        [SerializeField] private GaugeData[] gauges =
        {
            G(GaugeKind.BatteryEnergy, 4000, null), G(GaugeKind.PowerBalance, null, null), G(GaugeKind.PotableWater, 8, null),
            G(GaugeKind.UntreatedWater, null, null), G(GaugeKind.ProcessorHold, null, null), G(GaugeKind.StoredFood, 4, null),
            G(GaugeKind.CabinOxygen, 19, null), G(GaugeKind.CabinCarbonDioxide, null, 0.7)
        };

        [Header("Session")]
        [SerializeField] private double essentialUnservedGraceSeconds = 60;
        [SerializeField] private double reportIntervalSeconds = 120;
        [SerializeField] private OpportunityData[] science =
        {
            new OpportunityData { key = "regolith-sample", tasks = new[] { "collect-regolith" } },
            new OpportunityData { key = "crater-core", tasks = new[] { "crater-core" } }
        };
        [SerializeField] private double timeScale = 1;
        [SerializeField] private double maxStepSeconds = ExpeditionAuthority.DefaultMaxStepSeconds;

        public string ScenarioId => scenarioId;
        public string DisplayName => displayName;
        public double TimeScale => timeScale;
        public double MaxStepSeconds => maxStepSeconds;
        public ExpeditionScenarioDefinition Expedition => expedition;

        /// <summary>Null when the asset builds a valid scenario; otherwise the first problem. Never modifies the asset.</summary>
        public string Validate()
        {
            if (expedition == null) return "The phase 3 ExpeditionScenarioDefinition is not assigned.";
            string phase3 = expedition.Validate();
            if (phase3 != null) return "Phase 3 plan: " + phase3;
            if (!(timeScale > 0) || double.IsInfinity(timeScale)) return "Time scale must be positive.";
            if (!(maxStepSeconds > 0) || double.IsInfinity(maxStepSeconds)) return "Max step must be positive.";
            try
            {
                ToScenario();
                return null;
            }
            catch (Exception e) when (e is ArgumentException || e is NullReferenceException || e is InvalidOperationException)
            {
                return e.Message;
            }
        }

        /// <summary>Builds the immutable phase 4 scenario; throws ArgumentException on invalid data.</summary>
        public BaseOperationsScenario ToScenario()
        {
            if (expedition == null) throw new InvalidOperationException("The phase 3 ExpeditionScenarioDefinition is not assigned.");
            ExpeditionScenario p3 = expedition.ToScenario();
            OutpostScenario o = p3.Outpost;
            var removed = new HashSet<string>(removedPhase2Tasks ?? Array.Empty<string>(), StringComparer.Ordinal);

            var stations = new List<StationPlacement>(o.Layout.Stations)
            {
                new StationPlacement(new StationId(logisticsStation), new BasePosition(logisticsX, logisticsY), logisticsRadius)
            };
            var layout = new OutpostLayout(o.Layout.Bounds, o.Layout.Obstacles, stations, o.Layout.SpawnPoints);
            var tasks = new List<TaskDefinition>();
            foreach (TaskDefinition task in o.Tasks)
                if (!removed.Contains(task.Id.Value)) tasks.Add(task);
            var rules = new List<ExpeditionTaskRule>();
            foreach (ExpeditionTaskRule rule in p3.TaskRules)
                if (!removed.Contains(rule.Task.Value)) rules.Add(rule);
            var outpost = new OutpostScenario(scenarioId, provenance, layout, o.Movement, tasks, o.Simulation);
            var expeditionScenario = new ExpeditionScenario(outpost, p3.Zones, p3.Airlock, p3.Racks, p3.Eva, rules, p3.Shelter, p3.Comms,
                keepPhase3Episode ? p3.Episode : null, p3.Rules, p3.SuitedSpeedFactor);

            var spareList = new List<SpareStock>();
            foreach (SpareData s in spares ?? Array.Empty<SpareData>()) spareList.Add(new SpareStock(s.key, s.count, s.capacity));
            var equipment = new BaseEquipmentConfiguration(
                new WaterProcessorConfiguration(batchKilograms, holdCapacityKilograms, nominalRecovery, degradedRecovery, nominalConductivity,
                    degradedConductivity, productSpecLimit, scheduleFault ? degradeAtSeconds : (double?)null,
                    failAfterDegraded ? failAfterDegradedSeconds : (double?)null, processorFilterKey),
                new AirRevitalizationConfiguration(loadingPerCrewSecond, kneeLoading, saturatedFactor, initialActiveLoading, initialStandbyLoading,
                    scheduleInletRestriction ? inletRestrictedAtSeconds : (double?)null, restrictedFlowFraction, inletFilterKey),
                spareList, provenance);

            var operationList = new List<OperationDefinition>();
            foreach (OperationData op in operations ?? Array.Empty<OperationData>())
                operationList.Add(new OperationDefinition(op.action, op.choice, op.durationSeconds, op.powerWatts));

            var manifestList = new List<ResupplyManifest>();
            foreach (ManifestData m in manifests ?? Array.Empty<ManifestData>())
            {
                var items = new List<ManifestItem>();
                foreach (ItemData item in m.items ?? Array.Empty<ItemData>())
                    items.Add(item.kind == ItemKind.Spare ? ManifestItem.Spares(item.spareKey, item.count)
                        : ManifestItem.Mass(item.kind == ItemKind.PotableWater ? ResourceKind.PotableWater : ResourceKind.StoredFood, item.kilograms));
                manifestList.Add(new ResupplyManifest(m.key, items, m.leadSeconds, m.cancelWindowSeconds));
            }

            var gaugeList = new List<GaugeRule>();
            foreach (GaugeData g in gauges ?? Array.Empty<GaugeData>())
                gaugeList.Add(new GaugeRule(g.kind, g.hasReserve ? g.reserveAt : (double?)null, g.hasLimit ? g.limitAt : (double?)null));

            var opportunities = new List<ScienceOpportunity>();
            foreach (OpportunityData s in science ?? Array.Empty<OpportunityData>())
            {
                var ids = new List<TaskTypeId>();
                foreach (string task in s.tasks ?? Array.Empty<string>()) ids.Add(new TaskTypeId(task));
                opportunities.Add(new ScienceOpportunity(s.key, ids));
            }

            return new BaseOperationsScenario(expeditionScenario, equipment, new StationId(waterStation), new StationId(airStation), operationList,
                observationValiditySeconds, new LogisticsRules(new StationId(logisticsStation), maxOrdersInFlight, minSecondsBetweenOrders),
                manifestList, gaugeList, new IrreversibleRule(essentialUnservedGraceSeconds), reportIntervalSeconds, opportunities, provenance);
        }
    }
}
