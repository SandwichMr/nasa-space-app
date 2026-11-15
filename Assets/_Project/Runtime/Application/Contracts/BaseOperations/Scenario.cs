using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.BaseOperations;

namespace Trainer.Application.BaseOperations
{
    /// <summary>
    /// Time and power of one operation (configuration, immutable). Duration 0 = immediate (applied when accepted).
    /// A timed operation progresses only while its power is fully served, like a phase 2 Support task; energy is consumed
    /// while it progresses and never refunded.
    /// </summary>
    public sealed class OperationDefinition
    {
        public BaseOperationAction Action { get; }
        public OperationChoice Choice { get; }
        public double DurationSeconds { get; }
        public double PowerWatts { get; }

        public OperationDefinition(BaseOperationAction action, OperationChoice choice, double durationSeconds, double powerWatts)
        {
            Action = OutpostGuard.Defined(action, nameof(action));
            Choice = OutpostGuard.Defined(choice, nameof(choice));
            if (!BaseOperationCommand.IsChoiceAllowed(action, choice)) throw new ArgumentException("Choice not allowed for the action.", nameof(choice));
            if (action == BaseOperationAction.CancelOperation || action == BaseOperationAction.CancelOrder)
                throw new ArgumentException("Cancellations are immediate and take no definition.", nameof(action));
            DurationSeconds = OutpostGuard.NonNegative(durationSeconds, nameof(durationSeconds));
            PowerWatts = OutpostGuard.NonNegative(powerWatts, nameof(powerWatts));
            if (durationSeconds == 0d && powerWatts != 0d) throw new ArgumentException("An immediate operation draws no power.", nameof(powerWatts));
        }
    }

    /// <summary>A repeatable science opportunity (configuration): completing any of its tasks records a ScienceAssignment
    /// activity. It is an opportunity inside the session, never a global close.</summary>
    public sealed class ScienceOpportunity
    {
        public string Key { get; }
        public IReadOnlyList<TaskTypeId> Tasks { get; }

        public ScienceOpportunity(string key, IEnumerable<TaskTypeId> tasks)
        {
            Key = StableKey.Require(key, nameof(key));
            if (tasks == null) throw new ArgumentNullException(nameof(tasks));
            var copy = new List<TaskTypeId>();
            foreach (TaskTypeId task in tasks)
            {
                if (!task.IsValid) throw new ArgumentException("Task id required.", nameof(tasks));
                if (copy.Contains(task)) throw new ArgumentException("Duplicate task.", nameof(tasks));
                copy.Add(task);
            }
            if (copy.Count == 0) throw new ArgumentException("At least one task.", nameof(tasks));
            Tasks = new ReadOnlyCollection<TaskTypeId>(copy.ToArray());
        }
    }

    /// <summary>
    /// Static phase 4 configuration shared by the authority and every client of the same build (immutable). It CONTAINS the
    /// phase 3 <see cref="ExpeditionScenario"/> (layout, zones, airlock, suits, tasks) unchanged; the phase 3 mission duration
    /// and the closing objectives are not used by the continuous mode (sortie objectives become activities).
    /// The constructor refuses: equipment or logistics stations missing from the layout or shared, a phase 2 task offered at
    /// one of those stations (one meaning per station), a missing operation definition, manifests or opportunities that name
    /// unknown spares or tasks, and gauge rules that are not exactly one per <see cref="GaugeKind"/>.
    /// </summary>
    public sealed class BaseOperationsScenario
    {
        public ExpeditionScenario Expedition { get; }
        public BaseEquipmentConfiguration Equipment { get; }
        public StationId WaterStation { get; }
        public StationId AirStation { get; }
        public IReadOnlyList<OperationDefinition> Operations { get; }
        /// <summary>Simulated seconds an observation stays current if the machine state does not change.</summary>
        public double ObservationValiditySeconds { get; }
        public LogisticsRules Logistics { get; }
        public IReadOnlyList<ResupplyManifest> Manifests { get; }
        public IReadOnlyList<GaugeRule> Gauges { get; }
        public IrreversibleRule Irreversible { get; }
        /// <summary>Simulated seconds between non-terminal operations reports.</summary>
        public double ReportIntervalSeconds { get; }
        public IReadOnlyList<ScienceOpportunity> Science { get; }
        public string Provenance { get; }

