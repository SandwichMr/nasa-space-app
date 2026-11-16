using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Trainer.Application.BaseOperations;
using Trainer.Application.Outpost;
using Trainer.Simulation.Incidents;

namespace Trainer.Application.Incidents
{
    /// <summary>The phase 5 catalog of authored setbacks (P0). Water running low by consumption is EMERGENT, never an incident.</summary>
    public enum IncidentType
    {
        /// <summary>Water processor filter restricted (degraded, optional synthetic fail timer). Recovery: the phase 4 repair.</summary>
        ProcessorDegradation,
        /// <summary>Solar array converter tripped: generation derated. Recovery: inspect, then reset at the power panel.</summary>
        PowerConverterTrip,
        /// <summary>Battery discharge controller damaged: max discharge derated. Recovery: inspect, isolate, replace a part, restore.</summary>
        PowerControllerFault,
        /// <summary>Space-weather episode (historical or synthetic): watch → shelter instruction → end of shelter period.</summary>
        SpaceWeather
    }

    /// <summary>
    /// One catalog entry (immutable). Every time, weight and factor is a SYNTHETIC game parameter (not an NASA probability, MTBF
    /// or hardware value). <see cref="WarningSeconds"/> = preparation window between the visible symptom/watch and activation.
    /// </summary>
    public sealed class IncidentDefinition
    {
        public string Key { get; }
        public IncidentType Kind { get; }
        public double Weight { get; }
        public double CooldownSeconds { get; }
        public double WarningSeconds { get; }
        /// <summary>ProcessorDegradation: time from degraded to failed if not repaired (null = never fails).</summary>
        public double? ProcessorFailAfterSeconds { get; }
        /// <summary>Power kinds: factor applied while the fault is active (generation for a trip, discharge for a controller).</summary>
        public double PowerFactor { get; }
        /// <summary>PowerControllerFault: discharge factor while the controller is isolated for replacement.</summary>
        public double IsolatedFactor { get; }
        /// <summary>PowerControllerFault: spare consumed by the replacement.</summary>
        public string SpareKey { get; }

        public IncidentDefinition(string key, IncidentType kind, double weight, double cooldownSeconds, double warningSeconds,
            double? processorFailAfterSeconds, double powerFactor, double isolatedFactor, string spareKey)
        {
            Key = StableKey.Require(key, nameof(key));
            Kind = OutpostGuard.Defined(kind, nameof(kind));
            Weight = OutpostGuard.Positive(weight, nameof(weight));
            CooldownSeconds = OutpostGuard.NonNegative(cooldownSeconds, nameof(cooldownSeconds));
            WarningSeconds = OutpostGuard.NonNegative(warningSeconds, nameof(warningSeconds));
            if (processorFailAfterSeconds.HasValue) OutpostGuard.Positive(processorFailAfterSeconds.Value, nameof(processorFailAfterSeconds));
            if (kind != IncidentType.ProcessorDegradation && processorFailAfterSeconds.HasValue)
                throw new ArgumentException("Only the processor degradation has a fail timer.", nameof(processorFailAfterSeconds));
            ProcessorFailAfterSeconds = processorFailAfterSeconds;
            bool power = kind == IncidentType.PowerConverterTrip || kind == IncidentType.PowerControllerFault;
            if (power)
            {
                if (!(powerFactor >= 0d && powerFactor < 1d)) throw new ArgumentOutOfRangeException(nameof(powerFactor), "A power fault derates: [0, 1).");
            }
            else if (powerFactor != 1d) throw new ArgumentException("Only power faults carry a factor (use 1).", nameof(powerFactor));
            PowerFactor = powerFactor;
            if (kind == IncidentType.PowerControllerFault)
            {
                if (!(isolatedFactor >= 0d && isolatedFactor <= powerFactor)) throw new ArgumentOutOfRangeException(nameof(isolatedFactor));
                SpareKey = StableKey.Require(spareKey, nameof(spareKey));
            }
            else
            {
                if (isolatedFactor != 1d) throw new ArgumentException("Only the controller fault has an isolated factor (use 1).", nameof(isolatedFactor));
                if (spareKey != null) throw new ArgumentException("Only the controller fault names a spare.", nameof(spareKey));
            }
            IsolatedFactor = isolatedFactor;
        }
    }

