using System;
using System.Collections.Generic;
using Trainer.Application.BaseOperations;
using Trainer.Application.Expedition;
using Trainer.Application.Incidents;
using Trainer.Application.Outpost;
using Trainer.Simulation.BaseOperations;
using UnityEngine;

namespace Trainer.Unity.Configuration
{
    /// <summary>
    /// Authoring data of the phase 5 living base (D2). It REUSES the phase 4 scenario asset (the current lunar map, stations and
    /// art are untouched) and only adds: the power panel station, the power relay spare, the director catalog and rules, the
    /// renewable science assignments, the single bounded upgrade and the synthetic episode rules. The phase 4 scripted processor
    /// fault is switched off here (the director owns degradations). Every number is a SYNTHETIC game parameter (no NASA
    /// probability, MTBF, hardware value, dose or clinical threshold). The historical NASA episode is not stored in this asset: the
    /// authority process loads the frozen DONKI dataset from StreamingAssets (<see cref="HistoricalDatasetFolder"/>).
    /// </summary>
    [CreateAssetMenu(menuName = "Trainer/Living Base Scenario Definition (Phase 5)", fileName = "LivingBaseScenarioDefinition")]
    public sealed class LivingBaseScenarioDefinition : ScriptableObject
    {
        [Serializable]
        public sealed class IncidentData
        {
            public string key;
            public IncidentType kind;
            public double weight = 1;
            public double cooldownSeconds = 240;
            public double warningSeconds = 10;
            public bool hasProcessorFailAfter;
            public double processorFailAfterSeconds = 300;
            public double powerFactor = 1;
            public double isolatedFactor = 1;
            public string spareKey;
        }

        [Serializable]
        public sealed class TemplateData
        {
            public string key;
            public string activityKey;
            public bool exterior = true;
            public int rewardCredits = 2;
            public double deadlineSeconds = 600;
            public double renewCooldownSeconds = 300;
        }

        public const string SyntheticProvenance =
            "ESCENARIO SINTÉTICO de fase 5 (base viva con contratiempos): plano lunar de fase 4 + panel eléctrico; director " +
            "sembrado con presupuesto, enfriamientos, anti-repetición y descansos. Tiempos, pesos y factores son parámetros de " +
            "juego ilustrativos: no son probabilidades, MTBF, valores de hardware de la NASA, dosis ni umbrales clínicos.";

        [Header("Label")]
        [SerializeField] private string displayName = "Fase 5 · Base viva con contratiempos (escenario sintético)";
        [SerializeField, TextArea(3, 8)] private string provenance = SyntheticProvenance;

        [Header("Phase 4 scenario (reused: map, stations, art)")]
        [SerializeField] private BaseOperationsScenarioDefinition baseOperations;

        [Header("Power panel (new station, common area)")]
        [SerializeField, Tooltip("Station key; BaseInteriorView shows the key as its label.")] private string powerStation = "panel-energia";
        [SerializeField] private double powerX = 27.0;
        [SerializeField] private double powerY = 12.0;
        [SerializeField] private double powerRadius = 1.4;
        [SerializeField] private string powerRelaySpare = "power-relay";
        [SerializeField] private int powerRelayCount = 1;
        [SerializeField] private int powerRelayCapacity = 2;

        [Header("Director catalog (synthetic)")]
        [SerializeField] private IncidentData[] catalog =
        {
            new IncidentData { key = "processor-filter-clog", kind = IncidentType.ProcessorDegradation, weight = 3, cooldownSeconds = 300,
                warningSeconds = 20, hasProcessorFailAfter = true, processorFailAfterSeconds = 360 },
            new IncidentData { key = "array-converter-trip", kind = IncidentType.PowerConverterTrip, weight = 2, cooldownSeconds = 300,
                warningSeconds = 10, powerFactor = 0.5, isolatedFactor = 1 },
            new IncidentData { key = "discharge-controller-fault", kind = IncidentType.PowerControllerFault, weight = 1, cooldownSeconds = 420,
                warningSeconds = 10, powerFactor = 0.4, isolatedFactor = 0, spareKey = "power-relay" },
            new IncidentData { key = "space-weather", kind = IncidentType.SpaceWeather, weight = 2, cooldownSeconds = 600 }
        };

