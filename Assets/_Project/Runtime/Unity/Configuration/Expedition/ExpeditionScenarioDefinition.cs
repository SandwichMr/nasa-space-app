using System;
using System.Collections.Generic;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation;
using Trainer.Simulation.Eva;
using Trainer.Simulation.LifeSupport;
using UnityEngine;
using Plan = Trainer.Unity.Configuration.OutpostScenarioDefinition;

namespace Trainer.Unity.Configuration
{
    /// <summary>
    /// Authoring data of the phase 3 scenario (D2): the whole plan (phase 2 base + EVA wing + airlock + exterior), zones,
    /// airlock, suits and their reduced model, task rules, shelter, radio, the authored particle episode and the mission.
    /// The field defaults ARE the shipped synthetic scenario (the builder saves them as the asset), so tests and the asset
    /// agree. Every number is a labelled synthetic value: not NASA data, a suit specification or an EVA procedure.
    /// Conversion builds new immutable contract objects and never mutates the asset. Same Unity mapping as phase 2
    /// (world X = X, world Y = Y, 1 unit = 1 m). Deliberately exposes no parameterless method returning an
    /// OutpostScenario, so phase 2 tooling never mistakes it for an interior scenario.
    /// </summary>
    [CreateAssetMenu(menuName = "Trainer/Expedition Scenario Definition", fileName = "ExpeditionScenarioDefinition")]
    public sealed class ExpeditionScenarioDefinition : ScriptableObject
    {
        public enum RuleKind { Don, ChargeBattery, FillConsumables, SealCheck, ExteriorWork, PrepareShelter }

        [Serializable] public sealed class ZoneData { public CrewZone zone; public Plan.PlanRect area; }
        [Serializable] public sealed class SuitData { public string key; public string rackStation; public double oxygenKilograms; public double batteryJoules; public double feedwaterKilograms; }
        [Serializable] public sealed class ServiceData { public SuitService service; public double capacity; public double reserve; public double restRate; public double walkRate; public double workRate; }
        [Serializable] public sealed class RuleData { public string task; public RuleKind kind; public string suitKey; public double chargeEfficiency = 1; public double toolWatts; }
        [Serializable] public sealed class MessageData { public string id; public AdvisoryKind kind; public double issuedAtSeconds; public bool hasShelterBy; public double shelterBySeconds; public double validUntilSeconds; public string supersedes; public string textKey; }
        [Serializable] public sealed class ObjectiveData { public string key; public ObjectiveRole role; public string[] tasks = Array.Empty<string>(); public int targetCount = 1; }

        private static Plan.PlanRect R(double minX, double minY, double maxX, double maxY) =>
            new Plan.PlanRect { minX = minX, minY = minY, maxX = maxX, maxY = maxY };
        private static Plan.PlanPoint P(double x, double y) => new Plan.PlanPoint { x = x, y = y };
        private static Plan.StationData S(string id, double x, double y, double r) =>
            new Plan.StationData { id = id, x = x, y = y, interactionRadiusMeters = r };
        private static Plan.TaskData T(string id, string station, TaskCategory category, double seconds, double watts) =>
            new Plan.TaskData { id = id, station = station, category = category, durationSeconds = seconds, powerWatts = watts };

        public const string SyntheticProvenance =
            "ESCENARIO SINTÉTICO de fase 3: base de fase 2 + ala EVA (preparación y refugio) + esclusa de 2 plazas + exterior " +
            "de 29 x 22 m. Valores ilustrativos y comprimidos para que una salida dure minutos; no representan trajes, hardware, " +
            "datos ni procedimientos de la NASA. La preparación es un checklist educativo, no un protocolo EVA real.";
        public const string SyntheticEpisodeProvenance =
            "EPISODIO SINTÉTICO autorado (reproducible, sin Internet): no es un evento real. Evento oculto 140–220 s; aviso a " +
            "150 s, actualización a 195 s y fin del periodo de refugio indicado por control a 235 s. Sin dosis, sin clase X ni pfu.";