    /// <summary>
    /// Director rules (immutable, synthetic game pacing): evaluation cadence, grace and rest windows, simultaneity budget and
    /// per-evaluation pressure. The director only evaluates at multiples of <see cref="EvaluationIntervalSeconds"/> of simulated
    /// Running time, never per frame, and never while Ready/Paused/Interrupted.
    /// </summary>
    public sealed class DirectorRules
    {
        public double EvaluationIntervalSeconds { get; }
        public double InitialGraceSeconds { get; }
        public double RestAfterResolutionSeconds { get; }
        public int MaxSimultaneous { get; }
        /// <summary>Probability in [0, 1] that an evaluation in Ready proposes a candidate (seeded PRNG).</summary>
        public double PressurePerEvaluation { get; }
        /// <summary>Battery fraction below which no power incident is started (viability).</summary>
        public double MinBatteryFractionForPowerIncident { get; }
        /// <summary>A recovered machine must stay nominal this long before its incident counts as resolved.</summary>
        public double RecoveryObservationSeconds { get; }

        public DirectorRules(double evaluationIntervalSeconds, double initialGraceSeconds, double restAfterResolutionSeconds,
            int maxSimultaneous, double pressurePerEvaluation, double minBatteryFractionForPowerIncident, double recoveryObservationSeconds)
        {
            EvaluationIntervalSeconds = OutpostGuard.Positive(evaluationIntervalSeconds, nameof(evaluationIntervalSeconds));
            InitialGraceSeconds = OutpostGuard.NonNegative(initialGraceSeconds, nameof(initialGraceSeconds));
            RestAfterResolutionSeconds = OutpostGuard.NonNegative(restAfterResolutionSeconds, nameof(restAfterResolutionSeconds));
            if (maxSimultaneous < 1 || maxSimultaneous > 3) throw new ArgumentOutOfRangeException(nameof(maxSimultaneous), "1..3 simultaneous incidents.");
            MaxSimultaneous = maxSimultaneous;
            if (!(pressurePerEvaluation >= 0d && pressurePerEvaluation <= 1d)) throw new ArgumentOutOfRangeException(nameof(pressurePerEvaluation));
            PressurePerEvaluation = pressurePerEvaluation;
            if (!(minBatteryFractionForPowerIncident >= 0d && minBatteryFractionForPowerIncident <= 1d))
                throw new ArgumentOutOfRangeException(nameof(minBatteryFractionForPowerIncident));
            MinBatteryFractionForPowerIncident = minBatteryFractionForPowerIncident;
            RecoveryObservationSeconds = OutpostGuard.NonNegative(recoveryObservationSeconds, nameof(recoveryObservationSeconds));
        }
    }

    /// <summary>Timed power-panel operations (synthetic). They take crew time only: the declared draw is 0 W.</summary>
    public sealed class PowerOperationRules
    {
        public double InspectSeconds { get; }
        public double ResetSeconds { get; }
        public double ReplaceSeconds { get; }
        public double ObservationValiditySeconds { get; }

        public PowerOperationRules(double inspectSeconds, double resetSeconds, double replaceSeconds, double observationValiditySeconds)
        {
            InspectSeconds = OutpostGuard.Positive(inspectSeconds, nameof(inspectSeconds));
            ResetSeconds = OutpostGuard.Positive(resetSeconds, nameof(resetSeconds));
            ReplaceSeconds = OutpostGuard.Positive(replaceSeconds, nameof(replaceSeconds));
            ObservationValiditySeconds = OutpostGuard.Positive(observationValiditySeconds, nameof(observationValiditySeconds));
        }
    }

    /// <summary>
    /// A renewable science assignment template (immutable). It is fulfilled by a REAL activity of the base: a phase 4
    /// <c>ActivityRecord</c> of kind ScienceAssignment with <see cref="ActivityKey"/> completed after acceptance. Costs are the real
    /// costs of that activity (energy, crew time, suit consumables for exterior work); the reward is bounded science credits.
    /// </summary>
    public sealed class AssignmentTemplate
    {
        public string Key { get; }
        public string ActivityKey { get; }
        public bool Exterior { get; }
        public int RewardCredits { get; }
        public double DeadlineSeconds { get; }
        public double RenewCooldownSeconds { get; }

