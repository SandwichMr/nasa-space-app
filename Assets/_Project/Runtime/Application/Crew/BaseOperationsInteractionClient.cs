using System;
using Trainer.Application.BaseOperations;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.BaseOperations;

namespace Trainer.Application.Crew
{
    /// <summary>
    /// Client side of phase 4 interaction for the LOCAL player (D3, plain C#): the <see cref="IBaseOperationsIntents"/> that
    /// the scene hands to D4's views. From the latest CONFIRMED snapshot it stamps every intent with the epoch, the local zone
    /// sequence and, for a decision, the machine's CURRENT observation sequence, so an intent made before a zone change, a
    /// restart or a change of the machine is refused by the authority instead of acting on a different situation.
    /// <list type="bullet">
    /// <item>It never decides, moves anyone, reserves a spare or changes a stock. It sends no "completed" step: completion is
    /// what the authority publishes.</item>
    /// <item>One request in flight. Its answer is the authority's result (matched by id and epoch) or an abandonment with a
    /// client-only code (NotConnected, ConnectionLost, LocalDisconnect: the result is unknown and the next snapshot is the
    /// truth). A failed send never looks like a success, and a lost membership releases the pending request.</item>
    /// <item>Nothing is sent (false) when not joined, without state, while busy, for a malformed request (see
    /// <see cref="BaseOperationCommand.ShapeError"/>) or for a decision without a current observation of that machine.</item>
    /// </list>
    /// </summary>
    public sealed class BaseOperationsInteractionClient : IBaseOperationsIntents, IDisposable
    {
        private readonly IBaseOperationsSessionPort port;
        private readonly BaseOperationsScenario scenario;
        private bool disposed;

        public BaseOperationCommand Pending { get; private set; }
        public bool HasPending => Pending != null;
        public string LastReason { get; private set; }
        public event Action<string> Answered;

        public BaseOperationsInteractionClient(IBaseOperationsSessionPort port, BaseOperationsScenario scenario)
        {
            this.port = port ?? throw new ArgumentNullException(nameof(port));
            this.scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
            port.CommandCompleted += OnCompleted;
            port.BaseOperationCommandAbandoned += OnAbandoned;
            port.MembershipChanged += OnMembership;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            port.CommandCompleted -= OnCompleted;
            port.BaseOperationCommandAbandoned -= OnAbandoned;
            port.MembershipChanged -= OnMembership;
            Pending = null;
        }

        /// <summary>True for the stations with a phase 4 meaning (water, air, logistics).</summary>
        public bool IsBaseOperationsStation(StationId station) =>
            station.IsValid && (station == scenario.WaterStation || station == scenario.AirStation || station == scenario.Logistics.Console);

        /// <summary>Phase 4 station whose reach contains the local CONFIRMED position, nearest first (a UI hint only: the
        /// authority checks reach again on its own position).</summary>
        public bool TryFindStationInReach(out StationPlacement station)
        {
            station = null;
            if (!TryGetLocal(out BaseOperationsSnapshot snapshot, out _)) return false;
            if (!snapshot.Outpost.TryGetCrew(port.Membership.Player, out CrewMemberState pose)) return false;
            double best = double.PositiveInfinity;
            foreach (StationPlacement placement in scenario.Layout.Stations)
            {
                if (!IsBaseOperationsStation(placement.Id) || !placement.IsWithinReach(pose.Pose.Position)) continue;
                double distance = placement.Position.DistanceTo(pose.Pose.Position);
                if (distance < best)
                {
                    best = distance;
                    station = placement;
                }
            }
            return station != null;
        }

        /// <summary>The local player's confirmed operation in progress, if any.</summary>
        public bool TryGetOwnOperation(out OperationProgress operation)
        {
            operation = null;
            return TryGetLocal(out BaseOperationsSnapshot snapshot, out _) && snapshot.TryGetOperation(port.Membership.Player, out operation);
        }

        /// <summary>The machine's observation that a decision would be based on: the latest one, only while it is Current.</summary>
        public bool TryGetCurrentObservation(StationId station, out EquipmentObservation observation)
        {
            observation = null;
            EquipmentKind? machine = scenario.EquipmentAt(station);
            if (machine == null || !TryGetLocal(out BaseOperationsSnapshot snapshot, out _)) return false;
            EquipmentObservation latest = snapshot.EquipmentOf(machine.Value).LatestObservation;
            if (latest == null || !latest.Current) return false;
            observation = latest;
            return true;
        }