        [Header("Label")]
        [SerializeField] private string scenarioId = "eva-shelter-synthetic";
        [SerializeField] private string displayName = "Fase 3 · Salida exterior, regreso y refugio (escenario sintético)";
        [SerializeField, TextArea(3, 8)] private string provenance = SyntheticProvenance;

        [Header("Plan (metres)")]
        [SerializeField] private Plan.PlanRect bounds = R(0, -6, 58, 16);
        [SerializeField] private Plan.PlanRect[] obstacles =
        {
            // Phase 2 base, unchanged.
            R(5.85, 0, 6.15, 3.5), R(5.85, 6.5, 6.15, 10), R(11.85, 0, 12.15, 3.5), R(11.85, 6.5, 12.15, 10),
            R(2.2, 9.3, 3.8, 10), R(8.2, 9.3, 9.8, 10), R(14.2, 9.3, 15.8, 10), R(14.2, 0, 15.8, 0.7),
            // Ground outside the buildings (not walkable).
            R(0, -6, 29.3, 0), R(0, 10, 29.3, 16),
            // Laboratory - EVA wing wall (door y 6.5..9.3), preparation room - shelter wall (door x 19..21).
            R(18, 0, 18.3, 6.5), R(18, 9.3, 18.3, 10), R(18.3, 4.85, 19, 5.15), R(21, 4.85, 26, 5.15),
            // Wing east wall around the inner hatch, airlock module, outer wall around the outer hatch.
            R(26, 0, 26.3, 6.9), R(26, 8.5, 26.3, 10), R(26.3, 0, 29.3, 6.2), R(26.3, 9.2, 29.3, 10),
            R(29, 6.2, 29.3, 6.9), R(29, 8.5, 29.3, 9.2),
            // Preparation consoles and shelter console.
            R(18.7, 9.3, 20.1, 10), R(20.6, 9.3, 22, 10), R(22.5, 9.3, 23.9, 10), R(24.2, 9.3, 25.6, 10), R(24, 0, 25.6, 0.5),
            // Rocks of the exterior.
            R(33, 9.5, 35, 11.5), R(40, -1, 43, 1.5), R(44.5, 3, 46, 9), R(49, 9, 51, 11), R(50.5, 14, 56.5, 14.6)
        };
        [SerializeField] private Plan.StationData[] stations =
        {
            S("air-scrubber", 3, 8.6, 1.2), S("water-recycler", 9, 8.6, 1.2), S("science-lab", 15, 8.6, 1.2), S("sample-analyzer", 15, 1.4, 1.2),
            S("suit-rack-a", 19.4, 8.6, 1.0), S("suit-rack-b", 21.3, 8.6, 1.0), S("suit-charger", 23.2, 8.6, 1.0), S("consumables-panel", 24.9, 8.6, 1.0),
            S("suit-check", 23.0, 6.0, 1.0), S("airlock-inner-panel", 26.15, 7.7, 1.3), S("airlock-outer-panel", 29.15, 7.7, 1.3),
            S("shelter-panel", 24.8, 1.1, 1.2), S("regolith-site", 37, 4, 1.4), S("crater-rim", 53.5, 12.5, 1.4)
        };
        [SerializeField, Tooltip("Spawn point i belongs to crew slot i (phase 2 habitat).")]
        private Plan.PlanPoint[] spawnPoints = { P(2, 2.5), P(3.5, 2.5), P(2, 4), P(3.5, 4) };

        [Header("Crew movement")]
        [SerializeField] private double maxSpeedMetersPerSecond = 2;
        [SerializeField] private double crewRadiusMeters = 0.35;
        [SerializeField, Tooltip("Speed multiplier while on suit supply, (0, 1].")] private double suitedSpeedFactor = 0.6;

