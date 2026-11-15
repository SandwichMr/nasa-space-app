using System;
using Trainer.Simulation.LifeSupport;

namespace Trainer.Application.Outpost
{
    public enum TaskCategory
    {
        /// <summary>Base service. Its <see cref="TaskDefinition.PowerWatts"/> joins
        /// <see cref="OutpostDemand.ServiceLoadWatts"/> while in progress.</summary>
        Support,
        /// <summary>Science. While in progress it requests the phase 1 science channel
        /// (<see cref="OutpostDemand.ScienceRequested"/>); its own PowerWatts must be zero.</summary>
        Science
    }

    /// <summary>
    /// Task type performed by ONE crew member at ONE station (configuration, immutable).
    /// Progress accrues only while the task's demand is fully served, in simulated seconds, up to
    /// <see cref="DurationSeconds"/>. Energy is consumed by the simulation during progress (no refund on
    /// cancel). <see cref="CompletionEffect"/> is applied once, by the simulation, at completion; a cancelled
    /// or failed task applies nothing.
    /// </summary>
    public sealed class TaskDefinition
    {
        public TaskTypeId Id { get; }
        public StationId Station { get; }
        public TaskCategory Category { get; }
        /// <summary>Simulated seconds of fully served work needed to complete (finite, positive).</summary>
        public double DurationSeconds { get; }
        /// <summary>Additional electrical load while in progress, watts (finite, nonnegative; zero for Science).</summary>
        public double PowerWatts { get; }
        /// <summary>Material effect at completion; <see cref="ResourceEffect.None"/> when the task moves no mass.</summary>
        public ResourceEffect CompletionEffect { get; }

        public TaskDefinition(TaskTypeId id, StationId station, TaskCategory category, double durationSeconds,
            double powerWatts, ResourceEffect completionEffect)
        {
            if (!id.IsValid) throw new ArgumentException("Task type id required.", nameof(id));
            if (!station.IsValid) throw new ArgumentException("Station id required.", nameof(station));
            Id = id;
            Station = station;
            Category = OutpostGuard.Defined(category, nameof(category));
            DurationSeconds = OutpostGuard.Positive(durationSeconds, nameof(durationSeconds));
            PowerWatts = OutpostGuard.NonNegative(powerWatts, nameof(powerWatts));
            if (category == TaskCategory.Science && powerWatts != 0d)
                throw new ArgumentException("Science tasks use the phase 1 science channel; PowerWatts must be 0.",
                    nameof(powerWatts));
            CompletionEffect = completionEffect ?? throw new ArgumentNullException(nameof(completionEffect));
        }
    }

    /// <summary>A task instance in progress at a station (session state, immutable).</summary>
    public sealed class TaskProgress
    {
        public TaskTypeId Task { get; }
        public PlayerId Performer { get; }
        /// <summary>Fully served simulated seconds of work so far, within [0, DurationSeconds].</summary>
        public double WorkSeconds { get; }
        public double DurationSeconds { get; }
        /// <summary>True when the last authority step did not fully serve this task, so it did not advance.</summary>
        public bool Stalled { get; }
        public double Fraction => WorkSeconds / DurationSeconds;

        public TaskProgress(TaskTypeId task, PlayerId performer, double workSeconds, double durationSeconds, bool stalled)
        {
            if (!task.IsValid) throw new ArgumentException("Task type id required.", nameof(task));
            if (!performer.IsValid) throw new ArgumentException("Performer required.", nameof(performer));
            Task = task;
            Performer = performer;
            DurationSeconds = OutpostGuard.Positive(durationSeconds, nameof(durationSeconds));
            WorkSeconds = OutpostGuard.NonNegative(workSeconds, nameof(workSeconds));
            if (workSeconds > durationSeconds) throw new ArgumentOutOfRangeException(nameof(workSeconds), "Work exceeds duration.");
            Stalled = stalled;
        }
    }

    /// <summary>Why a task instance ended. Only Completed applies the completion effect.</summary>
    public enum TaskEndReason
    {
        Completed,
        /// <summary>The performer sent CancelTask.</summary>
        CancelledByPerformer,
        /// <summary>The performer's confirmed position left the station's interaction radius.</summary>
        PerformerMovedAway,
        /// <summary>The performer left or lost the connection.</summary>
        PerformerLeft,
        /// <summary>Completion was refused by the simulation (stock or capacity); nothing was applied.</summary>
        InsufficientResources,
        /// <summary>The mission reached Completed or Interrupted while the task was in progress.</summary>
        MissionEnded
    }

    /// <summary>Last finished task instance of a station (session state, immutable). The performer may no
    /// longer be present. Cleared by Restart.</summary>
    public sealed class TaskEnd
    {
        public TaskTypeId Task { get; }
        public PlayerId Performer { get; }
        public TaskEndReason Reason { get; }
        public double EndedAtSeconds { get; }

        public TaskEnd(TaskTypeId task, PlayerId performer, TaskEndReason reason, double endedAtSeconds)
        {
            if (!task.IsValid) throw new ArgumentException("Task type id required.", nameof(task));
            if (!performer.IsValid) throw new ArgumentException("Performer required.", nameof(performer));
            Task = task;
            Performer = performer;
            Reason = OutpostGuard.Defined(reason, nameof(reason));
            EndedAtSeconds = OutpostGuard.NonNegative(endedAtSeconds, nameof(endedAtSeconds));
        }
    }

    public enum StationAvailability
    {
        /// <summary>Free: a present crew member in reach may start one of its tasks.</summary>
        Available,
        /// <summary>One task in progress; nobody else can start there.</summary>
        Occupied,
        /// <summary>Not usable now by a scenario rule of the authority (documented by D2).</summary>
        Unavailable
    }

    /// <summary>Mutable station state as published (immutable object). Occupied if and only if
    /// <see cref="ActiveTask"/> is set; the occupant is the task performer.</summary>
    public sealed class StationState
    {
        public StationId Station { get; }
        public StationAvailability Availability { get; }
        public TaskProgress ActiveTask { get; }
        public TaskEnd LastEnd { get; }
        public PlayerId Occupant => ActiveTask?.Performer ?? PlayerId.None;

        public StationState(StationId station, StationAvailability availability, TaskProgress activeTask, TaskEnd lastEnd)
        {
            if (!station.IsValid) throw new ArgumentException("Station id required.", nameof(station));
            Station = station;
            Availability = OutpostGuard.Defined(availability, nameof(availability));
            if ((availability == StationAvailability.Occupied) != (activeTask != null))
                throw new ArgumentException("Occupied exactly when a task is in progress.", nameof(activeTask));
            ActiveTask = activeTask;
            LastEnd = lastEnd;
        }

        public static StationState Free(StationId station) =>
            new StationState(station, StationAvailability.Available, null, null);
    }
}
