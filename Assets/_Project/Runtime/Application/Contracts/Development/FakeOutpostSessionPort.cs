using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Trainer.Application.Outpost;

namespace Trainer.Application.Development
{
    /// <summary>
    /// DEVELOPMENT ONLY. Scripted <see cref="IOutpostSessionPort"/> for presentation work and tests, driven by a
    /// fixture. It is NOT an authority and NOT a transport:
    /// - it never decides a result: every command stays pending until the fixture calls Accept/Reject;
    /// - it never changes state by itself: the fixture publishes each consequence as a new snapshot;
    /// - it never moves anyone: movement inputs are only recorded (last one plus a counter);
    /// - it does not deduplicate ids, check permissions, proximity, exclusivity or stocks (the authority's job).
    /// What it does enforce is the CLIENT side of the contract: snapshots only move forward, Joined only with a
    /// snapshot that contains the player, one result or abandonment per submission, no invented results.
    /// Single-threaded; callbacks synchronous; subscriber exceptions propagate.
    /// </summary>
    public sealed class FakeOutpostSessionPort : IOutpostSessionPort
    {
        public const string Label = "DESARROLLO · FakeOutpostSessionPort · sin red, sin simulación, sin autoridad";

        private readonly List<OutpostCommand> pending = new List<OutpostCommand>();
        private OutpostSessionSnapshot snapshot;

        public OutpostMembership Membership { get; private set; } = OutpostMembership.NotJoined;

        public event Action<OutpostSessionSnapshot> SnapshotChanged;
        public event Action<CommandResult> CommandCompleted;
        public event Action<OutpostCommand, string> CommandAbandoned;
        public event Action<OutpostMembership> MembershipChanged;

        /// <summary>Submissions still waiting for the fixture, oldest first (a copy).</summary>
        public IReadOnlyList<OutpostCommand> PendingCommands => new ReadOnlyCollection<OutpostCommand>(pending.ToArray());

        /// <summary>Last movement input received while joined (null if none). Recorded, never applied.</summary>
        public MovementInput LastMovementInput { get; private set; }
        public long MovementInputCount { get; private set; }
        /// <summary>Movement inputs received while not joined (dropped).</summary>
        public long DroppedMovementInputCount { get; private set; }

        public bool TryGetSnapshot(out OutpostSessionSnapshot value)
        {
            value = snapshot;
            return value != null;
        }

        public void Submit(OutpostCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (!Membership.IsJoined)
            {
                CommandAbandoned?.Invoke(command, OutpostReasons.NotConnected);
                return;
            }
            pending.Add(command);
        }

        public void SubmitMovement(MovementInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (!Membership.IsJoined)
            {
                DroppedMovementInputCount++;
                return;
            }
            LastMovementInput = input;
            MovementInputCount++;
        }

        // ---- Fixture controls -------------------------------------------------------------------------------

        /// <summary>Publishes a fixture state. Throws unless it is newer than the current one (Epoch, Revision)
        /// and, while joined, still contains the local player (use <see cref="Disconnect"/> to end it).</summary>
        public void Publish(OutpostSessionSnapshot next)
        {
            if (next == null) throw new ArgumentNullException(nameof(next));
            if (!next.IsNewerThan(snapshot))
                throw new InvalidOperationException("A snapshot must be newer (epoch, revision) than the current one.");
            if (Membership.IsJoined && !next.TryGetCrew(Membership.Player, out _))
                throw new InvalidOperationException("The local player is joined; end the membership with Disconnect first.");
            snapshot = next;
            SnapshotChanged?.Invoke(next);
        }

        /// <summary>Marks this port as <paramref name="player"/>. The current snapshot must contain that player,
        /// as the authority's state always arrives before the membership.</summary>
        public void Join(PlayerId player)
        {
            if (Membership.IsJoined) throw new InvalidOperationException("Already joined.");
            if (snapshot == null || !snapshot.TryGetCrew(player, out _))
                throw new InvalidOperationException("Publish a snapshot that contains the player before Join.");
            SetMembership(OutpostMembership.Joined(player));
        }

        /// <summary>The authority refused to join (for example <see cref="OutpostReasons.SessionFull"/>).</summary>
        public void RejectJoin(string reason)
        {
            if (Membership.IsJoined) throw new InvalidOperationException("Already joined; use Disconnect.");
            SetMembership(OutpostMembership.Rejected(reason));
        }

        /// <summary>Ends the membership like a lost or closed link: every pending command is abandoned with
        /// <paramref name="reason"/> (result unknown), then the membership becomes Left. The last snapshot is
        /// kept as last known state, not live state.</summary>
        public void Disconnect(string reason)
        {
            if (!Membership.IsJoined) throw new InvalidOperationException("Not joined.");
            if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Reason required.", nameof(reason));
            PlayerId former = Membership.Player;
            OutpostCommand[] abandoned = pending.ToArray();
            pending.Clear();
            SetMembership(OutpostMembership.Left(former, reason));
            foreach (OutpostCommand command in abandoned) CommandAbandoned?.Invoke(command, reason);
        }

        /// <summary>Completes the oldest pending submission with <paramref name="commandId"/> as accepted.</summary>
        public CommandResult Accept(Guid commandId) => Complete(commandId, true, OutpostReasons.Accepted);

        /// <summary>Completes the oldest pending submission with <paramref name="commandId"/> as rejected with
        /// <paramref name="reason"/> (any authority reason code except Accepted).</summary>
        public CommandResult Reject(Guid commandId, string reason)
        {
            if (string.IsNullOrWhiteSpace(reason) || reason == OutpostReasons.Accepted)
                throw new ArgumentException("A rejection needs a reason other than Accepted.", nameof(reason));
            return Complete(commandId, false, reason);
        }

        private CommandResult Complete(Guid commandId, bool accepted, string reason)
        {
            int index = pending.FindIndex(command => command.Id == commandId);
            if (index < 0)
                throw new InvalidOperationException("No pending submission with that id: the fake never invents results.");
            OutpostCommand command = pending[index];
            pending.RemoveAt(index);
            var result = new CommandResult(command.Id, command.Epoch, accepted, reason);
            CommandCompleted?.Invoke(result);
            return result;
        }

        private void SetMembership(OutpostMembership value)
        {
            Membership = value;
            MembershipChanged?.Invoke(value);
        }
    }
}