        [Header("Tasks (phase 2 meaning)")]
        [SerializeField] private Plan.TaskData[] tasks =
        {
            T("check-cabin-air", "air-scrubber", TaskCategory.Support, 10, 40),
            new Plan.TaskData
            {
                id = "treat-water", station = "water-recycler", category = TaskCategory.Support, durationSeconds = 20, powerWatts = 150,
                completionChanges = new[]
                {
                    new Plan.ChangeData { kind = ResourceKind.UntreatedWater, deltaKilograms = -2.0 },
                    new Plan.ChangeData { kind = ResourceKind.PotableWater, deltaKilograms = 1.8 }
                },
                boundaryOutflowKilograms = 0.2
            },
            T("run-experiment", "science-lab", TaskCategory.Science, 30, 0),
            T("analyze-sample", "sample-analyzer", TaskCategory.Science, 25, 0),
            T("don-suit-a", "suit-rack-a", TaskCategory.Support, 5, 0), T("don-suit-b", "suit-rack-b", TaskCategory.Support, 5, 0),
            T("charge-suit", "suit-charger", TaskCategory.Support, 12, 200), T("fill-suit", "consumables-panel", TaskCategory.Support, 6, 0),
            T("check-suit", "suit-check", TaskCategory.Support, 5, 0), T("prepare-shelter", "shelter-panel", TaskCategory.Support, 6, 30),
            T("collect-regolith", "regolith-site", TaskCategory.Support, 15, 0), T("crater-core", "crater-rim", TaskCategory.Support, 20, 0)
        };

        [Header("Power (J / W), phase 1 meaning")]
        [SerializeField] private double initialEnergyJoules = 12000;
        [SerializeField] private double capacityJoules = 20000;
        [SerializeField] private double generationWatts = 80;
        [SerializeField] private double essentialConsumptionWatts = 60;
        [SerializeField] private double scienceConsumptionWatts = 60;
        [SerializeField] private double maxChargeWatts = 100;
        [SerializeField] private double maxDischargeWatts = 400;

        [Header("Water, food (kg) and cabin")]
        [SerializeField] private double potableWaterKilograms = 20;
        [SerializeField] private double untreatedWaterKilograms = 6;
        [SerializeField] private double potableCapacityKilograms = 40;
        [SerializeField] private double untreatedCapacityKilograms = 10;
        [SerializeField] private double storedFoodKilograms = 15;
        [SerializeField] private double foodCapacityKilograms = 30;
        [SerializeField] private double cabinFreeGasVolumeCubicMeters = 40;
        [SerializeField] private double cabinGasTemperatureKelvin = 295;
        [SerializeField] private double initialOxygenPartialPressureKilopascals = 21;
        [SerializeField] private double initialCarbonDioxidePartialPressureKilopascals = 0.4;

        [Header("Zones (first match wins)")]
        [SerializeField] private ZoneData[] zones =
        {
            new ZoneData { zone = CrewZone.Shelter, area = R(18.3, 0, 26, 4.85) },
            new ZoneData { zone = CrewZone.Airlock, area = R(26.3, 6.2, 29, 9.2) },
            new ZoneData { zone = CrewZone.Exterior, area = R(29, -6, 58, 16) },
            new ZoneData { zone = CrewZone.Interior, area = R(0, 0, 26.3, 10) }
        };

        [Header("Airlock")]
        [SerializeField] private Plan.PlanRect chamber = R(26.3, 6.2, 29, 9.2);
        [SerializeField] private Plan.PlanRect innerHatch = R(26, 6.9, 26.3, 8.5);
        [SerializeField] private Plan.PlanRect outerHatch = R(29, 6.9, 29.3, 8.5);
        [SerializeField] private string innerPanel = "airlock-inner-panel";
        [SerializeField] private string outerPanel = "airlock-outer-panel";
        [SerializeField] private int airlockCapacity = 2;
        [SerializeField] private double depressurizeSeconds = 8;
        [SerializeField] private double repressurizeSeconds = 8;
        [SerializeField] private double pumpPowerWatts = 120;
        [SerializeField] private double makeupGasPerCycleKilograms = 0.30;
        [SerializeField] private Plan.PlanPoint exteriorEntry = P(29.8, 7.7);

