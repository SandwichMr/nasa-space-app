using System;
using System.Collections.Generic;

namespace Trainer.Application.Outpost
{
    /// <summary>
    /// Phase 2 session port seen by ONE participant (presentation, input, tests). It is not the phase 1
    /// <see cref="ISessionPort"/> and does not replace it: phase 1 scenes keep using that one unchanged.
    /// Implementations: the network client adapter and the host-local adapter (D3), and the development
    /// fake. Single-threaded (Unity main thread); callbacks are synchronous.
    ///
    /// Submit and SubmitMovement only deliver intent. Subscribe before sending: a result may arrive
    /// immediately or later. Every Submit ends in exactly one CommandCompleted (the authority's answer, same
    /// Id and Epoch) or one CommandAbandoned (no answer will come on this link; the command may or may not have
    /// been applied — trust the next snapshot, never resend it under a new id to "retry").
    /// Snapshots are ordered by (Epoch, Revision); a port never raises SnapshotChanged with an older or equal
    /// one. Do not depend on the order between result and snapshot events.
    /// </summary>
    public interface IOutpostSessionPort
    {
        /// <summary>This participant's identity. Joined only after a snapshot that contains the player is
        /// available through TryGetSnapshot. Never null.</summary>
        OutpostMembership Membership { get; }

        /// <summary>False until the authority's first state arrives on the current link. After the link is
        /// lost it may keep returning the last one: check <see cref="Membership"/> before presenting it as live.</summary>
        bool TryGetSnapshot(out OutpostSessionSnapshot snapshot);

        /// <summary>Correlated intent. Null throws ArgumentNullException. When not joined the command is
        /// abandoned with NotConnected (never sent).</summary>
        void Submit(OutpostCommand command);

        /// <summary>Uncorrelated movement intent: no result event. Null throws ArgumentNullException.
        /// Ignored when not joined. Its effect is visible only as confirmed poses in later snapshots.</summary>
        void SubmitMovement(MovementInput input);

        event Action<OutpostSessionSnapshot> SnapshotChanged;
        event Action<CommandResult> CommandCompleted;
        /// <summary>(command, reason) with NotConnected, ConnectionLost or LocalDisconnect.</summary>
        event Action<OutpostCommand, string> CommandAbandoned;
        event Action<OutpostMembership> MembershipChanged;
    }

    /// <summary>
    /// Authority entry of the single phase 2 session, used by the host adapters (D3) that know each real
    /// connection. Implemented by the mission authority (D2), which owns the only instances of the roster
    /// (<see cref="ICrewRoster"/>) and simulation (<see cref="Trainer.Simulation.LifeSupport.IOutpostSimulation"/>).
    /// Single-threaded; state and deduplication are committed before events are raised, so subscribers may
    /// call back in. Subscriber exceptions propagate.
    /// </summary>
    public interface IOutpostAuthority
    {
        /// <summary>Binds a new identity. Joined (new PlayerId, lowest free slot, its spawn point) or Rejected
        /// with SessionFull. Raises SnapshotChanged with the new member before returning when joined.</summary>
        OutpostMembership Join();

        /// <summary>Ends an identity: cancels its task (PerformerLeft, no completion effect), frees its station,
        /// removes it from the crew and forgets its movement state. False if it was not present (idempotent).</summary>
        bool Leave(PlayerId player);

        /// <summary>Correlated intent from <paramref name="sender"/> (bound by the adapter, never read from the
        /// payload). Always answered through CommandCompleted with that sender.</summary>
        void Submit(PlayerId sender, OutpostCommand command);

        /// <summary>Applies <see cref="MovementInputPolicy"/> for <paramref name="sender"/>. No event is raised for
        /// the input itself; the disposition is returned for diagnostics.</summary>
        MovementInputDisposition SubmitMovement(PlayerId sender, MovementInput input);

        bool TryGetSnapshot(out OutpostSessionSnapshot snapshot);

        event Action<OutpostSessionSnapshot> SnapshotChanged;
        /// <summary>(sender, result): route the result only to that sender's connection.</summary>
        event Action<PlayerId, CommandResult> CommandCompleted;
    }

    /// <summary>
    /// Crew presence, identity allocation and confirmed movement INSIDE the authority (D3 implements; the
    /// mission authority owns the single instance and is its only caller). Built with the scenario's
    /// <see cref="OutpostLayout"/> and <see cref="CrewMovementSettings"/>; it is the only collision authority.
    /// Plain C#, single-threaded, no Unity physics.
    /// </summary>
    public interface ICrewRoster
    {
        /// <summary>Present members in PlayerId order (immutable list, replaced on change).</summary>
        IReadOnlyList<CrewMemberState> Members { get; }

        /// <summary>New PlayerId (never reused by this instance), lowest free slot, idle at that slot's spawn
        /// point with default facing; or Rejected(SessionFull).</summary>
        OutpostMembership Join();

        /// <summary>Removes the member and its held direction. False if not present.</summary>
        bool Leave(PlayerId player);

        /// <summary><see cref="MovementInputPolicy"/> with the member's last applied sequence; NotJoined for an
        /// unknown player. Applied replaces the held direction (position changes only in Advance).</summary>
        MovementInputDisposition SubmitMovement(PlayerId player, MovementInput input, long currentEpoch);

        /// <summary>Moves every member along its held direction for <paramref name="elapsedSeconds"/> (finite,
        /// nonnegative) at Magnitude * MaxSpeed. Every resulting position satisfies
        /// <see cref="OutpostLayout.IsAdmissible"/>; a step never moves farther than speed * time. Called only
        /// while the mission is Running.</summary>
        void Advance(double elapsedSeconds);

        /// <summary>Restart: every member back to its slot's spawn point, Idle, default facing, held direction
        /// cleared. Ids, slots and last applied sequences are kept.</summary>
        void ResetForRestart();
    }
}
