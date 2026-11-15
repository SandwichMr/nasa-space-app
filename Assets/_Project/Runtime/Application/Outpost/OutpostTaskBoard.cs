using System;
using System.Collections.Generic;

namespace Trainer.Application.Outpost
{
    /// <summary>
    /// Reservation ledger of the authority (mutable, single-threaded, owned by <see cref="OutpostAuthority"/>).
    /// It is the only place that binds a person to a station: at most one active task per station and at most
    /// one per crew member across the whole plan. A violated reservation is a programming error and throws, so
    /// a duplicated worker can never be published (the snapshot constructor would refuse it too).
    /// It never touches stocks or energy and never decides validation reasons; the authority does.
    /// </summary>
    public sealed class OutpostTaskBoard
    {
        /// <summary>A task instance in progress (mutable view inside the authority only).</summary>
        public sealed class ActiveTask
        {
            public StationId Station { get; }
            public TaskDefinition Definition { get; }
            public PlayerId Performer { get; }
            /// <summary>Id of the StartTask command that created the instance; used as the ResourceTransfer id.</summary>
            public Guid TransferId { get; }
            public double WorkSeconds { get; internal set; }
            public bool Stalled { get; internal set; }
            public double RemainingSeconds => Math.Max(0d, Definition.DurationSeconds - WorkSeconds);

            internal ActiveTask(StationId station, TaskDefinition definition, PlayerId performer, Guid transferId)
            {
                Station = station;
                Definition = definition;
                Performer = performer;
                TransferId = transferId;
            }

            public TaskProgress ToProgress() =>
                new TaskProgress(Definition.Id, Performer, WorkSeconds, Definition.DurationSeconds, Stalled);
        }

        private readonly OutpostLayout layout;
        private readonly Dictionary<StationId, ActiveTask> byStation = new Dictionary<StationId, ActiveTask>();
        private readonly Dictionary<PlayerId, ActiveTask> byPlayer = new Dictionary<PlayerId, ActiveTask>();
        private readonly Dictionary<StationId, TaskEnd> lastEnds = new Dictionary<StationId, TaskEnd>();

        public OutpostTaskBoard(OutpostLayout layout)
        {
            this.layout = layout ?? throw new ArgumentNullException(nameof(layout));
        }

        public int ActiveCount => byStation.Count;

        /// <summary>Active tasks in layout station order (a new list each call, safe to end tasks while iterating it).</summary>
        public List<ActiveTask> Active()
        {
            var list = new List<ActiveTask>(byStation.Count);
            foreach (StationPlacement station in layout.Stations)
                if (byStation.TryGetValue(station.Id, out ActiveTask task)) list.Add(task);
            return list;
        }

        public bool TryGetAtStation(StationId station, out ActiveTask task) => byStation.TryGetValue(station, out task);

        public bool TryGetForPlayer(PlayerId player, out ActiveTask task) => byPlayer.TryGetValue(player, out task);

        /// <summary>Reserves <paramref name="station"/> and <paramref name="performer"/> together. Throws if either
        /// is already reserved, if the station is unknown or if the task belongs to another station.</summary>
        public ActiveTask Reserve(StationId station, TaskDefinition definition, PlayerId performer, Guid transferId)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (!layout.TryGetStation(station, out _)) throw new ArgumentException($"Unknown station '{station}'.", nameof(station));
            if (definition.Station != station) throw new ArgumentException("Task is not offered at this station.", nameof(definition));
            if (!performer.IsValid) throw new ArgumentException("Performer required.", nameof(performer));
            if (transferId == Guid.Empty) throw new ArgumentException("Transfer id required.", nameof(transferId));
            if (byStation.ContainsKey(station)) throw new InvalidOperationException($"Station '{station}' is already occupied.");
            if (byPlayer.ContainsKey(performer)) throw new InvalidOperationException($"{performer} already has a task.");
            var task = new ActiveTask(station, definition, performer, transferId);
            byStation.Add(station, task);
            byPlayer.Add(performer, task);
            return task;
        }

        /// <summary>Releases both reservations of the task at <paramref name="station"/> and records why it ended.
        /// Throws if nothing is active there.</summary>
        public TaskEnd End(StationId station, TaskEndReason reason, double endedAtSeconds)
        {
            if (!byStation.TryGetValue(station, out ActiveTask task))
                throw new InvalidOperationException($"No task in progress at '{station}'.");
            byStation.Remove(station);
            byPlayer.Remove(task.Performer);
            var end = new TaskEnd(task.Definition.Id, task.Performer, reason, endedAtSeconds);
            lastEnds[station] = end;
            return end;
        }

        /// <summary>Ends every active task with <paramref name="reason"/> (mission end). Returns how many ended.</summary>
        public int EndAll(TaskEndReason reason, double endedAtSeconds)
        {
            List<ActiveTask> active = Active();
            foreach (ActiveTask task in active) End(task.Station, reason, endedAtSeconds);
            return active.Count;
        }

        /// <summary>Restart: forgets active tasks and last ends without recording anything.</summary>
        public void Clear()
        {
            byStation.Clear();
            byPlayer.Clear();
            lastEnds.Clear();
        }

        /// <summary>Published station states in layout order. No scenario rule of this mission makes a station
        /// Unavailable, so every station is Available or Occupied.</summary>
        public List<StationState> BuildStates()
        {
            var states = new List<StationState>(layout.Stations.Count);
            foreach (StationPlacement station in layout.Stations)
            {
                lastEnds.TryGetValue(station.Id, out TaskEnd lastEnd);
                states.Add(byStation.TryGetValue(station.Id, out ActiveTask task)
                    ? new StationState(station.Id, StationAvailability.Occupied, task.ToProgress(), lastEnd)
                    : new StationState(station.Id, StationAvailability.Available, null, lastEnd));
            }
            return states;
        }
    }
}
