using System;
using Trainer.Application.Outpost;

namespace Trainer.Application.BaseOperations
{
    /// <summary>
    /// Phase 4 station operations. Pause, Resume, Restart, phase 2/3 tasks and the airlock stay <c>ExpeditionCommand</c>s.
    /// Every action names a station (proximity is checked on the CONFIRMED position, like phase 2).
    /// </summary>
    public enum BaseOperationAction
    {
        /// <summary>Look at a machine: produces a new observation (timed). Water or air station.</summary>
        Inspect,
        /// <summary>Feed one batch of untreated water to the processor (timed, power). Hold must be empty.</summary>
        ProcessBatch,
        /// <summary>Decide where the held product goes: AcceptToPotable or Reprocess. Needs the current observation.</summary>
        RouteProduct,
        /// <summary>Recheck a Suspect potable tank: moves the suspect mass back to untreated (timed, power).</summary>
        RecheckPotable,
        /// <summary>Air service decision without isolation: SwitchBed or RegenerateStandbyBed. Needs the current observation.</summary>
        ServiceAir,
        /// <summary>Repair step: take the machine offline (needs the current observation that justifies it).</summary>
        Isolate,
        /// <summary>Repair step: ReplaceProcessorFilter or ReplaceInletFilter (timed, spare reserved). Needs the current observation.</summary>
        Intervene,
        /// <summary>Repair step: functional test (timed); produces a new observation and the test result.</summary>
        TestEquipment,
        /// <summary>Repair step: back online after a passed test (needs the test's observation).</summary>
        Restore,
        /// <summary>Order a manifest at the logistics console.</summary>
        PlaceOrder,
        /// <summary>Cancel a Pending order (own or team's) at the logistics console.</summary>
        CancelOrder,
        /// <summary>Unload an Arrived order into the stocks (timed). What does not fit stays in the order.</summary>
        UnloadDelivery,
        /// <summary>Cancel the sender's own operation at the station (no refund of energy; reserved spares released).</summary>
        CancelOperation
    }

    /// <summary>The decision carried by RouteProduct, ServiceAir and Intervene; None for every other action.</summary>
    public enum OperationChoice
    {
        None,
        AcceptToPotable,
        Reprocess,
        SwitchBed,
        RegenerateStandbyBed,
        ReplaceProcessorFilter,
        ReplaceInletFilter
    }

    /// <summary>
    /// Correlated phase 4 intent (immutable). Answered by exactly one <see cref="CommandResult"/> with the same Id and Epoch, or
    /// abandoned by the client adapter. Like phases 2–3 it carries NO identity, NO position, NO stock and NO result: the
    /// authority decides everything from its confirmed state.
    ///
    /// Staleness stamps (−1 = <see cref="Unstated"/>): <see cref="ZoneSequence"/> (optional, as phase 3) and
    /// <see cref="ObservationSequence"/> — the observation the decision was based on (required by RouteProduct, ServiceAir,
    /// Isolate, Intervene and Restore). A decision based on an observation that is no longer current is refused
    /// (StaleObservation), so nobody decides from information that has changed.
    /// The constructor accepts malformed values so the authority can answer them; <see cref="ShapeError"/> is the shared check.
    /// </summary>
    public sealed class BaseOperationCommand
    {
        public const long Unstated = -1;

        public Guid Id { get; }
        public long Epoch { get; }
        public BaseOperationAction Action { get; }
        public StationId Station { get; }
        public OperationChoice Choice { get; }
        /// <summary>Manifest key for PlaceOrder, else null.</summary>
        public string ManifestKey { get; }
        /// <summary>Order sequence for CancelOrder and UnloadDelivery, else Unstated.</summary>
        public long OrderSequence { get; }
        public long ObservationSequence { get; }
        public long ZoneSequence { get; }

