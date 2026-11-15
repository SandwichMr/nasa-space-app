using System;
using System.Collections.Generic;
using Trainer.Application.Outpost;
using Trainer.Simulation;
using Trainer.Simulation.LifeSupport;
using UnityEngine;

namespace Trainer.Unity.Configuration
{
    /// <summary>
    /// Authoring data of one phase 2 interior scenario: floor plan (the ONLY collision geometry, enforced by
    /// D3's roster), stations, tasks, initial resources, mission rules and clock. D2 owns it; D4 draws to match
    /// it and D5 validates it. Every number in the shipped asset is a labelled synthetic value, not NASA data.
    /// Conversion builds new immutable contract objects and never mutates the asset.
    ///
    /// Unity mapping (decided here, as SESSION_CONTRACT.md asks D2): world X = plan X, world Y = plan Y,
    /// 1 Unity unit = 1 m, plan origin = world origin; Z is only draw order.
    /// </summary>
    [CreateAssetMenu(menuName = "Trainer/Outpost Scenario Definition", fileName = "OutpostScenarioDefinition")]
    public sealed class OutpostScenarioDefinition : ScriptableObject
    {
        [Serializable] public sealed class PlanPoint { public double x; public double y; }

        [Serializable] public sealed class PlanRect { public double minX; public double minY; public double maxX; public double maxY; }

        [Serializable]
        public sealed class StationData
        {
            public string id;
            public double x;
            public double y;
            [Tooltip("Metres from the confirmed crew position to the station point (inclusive).")]
            public double interactionRadiusMeters = 1.2;
        }

        [Serializable] public sealed class ChangeData { public ResourceKind kind; public double deltaKilograms; }

        [Serializable]
        public sealed class TaskData
        {
            public string id;
            public string station;
            public TaskCategory category;
            [Tooltip("Simulated seconds of fully served work.")]
            public double durationSeconds;
            [Tooltip("Service load while in progress (W). Must be 0 for Science (it uses the phase 1 science channel).")]
            public double powerWatts;
            [Tooltip("Mass changes applied once at completion. Sum of deltas + boundary outflow must be 0.")]
            public ChangeData[] completionChanges = Array.Empty<ChangeData>();
            public double boundaryOutflowKilograms;
        }

        [Serializable]
        public sealed class ObjectiveData
        {
            public string key;
            public string[] tasks = Array.Empty<string>();
            public int targetCount = 1;
            public bool required = true;
        }

        [Header("Label")]
        [SerializeField] private string scenarioId = "interior-base";
        [SerializeField] private string displayName = "Fase 2 · Base interior (escenario sintético)";
        [SerializeField, TextArea(2, 6)] private string provenance =
            "Valores sintéticos ilustrativos. No representan hardware, datos ni requisitos de la NASA.";

        [Header("Floor plan (metres)")]
        [SerializeField] private PlanRect bounds = new PlanRect { maxX = 18, maxY = 10 };
        [SerializeField] private PlanRect[] obstacles = Array.Empty<PlanRect>();
        [SerializeField] private StationData[] stations = Array.Empty<StationData>();
        [SerializeField, Tooltip("Spawn point i belongs to crew slot i; their count is the maximum crew.")]
        private PlanPoint[] spawnPoints = Array.Empty<PlanPoint>();

        [Header("Crew movement")]
        [SerializeField] private double maxSpeedMetersPerSecond = 2;
        [SerializeField] private double crewRadiusMeters = 0.35;

        [Header("Tasks")]
        [SerializeField] private TaskData[] tasks = Array.Empty<TaskData>();

        [Header("Power (J / W) — phase 1 meaning")]
        [SerializeField] private double initialEnergyJoules = 12000;
        [SerializeField] private double capacityJoules = 20000;
        [SerializeField] private double generationWatts = 80;
        [SerializeField] private double essentialConsumptionWatts = 60;
        [SerializeField] private double scienceConsumptionWatts = 60;
        [SerializeField] private double maxChargeWatts = 100;
        [SerializeField] private double maxDischargeWatts = 250;

        [Header("Water and food (kg)")]
        [SerializeField] private double potableWaterKilograms = 20;
        [SerializeField] private double untreatedWaterKilograms = 6;
        [SerializeField] private double potableCapacityKilograms = 40;
        [SerializeField] private double untreatedCapacityKilograms = 10;
        [SerializeField] private double storedFoodKilograms = 15;
        [SerializeField] private double foodCapacityKilograms = 30;

        [Header("Cabin (one well-mixed zone)")]
        [SerializeField] private double cabinFreeGasVolumeCubicMeters = 40;
        [SerializeField] private double cabinGasTemperatureKelvin = 295;
        [SerializeField] private double initialOxygenPartialPressureKilopascals = 21;
        [SerializeField] private double initialCarbonDioxidePartialPressureKilopascals = 0.4;

        [Header("Mission (simulated seconds)")]
        [SerializeField] private double missionDurationSeconds = 240;
        [SerializeField] private ObjectiveData[] objectives = Array.Empty<ObjectiveData>();

        [Header("Clock")]
        [SerializeField, Tooltip("Simulated seconds per real second.")] private double timeScale = 1;
        [SerializeField, Tooltip("Longest simulated sub-step of the authority.")]
        private double maxStepSeconds = OutpostAuthority.DefaultMaxStepSeconds;