        [Header("Suits (reduced synthetic model)")]
        [SerializeField, TextArea(2, 4)] private string suitModelProvenance =
            "SINTÉTICO: modelo reducido de tres servicios con capacidades, reserva declarada y consumos comprimidos; no es un traje real.";
        [SerializeField] private ServiceData[] services =
        {
            new ServiceData { service = SuitService.Oxygen, capacity = 0.40, reserve = 0.08, restRate = 0.0016, walkRate = 0.0022, workRate = 0.0030 },
            new ServiceData { service = SuitService.Battery, capacity = 5000, reserve = 1000, restRate = 18, walkRate = 20, workRate = 22 },
            new ServiceData { service = SuitService.Feedwater, capacity = 0.80, reserve = 0.16, restRate = 0.0012, walkRate = 0.0028, workRate = 0.0045 }
        };
        [SerializeField] private SuitData[] suits =
        {
            new SuitData { key = "suit-a", rackStation = "suit-rack-a", oxygenKilograms = 0.12, batteryJoules = 2600, feedwaterKilograms = 0 },
            new SuitData { key = "suit-b", rackStation = "suit-rack-b", oxygenKilograms = 0.12, batteryJoules = 2600, feedwaterKilograms = 0 }
        };
        [SerializeField] private double oxygenRechargeKilograms = 2.0;
        [SerializeField] private double oxygenRechargeCapacityKilograms = 2.0;
        [SerializeField] private double makeupGasKilograms = 3.0;
        [SerializeField] private double makeupGasCapacityKilograms = 3.0;

        [Header("Phase 3 task rules")]
        [SerializeField] private RuleData[] rules =
        {
            new RuleData { task = "don-suit-a", kind = RuleKind.Don, suitKey = "suit-a" },
            new RuleData { task = "don-suit-b", kind = RuleKind.Don, suitKey = "suit-b" },
            new RuleData { task = "charge-suit", kind = RuleKind.ChargeBattery, chargeEfficiency = 1 },
            new RuleData { task = "fill-suit", kind = RuleKind.FillConsumables },
            new RuleData { task = "check-suit", kind = RuleKind.SealCheck },
            new RuleData { task = "prepare-shelter", kind = RuleKind.PrepareShelter },
            new RuleData { task = "collect-regolith", kind = RuleKind.ExteriorWork, toolWatts = 10 },
            new RuleData { task = "crater-core", kind = RuleKind.ExteriorWork, toolWatts = 20 }
        };

        [Header("Shelter and radio")]
        [SerializeField] private string shelterPanel = "shelter-panel";
        [SerializeField] private string shelterPrepareTask = "prepare-shelter";
        [SerializeField] private double shelterVentilationWatts = 40;
        [SerializeField] private Plan.PlanPoint antenna = P(27.8, 11);
        [SerializeField] private double radioRangeMeters = 20;

        [Header("Particle episode (authored, synthetic by default)")]
        [SerializeField] private bool hasEpisode = true;
        [SerializeField] private string episodeId = "synthetic-particle-episode";
        [SerializeField] private bool episodeSynthetic = true;
        [SerializeField, TextArea(2, 5)] private string episodeProvenance = SyntheticEpisodeProvenance;
        [SerializeField, Tooltip("Scenario truth, hidden until the session ends; never used to judge players.")]
        private double eventOnsetSeconds = 140;
        [SerializeField] private double eventEndSeconds = 220;
        [SerializeField] private MessageData[] messages =
        {
            new MessageData { id = "spe-warning-1", kind = AdvisoryKind.ShelterInstruction, issuedAtSeconds = 150, hasShelterBy = true,
                shelterBySeconds = 210, validUntilSeconds = 280, textKey = "particle-warning" },
            new MessageData { id = "spe-update-1", kind = AdvisoryKind.InstructionUpdate, issuedAtSeconds = 195, hasShelterBy = true,
                shelterBySeconds = 210, validUntilSeconds = 300, supersedes = "spe-warning-1", textKey = "particle-update" },
            new MessageData { id = "spe-end-1", kind = AdvisoryKind.InstructionEnd, issuedAtSeconds = 235, validUntilSeconds = 600,
                supersedes = "spe-update-1", textKey = "shelter-period-end" }
        };

        [Header("Mission (simulated seconds)")]
        [SerializeField] private double missionDurationSeconds = 360;
        [SerializeField] private ObjectiveData[] objectives =
        {
            new ObjectiveData { key = "crew-safe", role = ObjectiveRole.Safety },
            new ObjectiveData { key = "regolith-sample", role = ObjectiveRole.Primary, tasks = new[] { "collect-regolith" } },
            new ObjectiveData { key = "crater-core", role = ObjectiveRole.Optional, tasks = new[] { "crater-core" } }
        };

