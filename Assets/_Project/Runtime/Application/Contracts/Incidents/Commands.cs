using System;
using Trainer.Application.Outpost;

namespace Trainer.Application.Incidents
{
    /// <summary>Phase 5 intents. New enum (the frozen phase 4 <c>BaseOperationAction</c> is not extended).</summary>
    public enum IncidentAction
    {
        /// <summary>Power panel, timed: a power observation (findings + readings).</summary>
        InspectPower,
        /// <summary>Power panel, timed: reset a tripped converter. Needs a current observation.</summary>
        ResetConverter,
        /// <summary>Power panel, immediate: isolate the discharge controller for replacement. Needs a current observation.</summary>
        IsolatePower,
        /// <summary>Power panel, timed: replace the controller part (one spare, consumed at completion). Needs isolation.</summary>
        ReplacePowerPart,
        /// <summary>Power panel, immediate: back online after the replacement.</summary>
        RestorePower,
        /// <summary>Assignment board (no station): accept an offer.</summary>
        AcceptAssignment,
        /// <summary>Assignment board: postpone an offer (it returns later).</summary>
        PostponeAssignment,
        /// <summary>Assignment board: cancel an accepted assignment (no reward; resources already used are not refunded).</summary>
        CancelAssignment,
        /// <summary>Upgrade station, timed: install the bounded upgrade (credits + spare, at completion).</summary>
        InstallUpgrade,
        /// <summary>Cancel the sender's own phase 5 timed operation at that station.</summary>
        CancelOperation
    }

    /// <summary>
    /// A phase 5 intent from a client (immutable). No identity, position, amount, reward, severity, epoch choice or source id:
    /// the host binds the sender from the transport; the authority decides everything else.
    /// <see cref="AssignmentSequence"/> names the target offer/assignment (assignment actions only);
    /// <see cref="ObservationSequence"/> is the power observation the client saw (decisions that need one); <see cref="ZoneSequence"/>
    /// the sender's confirmed zone sequence (station actions). Commands share the id space of phases 2–4.
    /// </summary>
    public sealed class IncidentCommand
    {
        public Guid Id { get; }
        public long Epoch { get; }
        public IncidentAction Action { get; }
        public StationId Station { get; }
        public long AssignmentSequence { get; }
        public long ObservationSequence { get; }
        public long ZoneSequence { get; }

        public IncidentCommand(Guid id, long epoch, IncidentAction action, StationId station, long assignmentSequence, long observationSequence,
            long zoneSequence)
        {
            Id = id;
            Epoch = epoch;
            Action = action;
            Station = station;
            AssignmentSequence = assignmentSequence;
            ObservationSequence = observationSequence;
            ZoneSequence = zoneSequence;
        }

        public static bool IsAssignmentAction(IncidentAction action) =>
            action == IncidentAction.AcceptAssignment || action == IncidentAction.PostponeAssignment || action == IncidentAction.CancelAssignment;

        public static bool IsPowerAction(IncidentAction action) =>
            action == IncidentAction.InspectPower || action == IncidentAction.ResetConverter || action == IncidentAction.IsolatePower
            || action == IncidentAction.ReplacePowerPart || action == IncidentAction.RestorePower;

        public static bool NeedsObservation(IncidentAction action) =>
            action == IncidentAction.ResetConverter || action == IncidentAction.IsolatePower;

        /// <summary>Null when the shape is valid, else <see cref="OutpostReasons.UnknownAction"/> or <see cref="OutpostReasons.InvalidActionValue"/>.</summary>
        public string ShapeError()
        {
            if (!Enum.IsDefined(typeof(IncidentAction), Action)) return OutpostReasons.UnknownAction;
            if (Epoch < 0 || AssignmentSequence < 0 || ObservationSequence < 0 || ZoneSequence < 0) return OutpostReasons.InvalidActionValue;
            bool assignment = IsAssignmentAction(Action);
            if (assignment)
            {
                if (Station.IsValid || AssignmentSequence < 1 || ObservationSequence != 0 || ZoneSequence != 0) return OutpostReasons.InvalidActionValue;
                return null;
            }
            if (!Station.IsValid || AssignmentSequence != 0) return OutpostReasons.InvalidActionValue;
            if (NeedsObservation(Action) != (ObservationSequence > 0)) return OutpostReasons.InvalidActionValue;
            return null;
        }

        public bool HasSameContent(IncidentCommand other) =>
            other != null && other.Id == Id && other.Epoch == Epoch && other.Action == Action && other.Station == Station
            && other.AssignmentSequence == AssignmentSequence && other.ObservationSequence == ObservationSequence && other.ZoneSequence == ZoneSequence;
    }

    /// <summary>Stable phase 5 reason codes (in addition to the phase 2–4 codes, which keep their meaning).</summary>
    public static class IncidentReasons
    {
        public const string WrongStationForAction = "WrongStationForAction";
        public const string StaleZone = "StaleZone";
        public const string SealedNotAllowed = "SealedNotAllowed";
        public const string ObservationRequired = "ObservationRequired";
        public const string StaleObservation = "StaleObservation";
        public const string NothingToRepair = "NothingToRepair";
        public const string PowerNotIsolated = "PowerNotIsolated";
        public const string PowerAlreadyIsolated = "PowerAlreadyIsolated";
        public const string PartNotReplaced = "PartNotReplaced";
        public const string SpareUnavailable = "SpareUnavailable";
        public const string UnknownAssignment = "UnknownAssignment";
        public const string AssignmentNotOffered = "AssignmentNotOffered";
        public const string AssignmentNotAccepted = "AssignmentNotAccepted";
        public const string AssignmentLimitReached = "AssignmentLimitReached";
        public const string UpgradeInstalled = "UpgradeInstalled";
        public const string InsufficientCredits = "InsufficientCredits";
        public const string CurrentDataNotAvailable = "CurrentDataNotAvailable";
    }
}
