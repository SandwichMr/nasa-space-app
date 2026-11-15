using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Trainer.Application.Outpost;
using Trainer.Simulation.Eva;

namespace Trainer.Application.Expedition
{
    /// <summary>
    /// Static phase 3 configuration shared by the authority and every client of the same build (immutable).
    /// <see cref="Outpost"/> is a complete phase 2 scenario with its phase 2 meaning: the WHOLE plan (base, EVA wing,
    /// airlock chamber with OPEN doorways, exterior), every task (base, suit preparation, exterior work, shelter) and
    /// the base model configuration. Phase 3 adds zones, the airlock, suits, task rules, the shelter, radio coverage,
    /// an optional particle episode and the mission rules. The constructor checks every reference; whole-plan
    /// properties (zone coverage, connectivity with closed hatches) are D5 validator checks.
    /// </summary>
    public sealed class ExpeditionScenario
    {
        public OutpostScenario Outpost { get; }
        public IReadOnlyList<ZoneRegion> Zones { get; }
        public AirlockConfiguration Airlock { get; }
        /// <summary>Rack i holds suit i of <see cref="Eva"/>.</summary>
        public IReadOnlyList<SuitRack> Racks { get; }
        public EvaConfiguration Eva { get; }
        public IReadOnlyList<ExpeditionTaskRule> TaskRules { get; }
        public ShelterConfiguration Shelter { get; }
        public CommsConfiguration Comms { get; }
        /// <summary>Null when the scenario has no space weather information at all (the HUD must then say
        /// "sin información", never "todo bien").</summary>
        public ParticleEventEpisode Episode { get; }
        public ExpeditionMissionRules Rules { get; }
        /// <summary>Speed multiplier while on suit supply, in (0, 1].</summary>
        public double SuitedSpeedFactor { get; }

        public string Id => Outpost.Id;
        public string Provenance => Outpost.Provenance;
        public OutpostLayout Layout => Outpost.Layout;

