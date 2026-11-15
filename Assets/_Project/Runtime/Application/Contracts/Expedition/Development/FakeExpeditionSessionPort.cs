using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;

namespace Trainer.Application.Development
{
    /// <summary>
    /// DEVELOPMENT ONLY. Scripted <see cref="IExpeditionSessionPort"/> for D4 presentation work and contract tests,
    /// driven by a fixture (see <see cref="ExpeditionFixtures"/>). It is NOT an authority and NOT a transport:
    /// it never decides a result (every command waits for Accept/Reject), never changes state by itself (the fixture
    /// publishes each consequence), never moves anyone (movement is only recorded) and validates no rule (zone,
    /// airlock, suit, checklist, stock, proximity or epoch). It enforces only the CLIENT side of the contract:
    /// snapshots move forward, Joined only with a snapshot containing the player, one result or abandonment per
    /// submission, phase 2 events first then phase 3 events for the same revision, no invented results.
    /// </summary>
    public sealed class FakeExpeditionSessionPort : IExpeditionSessionPort
    {
        public const string Label = "DESARROLLO · FakeExpeditionSessionPort · sin red, sin simulación, sin autoridad";

        private sealed class Entry
        {
            public OutpostCommand Outpost;
            public ExpeditionCommand Expedition;
        }

        private readonly List<Entry> pending = new List<Entry>();
        private ExpeditionSessionSnapshot snapshot;

        public OutpostMembership Membership { get; private set; } = OutpostMembership.NotJoined;

        public event Action<OutpostSessionSnapshot> SnapshotChanged;
        public event Action<ExpeditionSessionSnapshot> ExpeditionChanged;
        public event Action<CommandResult> CommandCompleted;
        public event Action<OutpostCommand, string> CommandAbandoned;
        public event Action<ExpeditionCommand, string> ExpeditionCommandAbandoned;
        public event Action<OutpostMembership> MembershipChanged;

        /// <summary>Every pending submission as the phase 3 command the port would send (phase 2 ones converted with
        /// the zone stamp taken at submission), oldest first (a copy).</summary>
        public IReadOnlyList<ExpeditionCommand> PendingCommands
        {
            get
            {
                var copy = new List<ExpeditionCommand>(pending.Count);
                foreach (Entry entry in pending) copy.Add(entry.Expedition);
                return new ReadOnlyCollection<ExpeditionCommand>(copy.ToArray());
            }
        }

        public MovementInput LastMovementInput { get; private set; }
        public long MovementInputCount { get; private set; }
        public long DroppedMovementInputCount { get; private set; }

        public bool TryGetSnapshot(out OutpostSessionSnapshot value)
        {
            value = snapshot?.Outpost;
            return value != null;
        }

        public bool TryGetExpedition(out ExpeditionSessionSnapshot value)
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
            long zone = snapshot != null && snapshot.TryGetCrew(Membership.Player, out ExpeditionCrewState local)
                ? local.ZoneSequence : ExpeditionCommand.Unstated;
            pending.Add(new Entry { Outpost = command, Expedition = ExpeditionCommand.FromOutpost(command, zone) });
        }

        public void Submit(ExpeditionCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (!Membership.IsJoined)
            {
                ExpeditionCommandAbandoned?.Invoke(command, OutpostReasons.NotConnected);
                return;
            }
            pending.Add(new Entry { Expedition = command });
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

        /// <summary>Publishes a fixture state: phase 2 SnapshotChanged first, then ExpeditionChanged. Throws unless it
        /// is newer than the current one and, while joined, still contains the local player.</summary>
        public void Publish(ExpeditionSessionSnapshot next)
        {
            if (next == null) throw new ArgumentNullException(nameof(next));
            if (!next.IsNewerThan(snapshot))
                throw new InvalidOperationException("A snapshot must be newer (epoch, revision) than the current one.");
            if (Membership.IsJoined && !next.TryGetCrew(Membership.Player, out _))
                throw new InvalidOperationException("The local player is joined; end the membership with Disconnect first.");
            snapshot = next;
            SnapshotChanged?.Invoke(next.Outpost);
            ExpeditionChanged?.Invoke(next);
        }

        public void Join(PlayerId player)
        {
            if (Membership.IsJoined) throw new InvalidOperationException("Already joined.");
            if (snapshot == null || !snapshot.TryGetCrew(player, out _))
                throw new InvalidOperationException("Publish a snapshot that contains the player before Join.");
            SetMembership(OutpostMembership.Joined(player));
        }

        public void RejectJoin(string reason)
        {
            if (Membership.IsJoined) throw new InvalidOperationException("Already joined; use Disconnect.");
            SetMembership(OutpostMembership.Rejected(reason));
        }

        /// <summary>Ends the membership like a lost link: pending commands are abandoned (result unknown) through the
        /// event of their kind, then Membership is Left. The last snapshot stays as last known state.</summary>
        public void Disconnect(string reason)
        {
            if (!Membership.IsJoined) throw new InvalidOperationException("Not joined.");
            if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("Reason required.", nameof(reason));
            PlayerId former = Membership.Player;
            Entry[] abandoned = pending.ToArray();
            pending.Clear();
            SetMembership(OutpostMembership.Left(former, reason));
            foreach (Entry entry in abandoned)
            {
                if (entry.Outpost != null) CommandAbandoned?.Invoke(entry.Outpost, reason);
                else ExpeditionCommandAbandoned?.Invoke(entry.Expedition, reason);
            }
        }

        public CommandResult Accept(Guid commandId) => Complete(commandId, true, OutpostReasons.Accepted);

        public CommandResult Reject(Guid commandId, string reason)
        {
            if (string.IsNullOrWhiteSpace(reason) || reason == OutpostReasons.Accepted)
                throw new ArgumentException("A rejection needs a reason other than Accepted.", nameof(reason));
            return Complete(commandId, false, reason);
        }

        private CommandResult Complete(Guid commandId, bool accepted, string reason)
        {
            int index = pending.FindIndex(entry => entry.Expedition.Id == commandId);
            if (index < 0) throw new InvalidOperationException("No pending submission with that id: the fake never invents results.");
            ExpeditionCommand command = pending[index].Expedition;
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