        public AssignmentTemplate(string key, string activityKey, bool exterior, int rewardCredits, double deadlineSeconds, double renewCooldownSeconds)
        {
            Key = StableKey.Require(key, nameof(key));
            ActivityKey = StableKey.Require(activityKey, nameof(activityKey));
            Exterior = exterior;
            if (rewardCredits < 1 || rewardCredits > 4) throw new ArgumentOutOfRangeException(nameof(rewardCredits), "1..4 credits.");
            RewardCredits = rewardCredits;
            DeadlineSeconds = OutpostGuard.Positive(deadlineSeconds, nameof(deadlineSeconds));
            RenewCooldownSeconds = OutpostGuard.NonNegative(renewCooldownSeconds, nameof(renewCooldownSeconds));
        }
    }

    /// <summary>Assignment board rules (synthetic): open offers, offer lifetime, postpone delay and the credit ceiling.</summary>
    public sealed class AssignmentRules
    {
        public int OfferSlots { get; }
        public double OfferLifetimeSeconds { get; }
        public double PostponeSeconds { get; }
        public int MaxAccepted { get; }

        public AssignmentRules(int offerSlots, double offerLifetimeSeconds, double postponeSeconds, int maxAccepted)
        {
            if (offerSlots < 1 || offerSlots > 3) throw new ArgumentOutOfRangeException(nameof(offerSlots));
            OfferSlots = offerSlots;
            OfferLifetimeSeconds = OutpostGuard.Positive(offerLifetimeSeconds, nameof(offerLifetimeSeconds));
            PostponeSeconds = OutpostGuard.Positive(postponeSeconds, nameof(postponeSeconds));
            if (maxAccepted < 1 || maxAccepted > 2) throw new ArgumentOutOfRangeException(nameof(maxAccepted));
            MaxAccepted = maxAccepted;
        }
    }

    /// <summary>
    /// The single bounded equipment upgrade (immutable): a higher nominal processor recovery, paid with science credits and one
    /// spare, at the cost of a permanent extra essential load. Installed at most once per epoch; no tech tree.
    /// </summary>
    public sealed class UpgradeDefinition
    {
        public string Key { get; }
        public StationId Station { get; }
        public int CreditCost { get; }
        public string SpareKey { get; }
        public double InstallSeconds { get; }
        public double RecoveryFraction { get; }
        public double AddedEssentialWatts { get; }

        public UpgradeDefinition(string key, StationId station, int creditCost, string spareKey, double installSeconds, double recoveryFraction,
            double addedEssentialWatts)
        {
            Key = StableKey.Require(key, nameof(key));
            if (!station.IsValid) throw new ArgumentException("Station required.", nameof(station));
            Station = station;
            if (creditCost < 1 || creditCost > IncidentLimits.MaxCredits) throw new ArgumentOutOfRangeException(nameof(creditCost));
            CreditCost = creditCost;
            SpareKey = StableKey.Require(spareKey, nameof(spareKey));
            InstallSeconds = OutpostGuard.Positive(installSeconds, nameof(installSeconds));
            if (!(recoveryFraction > 0d && recoveryFraction <= IncidentEffectLimits.MaxUpgradedRecoveryFraction))
                throw new ArgumentOutOfRangeException(nameof(recoveryFraction));
            RecoveryFraction = recoveryFraction;
            if (!(addedEssentialWatts > 0d && addedEssentialWatts <= IncidentEffectLimits.MaxAddedEssentialWatts))
                throw new ArgumentOutOfRangeException(nameof(addedEssentialWatts), "The upgrade must cost a visible extra load.");
            AddedEssentialWatts = addedEssentialWatts;
        }
    }