        public string Id => Expedition.Id;
        public OutpostLayout Layout => Expedition.Layout;

        public BaseOperationsScenario(ExpeditionScenario expedition, BaseEquipmentConfiguration equipment, StationId waterStation,
            StationId airStation, IEnumerable<OperationDefinition> operations, double observationValiditySeconds, LogisticsRules logistics,
            IEnumerable<ResupplyManifest> manifests, IEnumerable<GaugeRule> gauges, IrreversibleRule irreversible,
            double reportIntervalSeconds, IEnumerable<ScienceOpportunity> science, string provenance)
        {
            Expedition = expedition ?? throw new ArgumentNullException(nameof(expedition));
            Equipment = equipment ?? throw new ArgumentNullException(nameof(equipment));
            Logistics = logistics ?? throw new ArgumentNullException(nameof(logistics));
            Irreversible = irreversible ?? throw new ArgumentNullException(nameof(irreversible));
            if (string.IsNullOrWhiteSpace(provenance)) throw new ArgumentException("Provenance label required.", nameof(provenance));
            Provenance = provenance;
            ObservationValiditySeconds = OutpostGuard.Positive(observationValiditySeconds, nameof(observationValiditySeconds));
            ReportIntervalSeconds = OutpostGuard.Positive(reportIntervalSeconds, nameof(reportIntervalSeconds));

            WaterStation = RequireStation(expedition, waterStation, nameof(waterStation));
            AirStation = RequireStation(expedition, airStation, nameof(airStation));
            RequireStation(expedition, logistics.Console, nameof(logistics));
            if (waterStation == airStation || waterStation == logistics.Console || airStation == logistics.Console)
                throw new ArgumentException("Water, air and logistics stations are distinct.");
            foreach (TaskDefinition task in expedition.Outpost.Tasks)
                if (task.Station == waterStation || task.Station == airStation || task.Station == logistics.Console)
                    throw new ArgumentException($"Task '{task.Id}' is offered at a phase 4 station; one meaning per station.", nameof(expedition));

            if (operations == null) throw new ArgumentNullException(nameof(operations));
            var ops = new List<OperationDefinition>();
            foreach (OperationDefinition op in operations)
            {
                if (op == null) throw new ArgumentException("Null operation.", nameof(operations));
                if (ops.Exists(o => o.Action == op.Action && o.Choice == op.Choice))
                    throw new ArgumentException($"Duplicate operation {op.Action}/{op.Choice}.", nameof(operations));
                ops.Add(op);
            }
            if (ops.Count > BaseOperationsLimits.MaxOperationDefinitions) throw new ArgumentException("Too many operations.", nameof(operations));
            foreach (BaseOperationAction action in Enum.GetValues(typeof(BaseOperationAction)))
                foreach (OperationChoice choice in Enum.GetValues(typeof(OperationChoice)))
                {
                    if (action == BaseOperationAction.CancelOperation || action == BaseOperationAction.CancelOrder) continue;
                    if (!BaseOperationCommand.IsChoiceAllowed(action, choice)) continue;
                    if (!ops.Exists(o => o.Action == action && o.Choice == choice))
                        throw new ArgumentException($"Missing operation definition {action}/{choice}.", nameof(operations));
                }
            Operations = new ReadOnlyCollection<OperationDefinition>(ops.ToArray());

            if (manifests == null) throw new ArgumentNullException(nameof(manifests));
            var manifestCopy = new List<ResupplyManifest>();
            foreach (ResupplyManifest manifest in manifests)
            {
                if (manifest == null) throw new ArgumentException("Null manifest.", nameof(manifests));
                if (manifestCopy.Exists(m => m.Key == manifest.Key)) throw new ArgumentException($"Duplicate manifest '{manifest.Key}'.", nameof(manifests));
                foreach (ManifestItem item in manifest.Items)
                    if (item.IsSpare && !HasSpare(equipment, item.SpareKey))
                        throw new ArgumentException($"Manifest '{manifest.Key}' names unknown spare '{item.SpareKey}'.", nameof(manifests));
                manifestCopy.Add(manifest);
            }
            if (manifestCopy.Count == 0 || manifestCopy.Count > BaseOperationsLimits.MaxManifests)
                throw new ArgumentException("Between 1 and MaxManifests manifests.", nameof(manifests));
            Manifests = new ReadOnlyCollection<ResupplyManifest>(manifestCopy.ToArray());

            if (gauges == null) throw new ArgumentNullException(nameof(gauges));
            var gaugeCopy = new List<GaugeRule>();
            foreach (GaugeRule gauge in gauges)
            {
                if (gauge == null) throw new ArgumentException("Null gauge rule.", nameof(gauges));
                if (gaugeCopy.Exists(g => g.Kind == gauge.Kind)) throw new ArgumentException($"Duplicate gauge {gauge.Kind}.", nameof(gauges));
                gaugeCopy.Add(gauge);
            }
            if (gaugeCopy.Count != Enum.GetValues(typeof(GaugeKind)).Length) throw new ArgumentException("Exactly one rule per gauge kind.", nameof(gauges));
            gaugeCopy.Sort((a, b) => a.Kind.CompareTo(b.Kind));
            Gauges = new ReadOnlyCollection<GaugeRule>(gaugeCopy.ToArray());

            if (science == null) throw new ArgumentNullException(nameof(science));
            var scienceCopy = new List<ScienceOpportunity>();
            foreach (ScienceOpportunity opportunity in science)
            {
                if (opportunity == null) throw new ArgumentException("Null opportunity.", nameof(science));
                if (scienceCopy.Exists(s => s.Key == opportunity.Key)) throw new ArgumentException($"Duplicate opportunity '{opportunity.Key}'.", nameof(science));
                foreach (TaskTypeId task in opportunity.Tasks)
                    if (!expedition.Outpost.TryGetTask(task, out _))
                        throw new ArgumentException($"Opportunity '{opportunity.Key}' names unknown task '{task}'.", nameof(science));
                scienceCopy.Add(opportunity);
            }
            if (scienceCopy.Count > BaseOperationsLimits.MaxScienceOpportunities) throw new ArgumentException("Too many opportunities.", nameof(science));
            Science = new ReadOnlyCollection<ScienceOpportunity>(scienceCopy.ToArray());
        }

