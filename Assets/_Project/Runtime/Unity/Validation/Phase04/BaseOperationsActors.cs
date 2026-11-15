using System;
using Trainer.Application.BaseOperations;
using Trainer.Application.Crew;
using Trainer.Application.Outpost;

namespace Trainer.Unity.Validation.Phase04
{
    /// <summary>
    /// What a D5 scripted player can do: read the confirmed state, hold a movement direction and REQUEST an operation.
    /// There is no way to set a position, a stock or a result: every effect is decided by the authority.
    /// Two implementations: <see cref="PortIntentsActor"/> (the scene/build path: the port and D3's intents) and, in the
    /// Editor validation assembly, an actor that talks to the authority directly for in-memory conformance runs.
    /// </summary>
    public interface IBaseOperationsActor
    {
        OutpostMembership Membership { get; }
        bool TryGetState(out BaseOperationsSnapshot snapshot);
        /// <summary>Held direction from raw axes (magnitude scaled to 1). (0, 0) stops.</summary>
        void Move(double x, double y);
        /// <summary>Sends one request; false when nothing was sent (busy, no state, no current observation...).</summary>
        bool Request(BaseOperationAction action, StationId station, OperationChoice choice, string manifestKey, long orderSequence);
        bool HasPending { get; }
        /// <summary>Returns and clears the last answer (reason code), or null when none arrived since the last call.</summary>
        string TakeAnswer();
    }

    /// <summary>
    /// Build/scene actor: movement through the phase 2 <see cref="CrewInputSender"/> on the port and operations through the
    /// D3 <see cref="IBaseOperationsIntents"/> (which stamps epoch, zone and the CURRENT observation itself).
    /// </summary>
    public sealed class PortIntentsActor : IBaseOperationsActor, IDisposable
    {
        private readonly IBaseOperationsSessionPort port;
        private readonly IBaseOperationsIntents intents;
        private readonly CrewInputSender movement;
        private string answer;

        public PortIntentsActor(IBaseOperationsSessionPort port, IBaseOperationsIntents intents)
        {
            this.port = port ?? throw new ArgumentNullException(nameof(port));
            this.intents = intents ?? throw new ArgumentNullException(nameof(intents));
            movement = new CrewInputSender(port);
            intents.Answered += OnAnswered;
        }

        public OutpostMembership Membership => port.Membership;
        public bool HasPending => intents.HasPending;

        public bool TryGetState(out BaseOperationsSnapshot snapshot) => port.TryGetBaseOperations(out snapshot);

        public void Move(double x, double y) => movement.Update(x, y);

        public bool Request(BaseOperationAction action, StationId station, OperationChoice choice, string manifestKey, long orderSequence)
        {
            switch (action)
            {
                case BaseOperationAction.PlaceOrder: return intents.RequestPlaceOrder(station, manifestKey);
                case BaseOperationAction.CancelOrder: return intents.RequestCancelOrder(station, orderSequence);
                case BaseOperationAction.UnloadDelivery: return intents.RequestUnload(station, orderSequence);
                case BaseOperationAction.CancelOperation: return intents.RequestCancelOperation(station);
                default: return intents.RequestOperation(station, action, choice);
            }
        }

        public string TakeAnswer()
        {
            string value = answer;
            answer = null;
            return value;
        }

        public void Dispose() => intents.Answered -= OnAnswered;

        private void OnAnswered(string reason) => answer = reason;
    }
}