        public BaseOperationCommand(Guid id, long epoch, BaseOperationAction action, StationId station,
            OperationChoice choice = OperationChoice.None, string manifestKey = null, long orderSequence = Unstated,
            long observationSequence = Unstated, long zoneSequence = Unstated)
        {
            if (epoch < 0) throw new ArgumentOutOfRangeException(nameof(epoch), "Epochs are nonnegative.");
            if (orderSequence < Unstated) throw new ArgumentOutOfRangeException(nameof(orderSequence));
            if (observationSequence < Unstated) throw new ArgumentOutOfRangeException(nameof(observationSequence));
            if (zoneSequence < Unstated) throw new ArgumentOutOfRangeException(nameof(zoneSequence));
            Id = id;
            Epoch = epoch;
            Action = action;
            Station = station;
            Choice = choice;
            ManifestKey = manifestKey;
            OrderSequence = orderSequence;
            ObservationSequence = observationSequence;
            ZoneSequence = zoneSequence;
        }

        public static BaseOperationCommand Operation(Guid id, long epoch, BaseOperationAction action, StationId station,
            OperationChoice choice = OperationChoice.None, long observationSequence = Unstated, long zoneSequence = Unstated) =>
            new BaseOperationCommand(id, epoch, action, station, choice, null, Unstated, observationSequence, zoneSequence);

        public static BaseOperationCommand PlaceOrder(Guid id, long epoch, StationId console, string manifestKey, long zoneSequence = Unstated) =>
            new BaseOperationCommand(id, epoch, BaseOperationAction.PlaceOrder, console, OperationChoice.None, manifestKey, Unstated, Unstated, zoneSequence);

        public static BaseOperationCommand ForOrder(Guid id, long epoch, BaseOperationAction action, StationId console, long orderSequence,
            long zoneSequence = Unstated) =>
            new BaseOperationCommand(id, epoch, action, console, OperationChoice.None, null, orderSequence, Unstated, zoneSequence);

        /// <summary>True when <paramref name="action"/> requires an observation stamp.</summary>
        public static bool NeedsObservation(BaseOperationAction action) =>
            action == BaseOperationAction.RouteProduct || action == BaseOperationAction.ServiceAir || action == BaseOperationAction.Isolate
            || action == BaseOperationAction.Intervene || action == BaseOperationAction.Restore;

        /// <summary>Choices allowed for <paramref name="action"/> (None only, for actions without a decision).</summary>
        public static bool IsChoiceAllowed(BaseOperationAction action, OperationChoice choice)
        {
            switch (action)
            {
                case BaseOperationAction.RouteProduct: return choice == OperationChoice.AcceptToPotable || choice == OperationChoice.Reprocess;
                case BaseOperationAction.ServiceAir: return choice == OperationChoice.SwitchBed || choice == OperationChoice.RegenerateStandbyBed;
                case BaseOperationAction.Intervene: return choice == OperationChoice.ReplaceProcessorFilter || choice == OperationChoice.ReplaceInletFilter;
                default: return choice == OperationChoice.None;
            }
        }

        /// <summary>Null when well formed; otherwise InvalidCommandId, UnknownAction or InvalidActionValue.</summary>
        public string ShapeError()
        {
            if (Id == Guid.Empty) return OutpostReasons.InvalidCommandId;
            if (!Enum.IsDefined(typeof(BaseOperationAction), Action)) return OutpostReasons.UnknownAction;
            if (!Station.IsValid) return OutpostReasons.InvalidActionValue;
            if (!Enum.IsDefined(typeof(OperationChoice), Choice) || !IsChoiceAllowed(Action, Choice)) return OutpostReasons.InvalidActionValue;
            bool order = Action == BaseOperationAction.PlaceOrder;
            if (order != (ManifestKey != null) || (order && !StableKey.IsValid(ManifestKey))) return OutpostReasons.InvalidActionValue;
            bool forOrder = Action == BaseOperationAction.CancelOrder || Action == BaseOperationAction.UnloadDelivery;
            if (forOrder ? OrderSequence < 1 : OrderSequence != Unstated) return OutpostReasons.InvalidActionValue;
            if (NeedsObservation(Action) ? ObservationSequence < 1 : ObservationSequence != Unstated) return OutpostReasons.InvalidActionValue;
            return null;
        }

