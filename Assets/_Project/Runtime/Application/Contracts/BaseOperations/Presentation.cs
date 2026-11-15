using System;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;

namespace Trainer.Application.BaseOperations
{
    /// <summary>
    /// Intents a phase 4 view may send for the LOCAL player (implemented by D3's interaction client). Views never build
    /// commands: they call these; the client stamps the epoch, the zone sequence and the machine's CURRENT observation
    /// sequence from the latest snapshot, and the authority decides. One request in flight; false when nothing was sent
    /// (not joined, no state, busy, or no current observation for a decision that needs one).
    /// </summary>
    public interface IBaseOperationsIntents
    {
        bool RequestOperation(StationId station, BaseOperationAction action, OperationChoice choice = OperationChoice.None);
        bool RequestPlaceOrder(StationId console, string manifestKey);
        bool RequestCancelOrder(StationId console, long orderSequence);
        bool RequestUnload(StationId console, long orderSequence);
        bool RequestCancelOperation(StationId station);
        bool HasPending { get; }
        /// <summary>Reason code of the last answer to this client (Accepted or a rejection), or null.</summary>
        string LastReason { get; }
        /// <summary>The authority answered (reason code) or a request was abandoned (client-only code).</summary>
        event Action<string> Answered;
    }

    /// <summary>
    /// What a phase 4 view receives from the scene composition (D2): the local port, the scenario, both intent sets (phase 3
    /// for tasks/airlock/return, phase 4 for operations) and a way to hold the keyboard. Phase 3 views keep receiving their own
    /// <see cref="ExpeditionViewContext"/> built from the same port.
    /// </summary>
    public sealed class BaseOperationsViewContext
    {
        public IBaseOperationsSessionPort Port { get; }
        public BaseOperationsScenario Scenario { get; }
        public IBaseOperationsIntents Intents { get; }
        public IExpeditionIntents ExpeditionIntents { get; }
        private readonly Action<object, bool> setMovementBlocked;

        public BaseOperationsViewContext(IBaseOperationsSessionPort port, BaseOperationsScenario scenario, IBaseOperationsIntents intents,
            IExpeditionIntents expeditionIntents, Action<object, bool> setMovementBlocked)
        {
            Port = port ?? throw new ArgumentNullException(nameof(port));
            Scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
            Intents = intents ?? throw new ArgumentNullException(nameof(intents));
            ExpeditionIntents = expeditionIntents ?? throw new ArgumentNullException(nameof(expeditionIntents));
            this.setMovementBlocked = setMovementBlocked ?? throw new ArgumentNullException(nameof(setMovementBlocked));
        }

        public void BlockMovement(object owner) => setMovementBlocked(owner, true);
        public void UnblockMovement(object owner) => setMovementBlocked(owner, false);
    }

    /// <summary>
    /// D4 mount point for phase 4 presentation (bars, station panels, decision feedback, reports). MonoBehaviours under the
    /// scene's phase 4 presentation mount; the composition calls <see cref="Bind"/> once the port exists and
    /// <see cref="Unbind"/> before replacing it. A view only reads the port and calls intents: no clock, inventory, movement or
    /// second evaluation lives in a view, and a view never sends "won the minigame".
    /// </summary>
    public interface IBaseOperationsView
    {
        void Bind(BaseOperationsViewContext context);
        void Unbind();
    }
}