    /// <summary>Seeded synthetic space-weather generator bounds (game seconds, synthetic). Every generated episode is labeled TRAINING.</summary>
    public sealed class SyntheticEpisodeRules
    {
        public double MinWatchLeadSeconds { get; }
        public double MaxWatchLeadSeconds { get; }
        public double ShelterWithinSeconds { get; }
        public double MinShelterPeriodSeconds { get; }
        public double MaxShelterPeriodSeconds { get; }
        public double MessageValiditySeconds { get; }

        public SyntheticEpisodeRules(double minWatchLeadSeconds, double maxWatchLeadSeconds, double shelterWithinSeconds,
            double minShelterPeriodSeconds, double maxShelterPeriodSeconds, double messageValiditySeconds)
        {
            MinWatchLeadSeconds = OutpostGuard.Positive(minWatchLeadSeconds, nameof(minWatchLeadSeconds));
            MaxWatchLeadSeconds = OutpostGuard.Positive(maxWatchLeadSeconds, nameof(maxWatchLeadSeconds));
            if (maxWatchLeadSeconds < minWatchLeadSeconds) throw new ArgumentException("Max lead below min.", nameof(maxWatchLeadSeconds));
            ShelterWithinSeconds = OutpostGuard.Positive(shelterWithinSeconds, nameof(shelterWithinSeconds));
            MinShelterPeriodSeconds = OutpostGuard.Positive(minShelterPeriodSeconds, nameof(minShelterPeriodSeconds));
            MaxShelterPeriodSeconds = OutpostGuard.Positive(maxShelterPeriodSeconds, nameof(maxShelterPeriodSeconds));
            if (maxShelterPeriodSeconds < minShelterPeriodSeconds) throw new ArgumentException("Max period below min.", nameof(maxShelterPeriodSeconds));
            MessageValiditySeconds = OutpostGuard.Positive(messageValiditySeconds, nameof(messageValiditySeconds));
        }
    }

    /// <summary>
    /// The phase 5 living base scenario (immutable): the COMPLETE phase 4 scenario (plan, machines, orders) plus the power panel,
    /// the incident catalog, director and assignment rules, the upgrade and the space-weather sources. The phase 4 scripted
    /// processor fault must be disabled here: the director owns every degradation (no duplicated legacy failure).
    /// </summary>
    public sealed class LivingBaseScenario
    {
        public BaseOperationsScenario BaseOperations { get; }
        public StationId PowerStation { get; }
        public IReadOnlyList<IncidentDefinition> Catalog { get; }
        public DirectorRules Director { get; }
        public PowerOperationRules PowerOperations { get; }
        public IReadOnlyList<AssignmentTemplate> Templates { get; }
        public AssignmentRules Assignments { get; }
        public UpgradeDefinition Upgrade { get; }
        /// <summary>The frozen historical episode, or null when the build ships none (Historical then falls back to synthetic).</summary>
        public SpaceWeatherEpisodeDefinition HistoricalEpisode { get; }
        public SyntheticEpisodeRules SyntheticEpisodes { get; }
        public string Provenance { get; }

