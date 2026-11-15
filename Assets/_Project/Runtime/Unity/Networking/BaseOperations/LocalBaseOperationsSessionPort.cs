using System;
using Trainer.Application;
using Trainer.Application.BaseOperations;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;

namespace Trainer.Unity.Networking.BaseOperations
{
    /// <summary>
    /// <see cref="IBaseOperationsSessionPort"/> for a player inside the authority process (host that also plays). It joins the
    /// same <see cref="IBaseOperationsAuthority"/> as remote players, so the host's astronaut is a normal member with its own
    /// PlayerId and the same validation; it only skips the network. Results are filtered to this player; phase 2 commands are
    /// stamped with the local zone sequence exactly like the network adapter. Events per stored revision: phase 2 → phase 3 →
    /// phase 4. Leave (or Dispose) ends the identity once; the authority then releases its operation and reservations.
    /// </summary>
    public sealed class LocalBaseOperationsSessionPort : IBaseOperationsSessionPort, IDisposable
    {
        private readonly IBaseOperationsAuthority authority;
        private BaseOperationsSnapshot snapshot;
        private bool disposed;

        public OutpostMembership Membership { get; private set; } = OutpostMembership.NotJoined;
        public event Action<OutpostSessionSnapshot> SnapshotChanged;
        public event Action<ExpeditionSessionSnapshot> ExpeditionChanged;
        public event Action<BaseOperationsSnapshot> BaseOperationsChanged;
        public event Action<CommandResult> CommandCompleted;
        public event Action<OutpostCommand, string> CommandAbandoned;
        public event Action<ExpeditionCommand, string> ExpeditionCommandAbandoned;
        public event Action<BaseOperationCommand, string> BaseOperationCommandAbandoned;
        public event Action<OutpostMembership> MembershipChanged;

        public LocalBaseOperationsSessionPort(IBaseOperationsAuthority authority)
        {
            this.authority = authority ?? throw new ArgumentNullException(nameof(authority));
            authority.SnapshotChanged += OnSnapshot;
            authority.CommandCompleted += OnResult;
        }

        public OutpostMembership Join()
        {
            if (disposed) throw new ObjectDisposedException(nameof(LocalBaseOperationsSessionPort));
            if (Membership.IsJoined) return Membership;
            OutpostMembership result = authority.Join(); // raises the snapshot with the new member first
            if (result.IsJoined && authority.TryGetSnapshot(out BaseOperationsSnapshot current) && current != null) Store(current);
            Membership = result;
            MembershipChanged?.Invoke(result);
            return result;
        }

        public void Leave(string reason = OutpostReasons.LocalDisconnect)
        {
            if (!Membership.IsJoined) return;
            PlayerId player = Membership.Player;
            Membership = OutpostMembership.Left(player, reason);
            authority.Leave(player);
            MembershipChanged?.Invoke(Membership);
        }

        public bool TryGetSnapshot(out OutpostSessionSnapshot value)
        {
            value = snapshot?.Outpost;
            return value != null;
        }

        public bool TryGetExpedition(out ExpeditionSessionSnapshot value)
        {
            value = snapshot?.Expedition;
            return value != null;
        }

        public bool TryGetBaseOperations(out BaseOperationsSnapshot value)
        {
            value = snapshot;
            return value != null;
        }

        public void Submit(OutpostCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (disposed || !Membership.IsJoined)
            {
                CommandAbandoned?.Invoke(command, OutpostReasons.NotConnected);
                return;
            }
            long zone = snapshot != null && snapshot.Expedition.TryGetCrew(Membership.Player, out ExpeditionCrewState local)
                ? local.ZoneSequence : ExpeditionCommand.Unstated;
            authority.Submit(Membership.Player, ExpeditionCommand.FromOutpost(command, zone));
        }

        public void Submit(ExpeditionCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (disposed || !Membership.IsJoined)
            {
                ExpeditionCommandAbandoned?.Invoke(command, OutpostReasons.NotConnected);
                return;
            }
            authority.Submit(Membership.Player, command);
        }

        public void Submit(BaseOperationCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (disposed || !Membership.IsJoined)
            {
                BaseOperationCommandAbandoned?.Invoke(command, OutpostReasons.NotConnected);
                return;
            }
            authority.Submit(Membership.Player, command);
        }

        public void SubmitMovement(MovementInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            if (disposed || !Membership.IsJoined) return;
            authority.SubmitMovement(Membership.Player, input);
        }

        public void Dispose()
        {
            if (disposed) return;
            try
            {
                Leave();
            }
            finally
            {
                disposed = true;
                authority.SnapshotChanged -= OnSnapshot;
                authority.CommandCompleted -= OnResult;
            }
        }

        private void OnSnapshot(BaseOperationsSnapshot next)
        {
            if (next != null) Store(next);
        }

        private void Store(BaseOperationsSnapshot next)
        {
            if (!next.IsNewerThan(snapshot)) return;
            snapshot = next;
            SnapshotChanged?.Invoke(next.Outpost);
            ExpeditionChanged?.Invoke(next.Expedition);
            BaseOperationsChanged?.Invoke(next);
        }

        private void OnResult(PlayerId sender, CommandResult result)
        {
            if (Membership.IsJoined && sender == Membership.Player) CommandCompleted?.Invoke(result);
        }
    }
}