        [Header("Director rules (synthetic)")]
        [SerializeField] private double evaluationIntervalSeconds = 30;
        [SerializeField] private double initialGraceSeconds = 150;
        [SerializeField] private double restAfterResolutionSeconds = 120;
        [SerializeField] private int maxSimultaneous = 2;
        [SerializeField] private double pressurePerEvaluation = 0.35;
        [SerializeField] private double minBatteryFractionForPowerIncident = 0.3;
        [SerializeField] private double recoveryObservationSeconds = 10;

        [Header("Power operations (s)")]
        [SerializeField] private double inspectSeconds = 4;
        [SerializeField] private double resetSeconds = 6;
        [SerializeField] private double replaceSeconds = 15;
        [SerializeField] private double powerObservationValiditySeconds = 60;

        [Header("Science assignments (renewable)")]
        [SerializeField] private TemplateData[] templates =
        {
            new TemplateData { key = "regolith-survey", activityKey = "regolith-sample", exterior = true, rewardCredits = 2,
                deadlineSeconds = 600, renewCooldownSeconds = 300 },
            new TemplateData { key = "crater-core-drill", activityKey = "crater-core", exterior = true, rewardCredits = 3,
                deadlineSeconds = 900, renewCooldownSeconds = 420 }
        };
        [SerializeField] private int offerSlots = 2;
        [SerializeField] private double offerLifetimeSeconds = 300;
        [SerializeField] private double postponeSeconds = 120;
        [SerializeField] private int maxAccepted = 1;

        [Header("Upgrade (one, bounded)")]
        [SerializeField] private string upgradeKey = "high-recovery-regulator";
        [SerializeField] private string upgradeStation = "water-recycler";
        [SerializeField] private int upgradeCreditCost = 3;
        [SerializeField] private string upgradeSpareKey = "processor-filter";
        [SerializeField] private double upgradeInstallSeconds = 20;
        [SerializeField] private double upgradeRecoveryFraction = 0.95;
        [SerializeField] private double upgradeAddedEssentialWatts = 15;

        [Header("Synthetic space-weather episodes (training, not forecasts)")]
        [SerializeField] private double minWatchLeadSeconds = 40;
        [SerializeField] private double maxWatchLeadSeconds = 80;
        [SerializeField] private double shelterWithinSeconds = 60;
        [SerializeField] private double minShelterPeriodSeconds = 90;
        [SerializeField] private double maxShelterPeriodSeconds = 150;
        [SerializeField] private double messageValiditySeconds = 60;

        [Header("Session")]
        [SerializeField, Tooltip("Mode requested by default; -trainer-phase5-mode historical|synthetic overrides it on the authority.")]
        private DataMode defaultMode = DataMode.Historical;
        [SerializeField, Tooltip("Default director seed; -trainer-phase5-seed N overrides it on the authority.")]
        private ulong defaultSeed = 20170910;
        [SerializeField, Tooltip("Folder under StreamingAssets holding manifest.json and episode.json.")]
        private string historicalDatasetFolder = "ScientificData/Phase05/donki-2017-09-10";

        public string DisplayName => displayName;
        public BaseOperationsScenarioDefinition BaseOperations => baseOperations;
        public DataMode DefaultMode => defaultMode;
        public ulong DefaultSeed => defaultSeed;
        public string HistoricalDatasetFolder => historicalDatasetFolder;
        public double TimeScale => baseOperations != null ? baseOperations.TimeScale : 1d;
        public double MaxStepSeconds => baseOperations != null ? baseOperations.MaxStepSeconds : ExpeditionAuthority.DefaultMaxStepSeconds;

        /// <summary>Null when valid; otherwise the first problem (also checked by the D5 validator).</summary>
        public string Validate()
        {
            if (baseOperations == null) return "The phase 4 BaseOperationsScenarioDefinition is not assigned.";
            string phase4 = baseOperations.Validate();
            if (phase4 != null) return "Phase 4 scenario: " + phase4;
            if (defaultMode == DataMode.Current) return "Current observation is not offered in phase 5.";
            if (string.IsNullOrWhiteSpace(historicalDatasetFolder) || historicalDatasetFolder.Contains("..")) return "Dataset folder must be a plain relative path.";
            try
            {
                ToScenario(null);
                return null;
            }
            catch (Exception e) when (e is ArgumentException || e is NullReferenceException || e is InvalidOperationException)
            {
                return e.Message;
            }
        }