        [Header("Clock")]
        [SerializeField, Tooltip("Simulated seconds per real second.")] private double timeScale = 1;
        [SerializeField] private double maxStepSeconds = ExpeditionAuthority.DefaultMaxStepSeconds;

        public string ScenarioId => scenarioId;
        public string DisplayName => displayName;
        public string Provenance => provenance;
        public double TimeScale => timeScale;
        public double MaxStepSeconds => maxStepSeconds;

        /// <summary>Null when the asset builds a valid scenario; otherwise the first problem. Never modifies the asset.</summary>
        public string Validate()
        {
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

        /// <summary>Builds the immutable phase 3 scenario; throws ArgumentException on invalid data.</summary>
        public ExpeditionScenario ToScenario()
        {
            var obstacleList = new List<BaseRect>();
            foreach (Plan.PlanRect rect in obstacles ?? Array.Empty<Plan.PlanRect>()) obstacleList.Add(Rect(rect));
            var stationList = new List<StationPlacement>();
            foreach (Plan.StationData s in stations ?? Array.Empty<Plan.StationData>())
                stationList.Add(new StationPlacement(new StationId(s.id), new BasePosition(s.x, s.y), s.interactionRadiusMeters));
            var spawnList = new List<BasePosition>();
            foreach (Plan.PlanPoint p in spawnPoints ?? Array.Empty<Plan.PlanPoint>()) spawnList.Add(Point(p));
            var layout = new OutpostLayout(Rect(bounds), obstacleList, stationList, spawnList);

            var taskList = new List<TaskDefinition>();
            foreach (Plan.TaskData t in tasks ?? Array.Empty<Plan.TaskData>())
            {
                var changes = new List<ResourceChange>();
                foreach (Plan.ChangeData c in t.completionChanges ?? Array.Empty<Plan.ChangeData>()) changes.Add(new ResourceChange(c.kind, c.deltaKilograms));
                ResourceEffect effect = changes.Count == 0 && t.boundaryOutflowKilograms == 0d ? ResourceEffect.None
                    : new ResourceEffect(changes, t.boundaryOutflowKilograms);
                taskList.Add(new TaskDefinition(new TaskTypeId(t.id), new StationId(t.station), t.category, t.durationSeconds, t.powerWatts, effect));
            }
            var simulation = new OutpostConfiguration(
                new PowerConfiguration(initialEnergyJoules, capacityJoules, generationWatts, essentialConsumptionWatts,
                    scienceConsumptionWatts, maxChargeWatts, maxDischargeWatts),
                new LifeSupportConfiguration(
                    new WaterStock(potableWaterKilograms, untreatedWaterKilograms, potableCapacityKilograms, untreatedCapacityKilograms),
                    new FoodStock(storedFoodKilograms, foodCapacityKilograms),
                    new CabinConfiguration(cabinFreeGasVolumeCubicMeters, cabinGasTemperatureKelvin,
                        initialOxygenPartialPressureKilopascals, initialCarbonDioxidePartialPressureKilopascals)));
            var outpost = new OutpostScenario(scenarioId, provenance, layout,
                new CrewMovementSettings(maxSpeedMetersPerSecond, crewRadiusMeters), taskList, simulation);

            var zoneList = new List<ZoneRegion>();
            foreach (ZoneData z in zones ?? Array.Empty<ZoneData>()) zoneList.Add(new ZoneRegion(z.zone, Rect(z.area)));
            var airlock = new AirlockConfiguration(Rect(chamber), Rect(innerHatch), Rect(outerHatch), new StationId(innerPanel),
                new StationId(outerPanel), airlockCapacity, depressurizeSeconds, repressurizeSeconds, pumpPowerWatts,
                makeupGasPerCycleKilograms, Point(exteriorEntry));

            var serviceList = new List<SuitServiceModel>();
            foreach (ServiceData s in services ?? Array.Empty<ServiceData>())
                serviceList.Add(new SuitServiceModel(s.service, s.capacity, s.reserve, s.restRate, s.walkRate, s.workRate));
            var model = new SuitModelConfiguration(serviceList, suitModelProvenance);
            var suitList = new List<SuitDefinition>();
            var racks = new List<SuitRack>();
            foreach (SuitData s in suits ?? Array.Empty<SuitData>())
            {
                suitList.Add(new SuitDefinition(s.key, new SuitLevels(s.oxygenKilograms, s.batteryJoules, s.feedwaterKilograms)));
                racks.Add(new SuitRack(s.key, new StationId(s.rackStation)));
            }
            var eva = new EvaConfiguration(model, suitList, new EvaSupplyConfiguration(oxygenRechargeKilograms,
                oxygenRechargeCapacityKilograms, makeupGasKilograms, makeupGasCapacityKilograms));

            var ruleList = new List<ExpeditionTaskRule>();
            foreach (RuleData r in rules ?? Array.Empty<RuleData>())
            {
                var task = new TaskTypeId(r.task);
                switch (r.kind)
                {
                    case RuleKind.Don: ruleList.Add(ExpeditionTaskRule.Don(task, r.suitKey)); break;
                    case RuleKind.ChargeBattery: ruleList.Add(ExpeditionTaskRule.ChargeBattery(task, r.chargeEfficiency)); break;
                    case RuleKind.FillConsumables: ruleList.Add(ExpeditionTaskRule.FillConsumables(task)); break;
                    case RuleKind.SealCheck: ruleList.Add(ExpeditionTaskRule.SealCheck(task)); break;
                    case RuleKind.ExteriorWork: ruleList.Add(ExpeditionTaskRule.Exterior(task, r.toolWatts)); break;
                    case RuleKind.PrepareShelter: ruleList.Add(ExpeditionTaskRule.PrepareShelter(task)); break;
                    default: throw new ArgumentException($"Unknown rule kind for '{r.task}'.");
                }
            }

            ParticleEventEpisode episode = null;
            if (hasEpisode)
            {
                var list = new List<AdvisoryMessage>();
                foreach (MessageData m in messages ?? Array.Empty<MessageData>())
                    list.Add(new AdvisoryMessage(m.id, m.kind, m.issuedAtSeconds, m.hasShelterBy ? m.shelterBySeconds : (double?)null,
                        m.validUntilSeconds, string.IsNullOrEmpty(m.supersedes) ? null : m.supersedes, m.textKey));
                episode = new ParticleEventEpisode(episodeId, episodeSynthetic, episodeProvenance, null, eventOnsetSeconds, eventEndSeconds, list);
            }

            var objectiveList = new List<ExpeditionObjectiveRule>();
            foreach (ObjectiveData o in objectives ?? Array.Empty<ObjectiveData>())
            {
                var ids = new List<TaskTypeId>();
                foreach (string t in o.tasks ?? Array.Empty<string>()) ids.Add(new TaskTypeId(t));
                objectiveList.Add(new ExpeditionObjectiveRule(o.key, o.role, ids, o.targetCount));
            }
            return new ExpeditionScenario(outpost, zoneList, airlock, racks, eva, ruleList,
                new ShelterConfiguration(new StationId(shelterPanel), new TaskTypeId(shelterPrepareTask), shelterVentilationWatts),
                new CommsConfiguration(Point(antenna), radioRangeMeters), episode,
                new ExpeditionMissionRules(missionDurationSeconds, objectiveList), suitedSpeedFactor);
        }

#if UNITY_EDITOR
        // Authoring feedback only: reports, never clamps or rewrites the values.
        private void OnValidate()
        {
            string problem = Validate();
            if (problem != null) Debug.LogWarning($"ExpeditionScenarioDefinition '{name}': {problem}", this);
        }
#endif

        private static BaseRect Rect(Plan.PlanRect r)
        {
            if (r == null) throw new ArgumentException("Missing rectangle.");
            return new BaseRect(r.minX, r.minY, r.maxX, r.maxY);
        }

        private static BasePosition Point(Plan.PlanPoint p)
        {
            if (p == null) throw new ArgumentException("Missing point.");
            return new BasePosition(p.x, p.y);
        }
    }
}