        public string ScenarioId => scenarioId;
        public string DisplayName => displayName;
        public string Provenance => provenance;
        public double TimeScale => timeScale;
        public double MaxStepSeconds => maxStepSeconds;

        /// <summary>Null when the asset builds a valid scenario and rules; otherwise the first problem found.
        /// Never modifies the asset.</summary>
        public string Validate()
        {
            if (!IsPositive(timeScale)) return "Time scale must be positive (0 would freeze the mission clock).";
            if (!IsPositive(maxStepSeconds)) return "Max step must be positive.";
            try
            {
                OutpostScenario scenario = ToScenario();
                OutpostMissionRules rules = ToRules();
                return rules.ProblemWith(scenario);
            }
            catch (Exception e) when (e is ArgumentException || e is NullReferenceException)
            {
                return e.Message;
            }
        }

        /// <summary>Builds the immutable scenario; throws ArgumentException on invalid data.</summary>
        public OutpostScenario ToScenario()
        {
            var obstacleList = new List<BaseRect>();
            foreach (PlanRect rect in obstacles ?? Array.Empty<PlanRect>()) obstacleList.Add(ToRect(rect));
            var stationList = new List<StationPlacement>();
            foreach (StationData station in stations ?? Array.Empty<StationData>())
                stationList.Add(new StationPlacement(new StationId(station.id), new BasePosition(station.x, station.y),
                    station.interactionRadiusMeters));
            var spawnList = new List<BasePosition>();
            foreach (PlanPoint point in spawnPoints ?? Array.Empty<PlanPoint>()) spawnList.Add(new BasePosition(point.x, point.y));
            var layout = new OutpostLayout(ToRect(bounds), obstacleList, stationList, spawnList);

            var taskList = new List<TaskDefinition>();
            foreach (TaskData task in tasks ?? Array.Empty<TaskData>())
            {
                var changes = new List<ResourceChange>();
                foreach (ChangeData change in task.completionChanges ?? Array.Empty<ChangeData>())
                    changes.Add(new ResourceChange(change.kind, change.deltaKilograms));
                var effect = changes.Count == 0 && task.boundaryOutflowKilograms == 0d
                    ? ResourceEffect.None : new ResourceEffect(changes, task.boundaryOutflowKilograms);
                taskList.Add(new TaskDefinition(new TaskTypeId(task.id), new StationId(task.station), task.category,
                    task.durationSeconds, task.powerWatts, effect));
            }

            var simulation = new OutpostConfiguration(
                ToPowerConfiguration(),
                new LifeSupportConfiguration(
                    new WaterStock(potableWaterKilograms, untreatedWaterKilograms, potableCapacityKilograms, untreatedCapacityKilograms),
                    new FoodStock(storedFoodKilograms, foodCapacityKilograms),
                    new CabinConfiguration(cabinFreeGasVolumeCubicMeters, cabinGasTemperatureKelvin,
                        initialOxygenPartialPressureKilopascals, initialCarbonDioxidePartialPressureKilopascals)));

            return new OutpostScenario(scenarioId, provenance, layout,
                new CrewMovementSettings(maxSpeedMetersPerSecond, crewRadiusMeters), taskList, simulation);
        }

        /// <summary>Builds the immutable mission rules; throws ArgumentException on invalid data.</summary>
        public OutpostMissionRules ToRules()
        {
            var list = new List<OutpostObjectiveRule>();
            foreach (ObjectiveData objective in objectives ?? Array.Empty<ObjectiveData>())
            {
                var ids = new List<TaskTypeId>();
                foreach (string task in objective.tasks ?? Array.Empty<string>()) ids.Add(new TaskTypeId(task));
                list.Add(new OutpostObjectiveRule(objective.key, ids, objective.targetCount, objective.required));
            }
            return new OutpostMissionRules(missionDurationSeconds, list);
        }

        public PowerConfiguration ToPowerConfiguration()
        {
            try
            {
                return new PowerConfiguration(initialEnergyJoules, capacityJoules, generationWatts,
                    essentialConsumptionWatts, scienceConsumptionWatts, maxChargeWatts, maxDischargeWatts);
            }
            catch (ArgumentException e)
            {
                throw new ArgumentException("Power values must be finite, nonnegative and initial energy within capacity.", e);
            }
        }

        /// <summary>Creates the single session authority for this scenario over D1's simulation and D3's roster.
        /// The roster must have been built for <see cref="ToScenario"/>'s layout and movement settings.</summary>
        public OutpostAuthority CreateAuthority(OutpostScenario scenario, IOutpostSimulation simulation, ICrewRoster roster)
        {
            string problem = Validate();
            if (problem != null) throw new InvalidOperationException($"OutpostScenarioDefinition '{name}': {problem}");
            return new OutpostAuthority(scenario, ToRules(), simulation, roster, maxStepSeconds);
        }

#if UNITY_EDITOR
        // Authoring feedback only: reports, never clamps or rewrites the values.
        private void OnValidate()
        {
            string problem = Validate();
            if (problem != null) Debug.LogWarning($"OutpostScenarioDefinition '{name}': {problem}", this);
        }
#endif

        private static BaseRect ToRect(PlanRect rect)
        {
            if (rect == null) throw new ArgumentException("Missing rectangle.");
            return new BaseRect(rect.minX, rect.minY, rect.maxX, rect.maxY);
        }

        private static bool IsPositive(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
    }
}