        public ExpeditionScenario(OutpostScenario outpost, IEnumerable<ZoneRegion> zones, AirlockConfiguration airlock,
            IEnumerable<SuitRack> racks, EvaConfiguration eva, IEnumerable<ExpeditionTaskRule> taskRules,
            ShelterConfiguration shelter, CommsConfiguration comms, ParticleEventEpisode episode,
            ExpeditionMissionRules rules, double suitedSpeedFactor)
        {
            Outpost = outpost ?? throw new ArgumentNullException(nameof(outpost));
            Airlock = airlock ?? throw new ArgumentNullException(nameof(airlock));
            Eva = eva ?? throw new ArgumentNullException(nameof(eva));
            Shelter = shelter ?? throw new ArgumentNullException(nameof(shelter));
            Comms = comms ?? throw new ArgumentNullException(nameof(comms));
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            Episode = episode;
            if (!(suitedSpeedFactor > 0d) || suitedSpeedFactor > 1d) throw new ArgumentOutOfRangeException(nameof(suitedSpeedFactor));
            SuitedSpeedFactor = suitedSpeedFactor;
            OutpostLayout layout = outpost.Layout;

            Zones = Copy(zones, nameof(zones), ExpeditionLimits.MaxZoneRegions);
            if (Zones.Count == 0) throw new ArgumentException("At least one zone region.", nameof(zones));
            foreach (CrewZone zone in new[] { CrewZone.Interior, CrewZone.Shelter, CrewZone.Airlock, CrewZone.Exterior })
                if (!HasRegion(zone)) throw new ArgumentException($"No region for zone {zone}.", nameof(zones));

            // Airlock geometry and panels.
            foreach (BaseRect rect in new[] { airlock.Chamber, airlock.InnerHatch, airlock.OuterHatch })
                if (!layout.Bounds.Contains(rect)) throw new ArgumentException("Airlock rectangles must be inside the bounds.", nameof(airlock));
            if (!layout.TryGetStation(airlock.InnerPanel, out _) || !layout.TryGetStation(airlock.OuterPanel, out _))
                throw new ArgumentException("Airlock panels must be layout stations.", nameof(airlock));
            if (!TryGetZone(Center(airlock.Chamber), out CrewZone chamberZone) || chamberZone != CrewZone.Airlock)
                throw new ArgumentException("The chamber centre must be in the Airlock zone.", nameof(airlock));
            if (!layout.IsAdmissible(airlock.ExteriorEntry, outpost.Movement.CrewRadiusMeters)
                || !TryGetZone(airlock.ExteriorEntry, out CrewZone entryZone) || entryZone != CrewZone.Exterior)
                throw new ArgumentException("The exterior entry must be an admissible Exterior point.", nameof(airlock));
            foreach (BaseRect obstacle in layout.Obstacles)
                if (Overlaps(obstacle, airlock.InnerHatch) || Overlaps(obstacle, airlock.OuterHatch))
                    throw new ArgumentException("Hatch doorways must be open in the plan (no layout obstacle there).", nameof(airlock));

            // Spawns inside the base.
            foreach (BasePosition spawn in layout.SpawnPoints)
                if (!TryGetZone(spawn, out CrewZone spawnZone) || spawnZone != CrewZone.Interior)
                    throw new ArgumentException("Every spawn point must be in the Interior zone.", nameof(outpost));
            foreach (StationPlacement station in layout.Stations)
                if (!TryGetZone(station.Position, out _) && !airlock.IsPanel(station.Id))
                    throw new ArgumentException($"Station '{station.Id}' is in no zone.", nameof(zones));

            // Racks and suits.
            Racks = Copy(racks, nameof(racks), ExpeditionLimits.MaxSuits);
            if (Racks.Count != eva.Suits.Count) throw new ArgumentException("One rack per suit.", nameof(racks));
            for (int i = 0; i < Racks.Count; i++)
            {
                if (!string.Equals(Racks[i].SuitKey, eva.Suits[i].Key, StringComparison.Ordinal))
                    throw new ArgumentException("Rack i holds suit i.", nameof(racks));
                if (!layout.TryGetStation(Racks[i].Station, out _)) throw new ArgumentException("Rack stations must exist.", nameof(racks));
            }

            // Task rules.
            TaskRules = Copy(taskRules, nameof(taskRules), OutpostLimits.MaxTaskTypes);
            var ruled = new HashSet<TaskTypeId>();
            var steps = new HashSet<ChecklistStep>();
            var donned = new HashSet<string>(StringComparer.Ordinal);
            foreach (ExpeditionTaskRule rule in TaskRules)
            {
                if (!ruled.Add(rule.Task)) throw new ArgumentException($"Two rules for '{rule.Task}'.", nameof(taskRules));
                if (!outpost.TryGetTask(rule.Task, out TaskDefinition task))
                    throw new ArgumentException($"Rule for unknown task '{rule.Task}'.", nameof(taskRules));
                layout.TryGetStation(task.Station, out StationPlacement at);
                if (!TryGetZone(at.Position, out CrewZone zone) || zone != rule.RequiredZone)
                    throw new ArgumentException($"'{rule.Task}' is offered outside its required zone.", nameof(taskRules));
                if (rule.Purpose == TaskPurpose.SuitPreparation)
                {
                    steps.Add(rule.Step.Value);
                    if (rule.Step == ChecklistStep.Don)
                    {
                        int suit = eva.IndexOf(rule.SuitKey);
                        if (suit < 0 || Racks[suit].Station != task.Station)
                            throw new ArgumentException($"'{rule.Task}' must be offered at the rack of '{rule.SuitKey}'.", nameof(taskRules));
                        if (!donned.Add(rule.SuitKey)) throw new ArgumentException($"Two Don tasks for '{rule.SuitKey}'.", nameof(taskRules));
                    }
                    if (rule.Step == ChecklistStep.ChargeBattery && !(task.PowerWatts > 0d && task.Category == TaskCategory.Support))
                        throw new ArgumentException($"'{rule.Task}' charges from the base service load: Support with power.", nameof(taskRules));
                }
                if (rule.Purpose == TaskPurpose.ExteriorWork && (task.Category != TaskCategory.Support || task.PowerWatts != 0d
                    || !task.CompletionEffect.IsEmpty))
                    throw new ArgumentException($"'{rule.Task}' runs on the suit: Support, 0 W of base power, no base effect.", nameof(taskRules));
                if (rule.Purpose == TaskPurpose.ShelterPreparation && rule.Task != shelter.PrepareTask)
                    throw new ArgumentException("Only the shelter's preparation task prepares it.", nameof(taskRules));
                if ((rule.Purpose == TaskPurpose.SuitPreparation || rule.Purpose == TaskPurpose.ExteriorWork)
                    && !task.CompletionEffect.IsEmpty)
                    throw new ArgumentException($"'{rule.Task}' moves suit stocks through EVA transfers, not a base effect.", nameof(taskRules));
            }
            if (donned.Count != eva.Suits.Count) throw new ArgumentException("Every suit needs its Don task.", nameof(taskRules));
            foreach (ChecklistStep step in new[] { ChecklistStep.ChargeBattery, ChecklistStep.FillConsumables, ChecklistStep.SealCheck })
                if (!steps.Contains(step)) throw new ArgumentException($"No task completes checklist step {step}.", nameof(taskRules));

            // Shelter.
            if (!layout.TryGetStation(shelter.Panel, out StationPlacement panel)
                || !TryGetZone(panel.Position, out CrewZone panelZone) || panelZone != CrewZone.Shelter)
                throw new ArgumentException("The shelter panel must be a station in the Shelter zone.", nameof(shelter));
            if (!outpost.TryGetTask(shelter.PrepareTask, out TaskDefinition prepare) || prepare.Station != shelter.Panel
                || !TryGetRule(shelter.PrepareTask, out ExpeditionTaskRule prepareRule) || prepareRule.Purpose != TaskPurpose.ShelterPreparation)
                throw new ArgumentException("The shelter preparation task must be offered at its panel with its rule.", nameof(shelter));

            // Mission rules count real tasks.
            foreach (ExpeditionObjectiveRule objective in rules.Objectives)
                foreach (TaskTypeId task in objective.Tasks)
                    if (!outpost.TryGetTask(task, out _))
                        throw new ArgumentException($"Objective '{objective.Key}' counts unknown task '{task}'.", nameof(rules));
        }