        public OperationDefinition DefinitionOf(BaseOperationAction action, OperationChoice choice)
        {
            foreach (OperationDefinition op in Operations)
                if (op.Action == action && op.Choice == choice) return op;
            return null;
        }

        public bool TryGetManifest(string key, out ResupplyManifest manifest)
        {
            foreach (ResupplyManifest candidate in Manifests)
                if (candidate.Key == key)
                {
                    manifest = candidate;
                    return true;
                }
            manifest = null;
            return false;
        }

        public GaugeRule GaugeRuleOf(GaugeKind kind) => Gauges[(int)kind];

        /// <summary>The machine at <paramref name="station"/>, or null for any other station.</summary>
        public EquipmentKind? EquipmentAt(StationId station) =>
            station == WaterStation ? EquipmentKind.WaterProcessor
            : station == AirStation ? EquipmentKind.AirRevitalization
            : (EquipmentKind?)null;

        public StationId StationOf(EquipmentKind equipment) => equipment == EquipmentKind.WaterProcessor ? WaterStation : AirStation;

        private static StationId RequireStation(ExpeditionScenario expedition, StationId station, string name)
        {
            if (!station.IsValid || !expedition.Layout.TryGetStation(station, out _))
                throw new ArgumentException($"Station '{station}' is not in the layout.", name);
            return station;
        }

        private static bool HasSpare(BaseEquipmentConfiguration equipment, string key)
        {
            foreach (SpareStock spare in equipment.InitialSpares)
                if (spare.Key == key) return true;
            return false;
        }
    }
}
