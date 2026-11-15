using System;
using System.Collections.Generic;
using Trainer.Application.Outpost;

namespace Trainer.Application.Expedition
{
    /// <summary>
    /// Phase 3 session port seen by ONE participant (presentation, input, tests). It EXTENDS the phase 2
    /// <see cref="IOutpostSessionPort"/>, so phase 2 movement input, avatars, session controls and base views bind to
    /// the same object unchanged; phase 2 members keep their meaning:
    /// <list type="bullet">
    /// <item><see cref="IOutpostSessionPort.TryGetSnapshot"/> returns exactly <c>Expedition.Outpost</c> of the latest
    /// phase 3 snapshot, and <see cref="IOutpostSessionPort.SnapshotChanged"/> is raised first, then
    /// <see cref="ExpeditionChanged"/>, for the same revision.</item>
    /// <item><see cref="IOutpostSessionPort.Submit(OutpostCommand)"/> forwards the same intent as
    /// <see cref="ExpeditionCommand.FromOutpost"/> stamped with the local member's zone sequence of the latest
    /// snapshot; its abandonment is reported through <see cref="IOutpostSessionPort.CommandAbandoned"/>.</item>
    /// <item><see cref="Submit(ExpeditionCommand)"/> results arrive through the same
    /// <see cref="IOutpostSessionPort.CommandCompleted"/>; abandonment through <see cref="ExpeditionCommandAbandoned"/>.</item>
    /// </list>
    /// Single-threaded (Unity main thread); callbacks synchronous. Subscribe before sending.
    /// </summary>
    public interface IExpeditionSessionPort : IOutpostSessionPort
    {
        /// <summary>False until the authority's first state arrives. After the link is lost it may keep returning
        /// the last one: check Membership before presenting it as live.</summary>
        bool TryGetExpedition(out ExpeditionSessionSnapshot snapshot);

        /// <summary>Correlated phase 3 intent. Null throws. When not joined it is abandoned with NotConnected.</summary>
        void Submit(ExpeditionCommand command);

        event Action<ExpeditionSessionSnapshot> ExpeditionChanged;

        /// <summary>(command, reason) with NotConnected, ConnectionLost or LocalDisconnect: result unknown.</summary>
        event Action<ExpeditionCommand, string> ExpeditionCommandAbandoned;
    }

    /// <summary>
    /// Authority entry of the single phase 3 session, used by the phase 3 host adapters (D3). Implemented by the
    /// expedition authority (D2), which owns the ONLY roster (<see cref="IZonedCrewRoster"/>), base model
    /// (<see cref="Trainer.Simulation.LifeSupport.IOutpostSimulation"/>) and EVA model
    /// (<see cref="Trainer.Simulation.Eva.IEvaSimulation"/>) of the session, interior AND exterior: there is no second
    /// authority for the exterior. It deliberately does NOT implement <see cref="IOutpostAuthority"/>, so phase 2 host
    /// code and validators can never mistake it for the interior authority.
    /// Same rules as phase 2: identity only from Join, results only to their sender, state committed before events.
    /// </summary>
    public interface IExpeditionAuthority
    {
        ExpeditionScenario Scenario { get; }

        /// <summary>Joined (new PlayerId, lowest free slot, spawn point inside the base, no suit) or Rejected(SessionFull).
        /// Raises SnapshotChanged with the new member before returning when joined. A newcomer inside the base
        /// receives the advisories that are current at that moment.</summary>
        OutpostMembership Join();

        /// <summary>Ends an identity: cancels its task (PerformerLeft), and if it was on suit supply leaves its suit
        /// Abandoned and records DisconnectedWhileSealed. No ghost, no grace period. False if not present.</summary>
        bool Leave(PlayerId player);

        /// <summary>Correlated intent from <paramref name="sender"/> (bound by the adapter). Always answered through
        /// CommandCompleted with that sender.</summary>
        void Submit(PlayerId sender, ExpeditionCommand command);

        /// <summary>Phase 2 movement rule (<see cref="MovementInputPolicy"/>) through the single roster.</summary>
        MovementInputDisposition SubmitMovement(PlayerId sender, MovementInput input);

        bool TryGetSnapshot(out ExpeditionSessionSnapshot snapshot);

        event Action<ExpeditionSessionSnapshot> SnapshotChanged;
        /// <summary>(sender, result): route the result only to that sender.</summary>
        event Action<PlayerId, CommandResult> CommandCompleted;
    }

    /// <summary>
    /// Phase 3 extension of the phase 2 roster (D3). It stays the ONLY collision authority and the only mover:
    /// a closed hatch is an extra blocking rectangle (overlay) and a sealed suit walks slower (speed factor).
    /// </summary>
    public interface IZonedCrewRoster : ICrewRoster
    {
        /// <summary>Rectangles that currently block movement in addition to the layout obstacles.</summary>
        IReadOnlyList<BaseRect> ObstacleOverlay { get; }

        /// <summary>True when no present member's disc overlaps <paramref name="area"/> (touching an edge is clear).</summary>
        bool IsClear(BaseRect area);

        /// <summary>Replaces the overlay. Throws InvalidOperationException, changing nothing, if a present member would
        /// not be admissible with it (check <see cref="IsClear"/> first).</summary>
        void SetObstacleOverlay(IEnumerable<BaseRect> rectangles);

        /// <summary>Speed multiplier in (0, 1] for a present member (1 on Join and after ResetForRestart).
        /// False if not present.</summary>
        bool SetSpeedFactor(PlayerId player, double factor);

        double SpeedFactorOf(PlayerId player);
    }
}
