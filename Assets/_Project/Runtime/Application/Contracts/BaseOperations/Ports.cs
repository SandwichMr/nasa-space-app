using System;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;

namespace Trainer.Application.BaseOperations
{
    /// <summary>
    /// Phase 4 session port seen by ONE participant (presentation, input, tests). It EXTENDS the phase 3
    /// <see cref="IExpeditionSessionPort"/> (and so the phase 2 port): movement input, avatars, session controls, phase 2
    /// stations and phase 3 suits/airlock bind to the same object unchanged.
    /// <list type="bullet">
    /// <item>For one revision the events are raised in order: SnapshotChanged (phase 2), ExpeditionChanged (phase 3),
    /// <see cref="BaseOperationsChanged"/>. <c>TryGetExpedition</c> returns exactly <c>BaseOperations.Expedition</c>.</item>
    /// <item><see cref="Submit(BaseOperationCommand)"/> results arrive through the same phase 2 <c>CommandCompleted</c>;
    /// abandonment through <see cref="BaseOperationCommandAbandoned"/>.</item>
    /// </list>
    /// Single-threaded (Unity main thread); callbacks synchronous. Subscribe before sending.
    /// </summary>
    public interface IBaseOperationsSessionPort : IExpeditionSessionPort
    {
        /// <summary>False until the first phase 4 state arrives. After the link is lost it may keep the last one: check Membership.</summary>
        bool TryGetBaseOperations(out BaseOperationsSnapshot snapshot);

        /// <summary>Correlated phase 4 intent. Null throws. When not joined it is abandoned with NotConnected.</summary>
        void Submit(BaseOperationCommand command);

        event Action<BaseOperationsSnapshot> BaseOperationsChanged;

        /// <summary>(command, reason) with NotConnected, ConnectionLost or LocalDisconnect: result unknown.</summary>
        event Action<BaseOperationCommand, string> BaseOperationCommandAbandoned;
    }

    /// <summary>
    /// Authority entry of the single phase 4 session, used by the phase 4 host adapters (D3). Implemented by D2's continuous
    /// authority, which owns the ONLY roster, base model (<see cref="Trainer.Simulation.LifeSupport.IOutpostSimulation"/>), EVA
    /// model, equipment model (<see cref="Trainer.Simulation.BaseOperations.IBaseEquipmentModel"/>) and clock of the session.
    /// It deliberately implements neither <see cref="IOutpostAuthority"/> nor <see cref="IExpeditionAuthority"/>, so earlier
    /// host code and validators never mistake it for theirs. Same rules as phases 2–3: identity only from Join, results only
    /// to their sender, state committed before events, commands deduplicated for the authority's lifetime (both kinds share
    /// one id space), Restart raises the epoch.
    /// </summary>
    public interface IBaseOperationsAuthority
    {
        BaseOperationsScenario Scenario { get; }

        /// <summary>As phase 3: new PlayerId, spawn inside the base without a suit, or Rejected(SessionFull). A late joiner
        /// gets the CURRENT state (stocks, machines, orders), never the initial values.</summary>
        OutpostMembership Join();

        /// <summary>Ends an identity: cancels its task or operation (PerformerLeft, reserved spares released, energy not
        /// refunded), applies the phase 3 sealed-suit rule, keeps every order. False if not present.</summary>
        bool Leave(PlayerId player);

        /// <summary>Phase 2/3 intent (pause, resume, restart, tasks, airlock, declare return) under continuous rules.</summary>
        void Submit(PlayerId sender, ExpeditionCommand command);

        /// <summary>Phase 4 station operation. Always answered through CommandCompleted with that sender.</summary>
        void Submit(PlayerId sender, BaseOperationCommand command);

        MovementInputDisposition SubmitMovement(PlayerId sender, MovementInput input);

        bool TryGetSnapshot(out BaseOperationsSnapshot snapshot);

        event Action<BaseOperationsSnapshot> SnapshotChanged;
        /// <summary>(sender, result): route the result only to that sender.</summary>
        event Action<PlayerId, CommandResult> CommandCompleted;
    }
}