        /// <summary>Zone of a confirmed centre: the first region that contains it. False outside every region (a
        /// configuration error once the D5 coverage check passes).</summary>
        public bool TryGetZone(BasePosition position, out CrewZone zone)
        {
            foreach (ZoneRegion region in Zones)
                if (region.Area.Contains(position))
                {
                    zone = region.Zone;
                    return true;
                }
            zone = CrewZone.Interior;
            return false;
        }

        public bool TryGetRule(TaskTypeId task, out ExpeditionTaskRule rule)
        {
            foreach (ExpeditionTaskRule candidate in TaskRules)
                if (candidate.Task == task)
                {
                    rule = candidate;
                    return true;
                }
            rule = null;
            return false;
        }

        /// <summary>The task's rule, or the implicit BaseOperation rule for tasks without one.</summary>
        public ExpeditionTaskRule RuleFor(TaskTypeId task) =>
            TryGetRule(task, out ExpeditionTaskRule rule) ? rule : ExpeditionTaskRule.BaseOperation(task);

        /// <summary>The layout with the given hatches closed (blocking), for validators and route planning.</summary>
        public OutpostLayout LayoutWithHatches(bool innerClosed, bool outerClosed)
        {
            var obstacles = new List<BaseRect>(Layout.Obstacles);
            if (innerClosed) obstacles.Add(Airlock.InnerHatch);
            if (outerClosed) obstacles.Add(Airlock.OuterHatch);
            return new OutpostLayout(Layout.Bounds, obstacles, Layout.Stations, Layout.SpawnPoints);
        }

        public int RackIndexOf(StationId station)
        {
            for (int i = 0; i < Racks.Count; i++)
                if (Racks[i].Station == station) return i;
            return -1;
        }

        private bool HasRegion(CrewZone zone)
        {
            foreach (ZoneRegion region in Zones)
                if (region.Zone == zone) return true;
            return false;
        }

        private static BasePosition Center(BaseRect rect) => new BasePosition((rect.MinX + rect.MaxX) / 2d, (rect.MinY + rect.MaxY) / 2d);

        /// <summary>Positive-area overlap (shared edges do not count).</summary>
        private static bool Overlaps(BaseRect a, BaseRect b) =>
            a.MinX < b.MaxX && b.MinX < a.MaxX && a.MinY < b.MaxY && b.MinY < a.MaxY;

        private static IReadOnlyList<T> Copy<T>(IEnumerable<T> items, string name, int max) where T : class
        {
            if (items == null) throw new ArgumentNullException(name);
            var copy = new List<T>();
            foreach (T item in items)
            {
                if (item == null) throw new ArgumentException("Null entry.", name);
                copy.Add(item);
            }
            if (copy.Count > max) throw new ArgumentException("Too many entries.", name);
            return new ReadOnlyCollection<T>(copy.ToArray());
        }
    }
}