        /// <summary>Key E in phase 4: cancels the local player's own operation in progress. Phase 4 decisions need a choice,
        /// so nothing else is sent without a panel; false when there is no own operation.</summary>
        public bool Interact() => TryGetOwnOperation(out OperationProgress own) && RequestCancelOperation(own.Station);

        public bool RequestOperation(StationId station, BaseOperationAction action, OperationChoice choice = OperationChoice.None)
        {
            if (action == BaseOperationAction.CancelOperation) return RequestCancelOperation(station);
            return Send((snapshot, me) =>
            {
                long observation = BaseOperationCommand.Unstated;
                if (BaseOperationCommand.NeedsObservation(action))
                {
                    if (!TryGetCurrentObservation(station, out EquipmentObservation current)) return null;
                    observation = current.Sequence;
                }
                return BaseOperationCommand.Operation(Guid.NewGuid(), snapshot.Epoch, action, station, choice, observation, me.ZoneSequence);
            });
        }

        public bool RequestPlaceOrder(StationId console, string manifestKey) =>
            Send((snapshot, me) => StableKey.IsValid(manifestKey)
                ? BaseOperationCommand.PlaceOrder(Guid.NewGuid(), snapshot.Epoch, console, manifestKey, me.ZoneSequence) : null);

        public bool RequestCancelOrder(StationId console, long orderSequence) =>
            Send((snapshot, me) => orderSequence < 1 ? null
                : BaseOperationCommand.ForOrder(Guid.NewGuid(), snapshot.Epoch, BaseOperationAction.CancelOrder, console, orderSequence, me.ZoneSequence));

        public bool RequestUnload(StationId console, long orderSequence) =>
            Send((snapshot, me) => orderSequence < 1 ? null
                : BaseOperationCommand.ForOrder(Guid.NewGuid(), snapshot.Epoch, BaseOperationAction.UnloadDelivery, console, orderSequence, me.ZoneSequence));

        /// <summary>Cancelling one's own operation carries no zone stamp, like cancelling a phase 2 task: walking away must
        /// not prevent stopping it.</summary>
        public bool RequestCancelOperation(StationId station) =>
            Send((snapshot, me) => BaseOperationCommand.Operation(Guid.NewGuid(), snapshot.Epoch, BaseOperationAction.CancelOperation, station));

        private bool Send(Func<BaseOperationsSnapshot, ExpeditionCrewState, BaseOperationCommand> build)
        {
            if (disposed) throw new ObjectDisposedException(nameof(BaseOperationsInteractionClient));
            if (Pending != null || !TryGetLocal(out BaseOperationsSnapshot snapshot, out ExpeditionCrewState me)) return false;
            BaseOperationCommand command = build(snapshot, me);
            if (command == null || command.ShapeError() != null) return false;
            Pending = command; // before Submit: the answer may arrive synchronously
            try
            {
                port.Submit(command);
            }
            catch (Exception)
            {
                // A port that throws after taking the request: the outcome is unknown, never a success.
                if (ReferenceEquals(Pending, command)) Release(OutpostReasons.ConnectionLost);
            }
            return true;
        }

        private bool TryGetLocal(out BaseOperationsSnapshot snapshot, out ExpeditionCrewState me)
        {
            me = null;
            snapshot = null;
            return port.Membership.IsJoined && port.TryGetBaseOperations(out snapshot) && snapshot != null
                && snapshot.Expedition.TryGetCrew(port.Membership.Player, out me);
        }

        private void OnCompleted(CommandResult result)
        {
            BaseOperationCommand pending = Pending;
            if (pending == null || result == null || result.CommandId != pending.Id || result.Epoch != pending.Epoch) return;
            Release(result.Reason);
        }

        private void OnAbandoned(BaseOperationCommand command, string reason)
        {
            if (command != null && ReferenceEquals(command, Pending)) Release(reason);
        }

        private void OnMembership(OutpostMembership membership)
        {
            if (Pending == null || membership.IsJoined) return;
            Release(membership.Reason == OutpostReasons.LocalDisconnect ? OutpostReasons.LocalDisconnect : OutpostReasons.ConnectionLost);
        }

        private void Release(string reason)
        {
            Pending = null;
            LastReason = reason;
            Answered?.Invoke(reason);
        }
    }
}