        public bool HasSameContent(BaseOperationCommand other) =>
            other != null && other.Id == Id && other.Epoch == Epoch && other.Action == Action && other.Station == Station
            && other.Choice == Choice && string.Equals(other.ManifestKey, ManifestKey, StringComparison.Ordinal)
            && other.OrderSequence == OrderSequence && other.ObservationSequence == ObservationSequence && other.ZoneSequence == ZoneSequence;
    }

    /// <summary>
    /// Phase 4 reason codes. Phase 2 (<see cref="OutpostReasons"/>) and phase 3 (<c>ExpeditionReasons</c>) codes keep their
    /// strings and meaning where the same rule applies (NotJoined, EpochMismatch, SessionEnded, InvalidState, UnknownStation,
    /// PlayerBusy, StationOccupied, OutOfRange, NoActiveTask, InsufficientResources, StaleZone, SealedNotAllowed...).
    /// </summary>
    public static class BaseOperationReasons
    {
        /// <summary>The station is not the water, air or logistics station the action needs.</summary>
        public const string WrongStationForAction = "WrongStationForAction";
        /// <summary>No current observation of the machine, or the decision states none.</summary>
        public const string ObservationRequired = "ObservationRequired";
        /// <summary>The stated observation is not the machine's current one (state changed or window expired).</summary>
        public const string StaleObservation = "StaleObservation";
        /// <summary>The machine is isolated (repair in progress): no batch, routing or air service.</summary>
        public const string EquipmentIsolated = "EquipmentIsolated";
        /// <summary>Intervene/Test/Restore without an open repair (machine online).</summary>
        public const string EquipmentNotIsolated = "EquipmentNotIsolated";
        /// <summary>The processor is Failed: no batch until repaired.</summary>
        public const string EquipmentFailed = "EquipmentFailed";
        /// <summary>The step does not follow <see cref="RepairSequence"/>.</summary>
        public const string RepairStepOutOfOrder = "RepairStepOutOfOrder";
        /// <summary>Isolate without a finding that justifies a repair in the stated observation.</summary>
        public const string NothingToRepair = "NothingToRepair";
        /// <summary>The intervention belongs to the other machine.</summary>
        public const string InterventionNotForEquipment = "InterventionNotForEquipment";
        /// <summary>ProcessBatch with product still held.</summary>
        public const string HoldNotEmpty = "HoldNotEmpty";
        /// <summary>RouteProduct with an empty hold.</summary>
        public const string HoldEmpty = "HoldEmpty";
        /// <summary>RecheckPotable while the potable tank is Verified.</summary>
        public const string PotableNotSuspect = "PotableNotSuspect";
        /// <summary>No free spare of the needed kind (others may be reserved).</summary>
        public const string SpareUnavailable = "SpareUnavailable";
        public const string UnknownManifest = "UnknownManifest";
        /// <summary>MaxOrdersInFlight reached.</summary>
        public const string OrderLimitReached = "OrderLimitReached";
        /// <summary>MinSecondsBetweenOrders not yet elapsed since the last order.</summary>
        public const string OrderTooSoon = "OrderTooSoon";
        public const string UnknownOrder = "UnknownOrder";
        /// <summary>CancelOrder after the cancel window (in transit, arrived or closed).</summary>
        public const string OrderNotCancellable = "OrderNotCancellable";
        /// <summary>UnloadDelivery of an order that has not arrived or is closed.</summary>
        public const string OrderNotArrived = "OrderNotArrived";
        /// <summary>Nothing of the order fits in the stocks now.</summary>
        public const string NoStorageCapacity = "NoStorageCapacity";
    }
}
