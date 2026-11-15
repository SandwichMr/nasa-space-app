using System;
using Trainer.Application.Outpost;

namespace Trainer.Application.Expedition
{
    public enum ExpeditionAction
    {
        /// <summary>As phase 2: Running -> Paused.</summary>
        Pause,
        /// <summary>As phase 2: Ready or Paused -> Running.</summary>
        Resume,
        /// <summary>As phase 2: any state -> new epoch in Ready.</summary>
        Restart,
        /// <summary>As phase 2 plus phase 3 requirements (zone, suit, checklist). Station and task required.</summary>
        StartTask,
        /// <summary>As phase 2: cancel the sender's own task at Station. Station required.</summary>
        CancelTask,
        /// <summary>Start the airlock's next cycle from one of its panels (Station = a panel). The direction is
        /// decided by the airlock state; <see cref="ExpeditionCommand.AirlockCycle"/> states which state the sender saw.</summary>
        CycleAirlock,
        /// <summary>Team decision to end the exterior work and come back. No station or task.</summary>
        DeclareReturn
    }

    /// <summary>
    /// Correlated phase 3 intent (immutable). Answered by exactly one phase 1 <see cref="CommandResult"/> with the
    /// same Id and Epoch, or abandoned by the client adapter. Like phase 2 it carries NO identity and NO position.
    ///
    /// Staleness stamps (−1 = <see cref="Unstated"/>):
    /// <see cref="ZoneSequence"/> is the sender's own zone sequence as last confirmed when the intent was made
    /// (StartTask, CycleAirlock); if the sender changed zone since, the authority refuses it (StaleZone).
    /// <see cref="AirlockCycle"/> is the airlock cycle sequence the sender saw (CycleAirlock); if a cycle started
    /// since, it is refused (StaleAirlockState), so a request made for one direction never runs the other.
    /// The constructor accepts unknown actions, Guid.Empty and missing fields so the authority can answer them;
    /// <see cref="ShapeError"/> is the shared structural check.
    /// </summary>
    public sealed class ExpeditionCommand
    {
        public const long Unstated = -1;

        public Guid Id { get; }
        public long Epoch { get; }
        public ExpeditionAction Action { get; }
        public StationId Station { get; }
        public TaskTypeId Task { get; }
        public long ZoneSequence { get; }
        public long AirlockCycle { get; }

        public ExpeditionCommand(Guid id, long epoch, ExpeditionAction action, StationId station = default,
            TaskTypeId task = default, long zoneSequence = Unstated, long airlockCycle = Unstated)
        {
            if (epoch < 0) throw new ArgumentOutOfRangeException(nameof(epoch), "Epochs are nonnegative.");
            if (zoneSequence < Unstated) throw new ArgumentOutOfRangeException(nameof(zoneSequence));
            if (airlockCycle < Unstated) throw new ArgumentOutOfRangeException(nameof(airlockCycle));
            Id = id;
            Epoch = epoch;
            Action = action;
            Station = station;
            Task = task;
            ZoneSequence = zoneSequence;
            AirlockCycle = airlockCycle;
        }

        public static ExpeditionCommand Session(Guid id, long epoch, ExpeditionAction action) =>
            new ExpeditionCommand(id, epoch, action);

        public static ExpeditionCommand StartTask(Guid id, long epoch, StationId station, TaskTypeId task, long zoneSequence) =>
            new ExpeditionCommand(id, epoch, ExpeditionAction.StartTask, station, task, zoneSequence);

        public static ExpeditionCommand CancelTask(Guid id, long epoch, StationId station) =>
            new ExpeditionCommand(id, epoch, ExpeditionAction.CancelTask, station);

        public static ExpeditionCommand CycleAirlock(Guid id, long epoch, StationId panel, long zoneSequence, long airlockCycle) =>
            new ExpeditionCommand(id, epoch, ExpeditionAction.CycleAirlock, panel, default, zoneSequence, airlockCycle);

        public static ExpeditionCommand DeclareReturn(Guid id, long epoch) =>
            new ExpeditionCommand(id, epoch, ExpeditionAction.DeclareReturn);

        /// <summary>
        /// The same intent as a phase 2 command submitted through a phase 3 port (same Id, Epoch, station, task;
        /// action mapped by name; an undefined phase 2 action stays undefined so the authority answers UnknownAction).
        /// A StartTask gets <paramref name="zoneSequence"/>, the sender's zone sequence in the port's latest snapshot.
        /// </summary>
        public static ExpeditionCommand FromOutpost(OutpostCommand command, long zoneSequence)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            ExpeditionAction action;
            switch (command.Action)
            {
                case OutpostAction.Pause: action = ExpeditionAction.Pause; break;
                case OutpostAction.Resume: action = ExpeditionAction.Resume; break;
                case OutpostAction.Restart: action = ExpeditionAction.Restart; break;
                case OutpostAction.StartTask: action = ExpeditionAction.StartTask; break;
                case OutpostAction.CancelTask: action = ExpeditionAction.CancelTask; break;
                default: action = (ExpeditionAction)int.MaxValue; break;
            }
            long zone = action == ExpeditionAction.StartTask ? Math.Max(Unstated, zoneSequence) : Unstated;
            return new ExpeditionCommand(command.Id, command.Epoch, action, command.Station, command.Task, zone);
        }

