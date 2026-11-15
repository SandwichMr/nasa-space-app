using System;
using Trainer.Application;
using Trainer.Application.BaseOperations;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.BaseOperations;

namespace Trainer.Unity.Bootstrap.BaseOperations
{
    /// <summary>
    /// PROVISIONAL (D2, until D3's <c>BaseOperationsInteractionClient</c> is merged): turns a view's request into one
    /// correlated <see cref="BaseOperationCommand"/> stamped with the CURRENT epoch, the confirmed zone sequence and the
    /// machine's latest observation. One request in flight at a time; it never invents a result (the answer is the
    /// authority's CommandCompleted or the port's abandonment).
    /// </summary>
    public sealed class ProvisionalBaseOperationsIntents : IBaseOperationsIntents, IDisposable
    {
        private readonly IBaseOperationsSessionPort port;
        private readonly BaseOperationsScenario scenario;
        private Guid inFlight;

        public bool HasPending => inFlight != Guid.Empty;
        public string LastReason { get; private set; }
        public event Action<string> Answered;

        public ProvisionalBaseOperationsIntents(IBaseOperationsSessionPort port, BaseOperationsScenario scenario)
        {
            this.port = port ?? throw new ArgumentNullException(nameof(port));
            this.scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
            port.CommandCompleted += OnResult;
            port.BaseOperationCommandAbandoned += OnAbandoned;
        }

        public bool RequestOperation(StationId station, BaseOperationAction action, OperationChoice choice = OperationChoice.None)
        {
            if (!Ready(out BaseOperationsSnapshot s, out long zone)) return false;
            long observation = BaseOperationCommand.Unstated;
            if (BaseOperationCommand.NeedsObservation(action))
            {
                EquipmentKind? kind = scenario.EquipmentAt(station);
                EquipmentObservation latest = kind.HasValue ? s.EquipmentOf(kind.Value).LatestObservation : null;
                observation = latest?.Sequence ?? 1; // the authority answers ObservationRequired when there is none
            }
            return Send(BaseOperationCommand.Operation(Guid.NewGuid(), s.Epoch, action, station, choice, observation, zone));
        }

        public bool RequestPlaceOrder(StationId console, string manifestKey) =>
            Ready(out BaseOperationsSnapshot s, out long zone) && Send(BaseOperationCommand.PlaceOrder(Guid.NewGuid(), s.Epoch, console, manifestKey, zone));

        public bool RequestCancelOrder(StationId console, long orderSequence) =>
            Ready(out BaseOperationsSnapshot s, out long zone)
            && Send(BaseOperationCommand.ForOrder(Guid.NewGuid(), s.Epoch, BaseOperationAction.CancelOrder, console, orderSequence, zone));

        public bool RequestUnload(StationId console, long orderSequence) =>
            Ready(out BaseOperationsSnapshot s, out long zone)
            && Send(BaseOperationCommand.ForOrder(Guid.NewGuid(), s.Epoch, BaseOperationAction.UnloadDelivery, console, orderSequence, zone));

        public bool RequestCancelOperation(StationId station) => RequestOperation(station, BaseOperationAction.CancelOperation);

        private bool Ready(out BaseOperationsSnapshot snapshot, out long zone)
        {
            zone = BaseOperationCommand.Unstated;
            snapshot = null;
            if (HasPending || !port.Membership.IsJoined || !port.TryGetBaseOperations(out snapshot)) return false;
            if (snapshot.Expedition.TryGetCrew(port.Membership.Player, out ExpeditionCrewState crew)) zone = crew.ZoneSequence;
            return true;
        }

        private bool Send(BaseOperationCommand command)
        {
            inFlight = command.Id;
            port.Submit(command);
            return true;
        }

        private void OnResult(CommandResult result)
        {
            if (result.CommandId != inFlight) return;
            inFlight = Guid.Empty;
            LastReason = result.Reason;
            Answered?.Invoke(result.Reason);
        }

        private void OnAbandoned(BaseOperationCommand command, string reason)
        {
            if (command.Id != inFlight) return;
            inFlight = Guid.Empty;
            LastReason = reason;
            Answered?.Invoke(reason);
        }

        public void Dispose()
        {
            port.CommandCompleted -= OnResult;
            port.BaseOperationCommandAbandoned -= OnAbandoned;
        }
    }
}
