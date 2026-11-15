using System;

namespace Trainer.Application.Outpost
{
    public enum OutpostAction
    {
        /// <summary>Running -> Paused. No station or task.</summary>
        Pause,
        /// <summary>Ready or Paused -> Running. No station or task.</summary>
        Resume,
        /// <summary>Any state -> new epoch in Ready. No station or task.</summary>
        Restart,
        /// <summary>Start <see cref="OutpostCommand.Task"/> at <see cref="OutpostCommand.Station"/>. Both required.</summary>
        StartTask,
        /// <summary>Cancel the sender's own task at <see cref="OutpostCommand.Station"/>. Station required, no task.</summary>
        CancelTask
    }

    /// <summary>
    /// Correlated phase 2 intent (immutable). Answered by exactly one phase 1 <see cref="CommandResult"/> with
    /// the same Id and Epoch, or abandoned by the client adapter. It carries NO player identity: the authority
    /// receives the sender from the host adapter (<see cref="IOutpostAuthority.Submit"/>).
    /// The constructor accepts unknown actions, Guid.Empty and missing fields so the authority can answer them
    /// with a reason; <see cref="ShapeError"/> is the shared structural check.
    /// </summary>
    public sealed class OutpostCommand
    {
        public Guid Id { get; }
        public long Epoch { get; }
        public OutpostAction Action { get; }
        public StationId Station { get; }
        public TaskTypeId Task { get; }

        public OutpostCommand(Guid id, long epoch, OutpostAction action,
            StationId station = default, TaskTypeId task = default)
        {
            if (epoch < 0) throw new ArgumentOutOfRangeException(nameof(epoch), "Epochs are nonnegative.");
            Id = id;
            Epoch = epoch;
            Action = action;
            Station = station;
            Task = task;
        }

        public static OutpostCommand StartTask(Guid id, long epoch, StationId station, TaskTypeId task) =>
            new OutpostCommand(id, epoch, OutpostAction.StartTask, station, task);

        public static OutpostCommand CancelTask(Guid id, long epoch, StationId station) =>
            new OutpostCommand(id, epoch, OutpostAction.CancelTask, station);

        /// <summary>Null when well formed; otherwise InvalidCommandId, UnknownAction or InvalidActionValue.</summary>
        public string ShapeError()
        {
            if (Id == Guid.Empty) return OutpostReasons.InvalidCommandId;
            if (!Enum.IsDefined(typeof(OutpostAction), Action)) return OutpostReasons.UnknownAction;
            bool needsStation = Action == OutpostAction.StartTask || Action == OutpostAction.CancelTask;
            bool needsTask = Action == OutpostAction.StartTask;
            if (Station.IsValid != needsStation || Task.IsValid != needsTask) return OutpostReasons.InvalidActionValue;
            return null;
        }

        /// <summary>Same id, epoch, action, station and task. A replay with the same content (and sender) gets
        /// the stored result; other content under a used id is CommandIdConflict.</summary>
        public bool HasSameContent(OutpostCommand other) =>
            other != null && other.Id == Id && other.Epoch == Epoch && other.Action == Action
            && other.Station == Station && other.Task == Task;
    }

    /// <summary>Reason codes. Authority results use the first two groups (the phase 1 strings keep their
    /// meaning). The last group is client-side only: it never appears in a CommandResult.</summary>
    public static class OutpostReasons
    {
        // Same strings and meaning as phase 1.
        public const string Accepted = "Accepted";
        public const string InvalidCommandId = "InvalidCommandId";
        public const string CommandIdConflict = "CommandIdConflict";
        public const string EpochMismatch = "EpochMismatch";
        public const string UnknownAction = "UnknownAction";
        public const string InvalidActionValue = "InvalidActionValue";
        public const string InvalidState = "InvalidState";
        public const string SessionEnded = "SessionEnded";
        public const string SimulationFault = "SimulationFault";

        // Phase 2 task and membership rules.
        public const string NotJoined = "NotJoined";
        public const string UnknownStation = "UnknownStation";
        public const string UnknownTaskType = "UnknownTaskType";
        public const string TaskNotOfferedAtStation = "TaskNotOfferedAtStation";
        public const string StationUnavailable = "StationUnavailable";
        public const string PlayerBusy = "PlayerBusy";
        public const string StationOccupied = "StationOccupied";
        public const string OutOfRange = "OutOfRange";
        public const string NoActiveTask = "NoActiveTask";
        public const string InsufficientResources = "InsufficientResources";
        /// <summary>Join refused: every spawn slot is taken.</summary>
        public const string SessionFull = "SessionFull";

        // Client-side only (same strings as phase 1 network codes): abandonment and end of membership.
        public const string NotConnected = "NotConnected";
        public const string ConnectionLost = "ConnectionLost";
        public const string LocalDisconnect = "LocalDisconnect";
    }
}