        /// <summary>Null when well formed; otherwise InvalidCommandId, UnknownAction or InvalidActionValue.</summary>
        public string ShapeError()
        {
            if (Id == Guid.Empty) return OutpostReasons.InvalidCommandId;
            if (!Enum.IsDefined(typeof(ExpeditionAction), Action)) return OutpostReasons.UnknownAction;
            bool needsStation = Action == ExpeditionAction.StartTask || Action == ExpeditionAction.CancelTask
                || Action == ExpeditionAction.CycleAirlock;
            bool needsTask = Action == ExpeditionAction.StartTask;
            if (Station.IsValid != needsStation || Task.IsValid != needsTask) return OutpostReasons.InvalidActionValue;
            bool zoneAllowed = Action == ExpeditionAction.StartTask || Action == ExpeditionAction.CycleAirlock;
            if (!zoneAllowed && ZoneSequence != Unstated) return OutpostReasons.InvalidActionValue;
            if (Action != ExpeditionAction.CycleAirlock && AirlockCycle != Unstated) return OutpostReasons.InvalidActionValue;
            return null;
        }

        /// <summary>Every field equal. A replay with the same content (and sender) gets the stored result; other
        /// content under a used id is CommandIdConflict.</summary>
        public bool HasSameContent(ExpeditionCommand other) =>
            other != null && other.Id == Id && other.Epoch == Epoch && other.Action == Action && other.Station == Station
            && other.Task == Task && other.ZoneSequence == ZoneSequence && other.AirlockCycle == AirlockCycle;
    }

    /// <summary>
    /// Phase 3 reason codes. Every phase 2 code of <see cref="OutpostReasons"/> keeps its string and meaning and is
    /// used where the same rule applies; these are the new rules. Client-only codes stay NotConnected,
    /// ConnectionLost and LocalDisconnect.
    /// </summary>
    public static class ExpeditionReasons
    {
        /// <summary>The sender changed zone after making the intent (its ZoneSequence is older).</summary>
        public const string StaleZone = "StaleZone";
        /// <summary>An airlock cycle started after the sender saw the airlock (its AirlockCycle is older).</summary>
        public const string StaleAirlockState = "StaleAirlockState";
        /// <summary>The task needs another zone (for example exterior work from inside).</summary>
        public const string WrongZone = "WrongZone";
        /// <summary>Base tasks need the performer inside the pressurized base, not on suit supply.</summary>
        public const string SealedNotAllowed = "SealedNotAllowed";
        /// <summary>The step needs a worn (unsealed) suit and the sender wears none.</summary>
        public const string SuitRequired = "SuitRequired";
        /// <summary>The sender already wears a suit (Don).</summary>
        public const string AlreadySuited = "AlreadySuited";
        /// <summary>That rack's suit is not on it (taken by someone else, or abandoned).</summary>
        public const string SuitNotOnRack = "SuitNotOnRack";
        /// <summary>A previous checklist step is missing.</summary>
        public const string ChecklistOutOfOrder = "ChecklistOutOfOrder";
        /// <summary>That checklist step is already done for this suit.</summary>
        public const string ChecklistStepDone = "ChecklistStepDone";
        /// <summary>The station is not a panel of the airlock.</summary>
        public const string NotAnAirlockPanel = "NotAnAirlockPanel";
        /// <summary>A cycle is in progress.</summary>
        public const string AirlockBusy = "AirlockBusy";
        /// <summary>Someone is standing in a hatch doorway: no hatch closes on a person.</summary>
        public const string AirlockObstructed = "AirlockObstructed";
        /// <summary>More occupants than the airlock capacity.</summary>
        public const string AirlockOverCapacity = "AirlockOverCapacity";
        /// <summary>To depressurize, every occupant needs a worn suit with the whole checklist.</summary>
        public const string SuitNotReady = "SuitNotReady";
        /// <summary>A shelter instruction is in force: nobody cycles OUT (an empty cycle to fetch returning crew is allowed).</summary>
        public const string EgressBlockedByInstruction = "EgressBlockedByInstruction";
        /// <summary>The return was declared: no new exterior work starts.</summary>
        public const string ReturnDeclared = "ReturnDeclared";
        /// <summary>DeclareReturn without anybody on suit supply.</summary>
        public const string NoExpeditionUnderway = "NoExpeditionUnderway";
        /// <summary>DeclareReturn after a return was already declared in this epoch.</summary>
        public const string ReturnAlreadyDeclared = "ReturnAlreadyDeclared";
    }
}
