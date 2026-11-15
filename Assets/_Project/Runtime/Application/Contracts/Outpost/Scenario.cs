using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Trainer.Simulation.LifeSupport;

namespace Trainer.Application.Outpost
{
    /// <summary>
    /// Static phase 2 configuration shared by the authority and every client of the same build (immutable).
    /// Built from one labelled scenario asset; mission rules (objective, duration) belong to the mission
    /// implementation and are not part of this type. Snapshots carry <see cref="Id"/> so a client can detect a
    /// different scenario.
    /// </summary>
    public sealed class OutpostScenario
    {
        public string Id { get; }
        /// <summary>Human-readable label of where the values come from (for example "synthetic").</summary>
        public string Provenance { get; }
        public OutpostLayout Layout { get; }
        public CrewMovementSettings Movement { get; }
        public IReadOnlyList<TaskDefinition> Tasks { get; }
        public OutpostConfiguration Simulation { get; }

        public OutpostScenario(string id, string provenance, OutpostLayout layout, CrewMovementSettings movement,
            IEnumerable<TaskDefinition> tasks, OutpostConfiguration simulation)
        {
            Id = StableKey.Require(id, nameof(id));
            if (string.IsNullOrWhiteSpace(provenance)) throw new ArgumentException("Provenance label required.", nameof(provenance));
            Provenance = provenance;
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            Movement = movement ?? throw new ArgumentNullException(nameof(movement));
            Simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
            if (tasks == null) throw new ArgumentNullException(nameof(tasks));

            var copy = new List<TaskDefinition>();
            var ids = new HashSet<TaskTypeId>();
            foreach (TaskDefinition task in tasks)
            {
                if (task == null) throw new ArgumentException("Null task.", nameof(tasks));
                if (!ids.Add(task.Id)) throw new ArgumentException($"Duplicate task '{task.Id}'.", nameof(tasks));
                if (!layout.TryGetStation(task.Station, out _))
                    throw new ArgumentException($"Task '{task.Id}' uses unknown station '{task.Station}'.", nameof(tasks));
                copy.Add(task);
            }
            if (copy.Count == 0 || copy.Count > OutpostLimits.MaxTaskTypes)
                throw new ArgumentException("Between 1 and MaxTaskTypes tasks.", nameof(tasks));
            foreach (BasePosition spawn in layout.SpawnPoints)
                if (!layout.IsAdmissible(spawn, movement.CrewRadiusMeters))
                    throw new ArgumentException("Every spawn point must be admissible for the crew radius.", nameof(layout));
            Tasks = new ReadOnlyCollection<TaskDefinition>(copy.ToArray());
        }

        public bool TryGetTask(TaskTypeId id, out TaskDefinition task)
        {
            foreach (TaskDefinition candidate in Tasks)
                if (candidate.Id == id)
                {
                    task = candidate;
                    return true;
                }
            task = null;
            return false;
        }
    }
}
