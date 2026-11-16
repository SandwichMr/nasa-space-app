using System;
using Trainer.Application.BaseOperations;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;

namespace Trainer.Application.Incidents
{
    /// <summary>
    /// Phase 5 session port seen by ONE participant. It EXTENDS the phase 4 port (and so phases 2–3): movement, avatars, session
    /// controls, stations, suits, airlock and the phase 4 bars bind to the same object unchanged.
    /// <list type="bullet">
    /// <item>For one revision the events are raised in order: SnapshotChanged (2), ExpeditionChanged (3), BaseOperationsChanged (4),
    /// <see cref="LivingBaseChanged"/>. <c>TryGetBaseOperations</c> returns exactly <c>LivingBase.BaseOperations</c>.</item>
    /// <item><see cref="Submit(IncidentCommand)"/> results arrive through the phase 2 <c>CommandCompleted</c>; abandonment through
    /// <see cref="IncidentCommandAbandoned"/>.</item>
    /// </list>
    /// Single-threaded (Unity main thread); callbacks synchronous. Subscribe before sending.
    /// </summary>
    public interface ILivingBaseSessionPort : IBaseOperationsSessionPort
    {
        bool TryGetLivingBase(out LivingBaseSnapshot snapshot);
        void Submit(IncidentCommand command);
        event Action<LivingBaseSnapshot> LivingBaseChanged;
        /// <summary>(command, reason) with NotConnected, ConnectionLost or LocalDisconnect: result unknown.</summary>
        event Action<IncidentCommand, string> IncidentCommandAbandoned;
    }

    /// <summary>
    /// Authority entry of the single phase 5 session (D2 implements; D3 host adapters use it). It owns the ONLY phase 4 authority
    /// (and through it the only roster, base model, EVA model, equipment model and clock) plus the director, incidents, assignments
    /// and the upgrade. It implements none of the earlier authority interfaces. Same rules as phases 2–4: identity only from Join,
    /// results only to their sender, state committed before events, every command id deduplicated for the authority's lifetime
    /// across ALL kinds (phase 3, 4 and 5 share one id space), Restart raises the epoch and keeps the setup.
    /// </summary>
    public interface ILivingBaseAuthority
    {
        LivingBaseScenario Scenario { get; }
        LivingBaseSetup Setup { get; }
        OutpostMembership Join();
        bool Leave(PlayerId player);
        void Submit(PlayerId sender, ExpeditionCommand command);
        void Submit(PlayerId sender, BaseOperationCommand command);
        void Submit(PlayerId sender, IncidentCommand command);
        MovementInputDisposition SubmitMovement(PlayerId sender, MovementInput input);
        bool TryGetSnapshot(out LivingBaseSnapshot snapshot);
        event Action<LivingBaseSnapshot> SnapshotChanged;
        event Action<PlayerId, CommandResult> CommandCompleted;
    }

    /// <summary>
    /// Intents a phase 5 view may send for the LOCAL player (implemented by D3's interaction client). The client stamps epoch,
    /// zone sequence and the CURRENT power observation; the view never invents them. One request in flight; false when nothing
    /// was sent.
    /// </summary>
    public interface ILivingBaseIntents
    {
        bool RequestPower(IncidentAction action);
        bool RequestAssignment(IncidentAction action, long assignmentSequence);
        bool RequestInstallUpgrade();
        bool RequestCancelOperation(StationId station);
        bool HasPending { get; }
        string LastReason { get; }
        event Action<string> Answered;
    }

    /// <summary>What a phase 5 view receives from the composition: the port, the scenario, all intent sets and the keyboard hold.</summary>
    public sealed class LivingBaseViewContext
    {
        public ILivingBaseSessionPort Port { get; }
        public LivingBaseScenario Scenario { get; }
        public ILivingBaseIntents Intents { get; }
        public IBaseOperationsIntents BaseOperationsIntents { get; }
        public IExpeditionIntents ExpeditionIntents { get; }
        private readonly Action<object, bool> setMovementBlocked;

        public LivingBaseViewContext(ILivingBaseSessionPort port, LivingBaseScenario scenario, ILivingBaseIntents intents,
            IBaseOperationsIntents baseOperationsIntents, IExpeditionIntents expeditionIntents, Action<object, bool> setMovementBlocked)
        {
            Port = port ?? throw new ArgumentNullException(nameof(port));
            Scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
            Intents = intents ?? throw new ArgumentNullException(nameof(intents));
            BaseOperationsIntents = baseOperationsIntents ?? throw new ArgumentNullException(nameof(baseOperationsIntents));
            ExpeditionIntents = expeditionIntents ?? throw new ArgumentNullException(nameof(expeditionIntents));
            this.setMovementBlocked = setMovementBlocked ?? throw new ArgumentNullException(nameof(setMovementBlocked));
        }

        public void BlockMovement(object owner) => setMovementBlocked(owner, true);
        public void UnblockMovement(object owner) => setMovementBlocked(owner, false);
    }

    /// <summary>D4 mount point for phase 5 presentation (incident cards, advisories, power panel, assignments, upgrade, sources).
    /// Bind once the port exists, Unbind before replacing it. Reads the port, calls intents; nothing else.</summary>
    public interface ILivingBaseView
    {
        void Bind(LivingBaseViewContext context);
        void Unbind();
    }
}