        /// <summary>The phase 5 scenario: phase 4 scenario + power panel + relay spare, scripted fault off, plus the phase 5 rules.
        /// <paramref name="historical"/> is the loaded DONKI episode or null (synthetic, or fallback).</summary>
        public LivingBaseScenario ToScenario(SpaceWeatherEpisodeDefinition historical)
        {
            if (baseOperations == null) throw new InvalidOperationException("The phase 4 BaseOperationsScenarioDefinition is not assigned.");
            BaseOperationsScenario p4 = baseOperations.ToScenario();
            ExpeditionScenario e = p4.Expedition;
            OutpostScenario o = e.Outpost;
            var panel = new StationId(powerStation);
            var stations = new List<StationPlacement>(o.Layout.Stations) { new StationPlacement(panel, new BasePosition(powerX, powerY), powerRadius) };
            var layout = new OutpostLayout(o.Layout.Bounds, o.Layout.Obstacles, stations, o.Layout.SpawnPoints);
            var outpost = new OutpostScenario(o.Id, provenance, layout, o.Movement, o.Tasks, o.Simulation);
            var expedition = new ExpeditionScenario(outpost, e.Zones, e.Airlock, e.Racks, e.Eva, e.TaskRules, e.Shelter, e.Comms, null, e.Rules,
                e.SuitedSpeedFactor);

            WaterProcessorConfiguration w = p4.Equipment.WaterProcessor;
            var water = new WaterProcessorConfiguration(w.BatchKilograms, w.HoldCapacityKilograms, w.NominalRecoveryFraction, w.DegradedRecoveryFraction,
                w.NominalConductivityMicrosiemens, w.DegradedConductivityMicrosiemens, w.ProductSpecLimitMicrosiemens, null, w.FailAfterDegradedSeconds,
                w.FilterSpareKey);
            var spares = new List<SpareStock>(p4.Equipment.InitialSpares);
            if (!spares.Exists(s => s.Key == powerRelaySpare)) spares.Add(new SpareStock(powerRelaySpare, powerRelayCount, powerRelayCapacity));
            var equipment = new BaseEquipmentConfiguration(water, p4.Equipment.Air, spares, provenance);
            var baseScenario = new BaseOperationsScenario(expedition, equipment, p4.WaterStation, p4.AirStation, p4.Operations,
                p4.ObservationValiditySeconds, p4.Logistics, p4.Manifests, p4.Gauges, p4.Irreversible, p4.ReportIntervalSeconds, p4.Science, provenance);

            var entries = new List<IncidentDefinition>();
            foreach (IncidentData d in catalog ?? Array.Empty<IncidentData>())
                entries.Add(new IncidentDefinition(d.key, d.kind, d.weight, d.cooldownSeconds, d.warningSeconds,
                    d.hasProcessorFailAfter ? d.processorFailAfterSeconds : (double?)null, d.powerFactor, d.isolatedFactor,
                    string.IsNullOrEmpty(d.spareKey) ? null : d.spareKey));
            var templateList = new List<AssignmentTemplate>();
            foreach (TemplateData t in templates ?? Array.Empty<TemplateData>())
                templateList.Add(new AssignmentTemplate(t.key, t.activityKey, t.exterior, t.rewardCredits, t.deadlineSeconds, t.renewCooldownSeconds));

            return new LivingBaseScenario(baseScenario, panel, entries,
                new DirectorRules(evaluationIntervalSeconds, initialGraceSeconds, restAfterResolutionSeconds, maxSimultaneous, pressurePerEvaluation,
                    minBatteryFractionForPowerIncident, recoveryObservationSeconds),
                new PowerOperationRules(inspectSeconds, resetSeconds, replaceSeconds, powerObservationValiditySeconds),
                templateList, new AssignmentRules(offerSlots, offerLifetimeSeconds, postponeSeconds, maxAccepted),
                new UpgradeDefinition(upgradeKey, new StationId(upgradeStation), upgradeCreditCost, upgradeSpareKey, upgradeInstallSeconds,
                    upgradeRecoveryFraction, upgradeAddedEssentialWatts),
                historical,
                new SyntheticEpisodeRules(minWatchLeadSeconds, maxWatchLeadSeconds, shelterWithinSeconds, minShelterPeriodSeconds,
                    maxShelterPeriodSeconds, messageValiditySeconds),
                provenance);
        }
    }
}