        public LivingBaseScenario(BaseOperationsScenario baseOperations, StationId powerStation, IEnumerable<IncidentDefinition> catalog,
            DirectorRules director, PowerOperationRules powerOperations, IEnumerable<AssignmentTemplate> templates, AssignmentRules assignments,
            UpgradeDefinition upgrade, SpaceWeatherEpisodeDefinition historicalEpisode, SyntheticEpisodeRules syntheticEpisodes, string provenance)
        {
            BaseOperations = baseOperations ?? throw new ArgumentNullException(nameof(baseOperations));
            if (baseOperations.Equipment.WaterProcessor.DegradeAtSeconds.HasValue)
                throw new ArgumentException("Phase 5 disables the phase 4 scripted processor fault: the director owns degradations.", nameof(baseOperations));
            if (!baseOperations.Layout.TryGetStation(powerStation, out _)) throw new ArgumentException("The power panel must be a station of the plan.", nameof(powerStation));
            PowerStation = powerStation;
            var entries = new List<IncidentDefinition>(catalog ?? throw new ArgumentNullException(nameof(catalog)));
            if (entries.Count == 0 || entries.Count > IncidentLimits.MaxCatalogEntries || entries.Contains(null))
                throw new ArgumentException("1..MaxCatalogEntries entries.", nameof(catalog));
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (IncidentDefinition d in entries)
            {
                if (!keys.Add(d.Key)) throw new ArgumentException($"Duplicate catalog key '{d.Key}'.", nameof(catalog));
                if (d.SpareKey != null && !HasSpare(baseOperations, d.SpareKey))
                    throw new ArgumentException($"Catalog spare '{d.SpareKey}' is not a configured spare.", nameof(catalog));
            }
            Catalog = new ReadOnlyCollection<IncidentDefinition>(entries);
            Director = director ?? throw new ArgumentNullException(nameof(director));
            PowerOperations = powerOperations ?? throw new ArgumentNullException(nameof(powerOperations));
            var templateList = new List<AssignmentTemplate>(templates ?? throw new ArgumentNullException(nameof(templates)));
            if (templateList.Count == 0 || templateList.Count > IncidentLimits.MaxAssignmentTemplates || templateList.Contains(null))
                throw new ArgumentException("1..MaxAssignmentTemplates templates.", nameof(templates));
            var templateKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (AssignmentTemplate t in templateList)
            {
                if (!templateKeys.Add(t.Key)) throw new ArgumentException($"Duplicate template '{t.Key}'.", nameof(templates));
                if (!HasOpportunity(baseOperations, t.ActivityKey))
                    throw new ArgumentException($"Template '{t.Key}' names no science opportunity '{t.ActivityKey}'.", nameof(templates));
            }
            Templates = new ReadOnlyCollection<AssignmentTemplate>(templateList);
            Assignments = assignments ?? throw new ArgumentNullException(nameof(assignments));
            Upgrade = upgrade ?? throw new ArgumentNullException(nameof(upgrade));
            if (!baseOperations.Layout.TryGetStation(upgrade.Station, out _)) throw new ArgumentException("Upgrade station not in the plan.", nameof(upgrade));
            if (!HasSpare(baseOperations, upgrade.SpareKey)) throw new ArgumentException("Upgrade spare not configured.", nameof(upgrade));
            if (!(upgrade.RecoveryFraction > baseOperations.Equipment.WaterProcessor.NominalRecoveryFraction))
                throw new ArgumentException("The upgrade must raise the nominal recovery.", nameof(upgrade));
            if (historicalEpisode != null && historicalEpisode.Mode != DataMode.Historical)
                throw new ArgumentException("The historical slot holds a historical episode.", nameof(historicalEpisode));
            HistoricalEpisode = historicalEpisode;
            SyntheticEpisodes = syntheticEpisodes ?? throw new ArgumentNullException(nameof(syntheticEpisodes));
            if (string.IsNullOrWhiteSpace(provenance)) throw new ArgumentException("Provenance label required.", nameof(provenance));
            Provenance = provenance;
        }

        public IncidentDefinition DefinitionOf(string key)
        {
            foreach (IncidentDefinition d in Catalog)
                if (d.Key == key) return d;
            return null;
        }

        public AssignmentTemplate TemplateOf(string key)
        {
            foreach (AssignmentTemplate t in Templates)
                if (t.Key == key) return t;
            return null;
        }

        private static bool HasSpare(BaseOperationsScenario scenario, string key)
        {
            foreach (var s in scenario.Equipment.InitialSpares)
                if (s.Key == key) return true;
            return false;
        }

        private static bool HasOpportunity(BaseOperationsScenario scenario, string key)
        {
            foreach (ScienceOpportunity o in scenario.Science)
                if (o.Key == key) return true;
            return false;
        }
    }

    /// <summary>How the host starts the session: requested data mode and seed. Restart keeps it (new epoch, same setup).</summary>
    public sealed class LivingBaseSetup
    {
        public DataMode RequestedMode { get; }
        public ulong Seed { get; }

        public LivingBaseSetup(DataMode requestedMode, ulong seed)
        {
            RequestedMode = OutpostGuard.Defined(requestedMode, nameof(requestedMode));
            if (requestedMode == DataMode.Current) throw new ArgumentException("Current observation is not implemented in phase 5.", nameof(requestedMode));
            Seed = seed;
        }
    }
}
