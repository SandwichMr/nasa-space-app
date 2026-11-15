using System;
using Trainer.Application;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;

namespace Trainer.Unity.Networking.Expedition
{
    /// <summary>
    /// <see cref="IExpeditionSessionPort"/> for a player inside the authority process (host that also plays). It joins
    /// the same <see cref="IExpeditionAuthority"/> as remote players, so the host's astronaut is a normal member with its
    /// own PlayerId; it only skips the network. Results are filtered to this player; phase 2 commands are stamped with
    /// the local zone sequence exactly like the network adapter does. Leave (or Dispose) ends the identity once.
    /// </summary>
    public sealed class LocalExpeditionSessionPort : IExpeditionSessionPort, IDisposable
    {
        private readonly IExpeditionAuthority authority;
        private ExpeditionSessionSnapshot snapshot;
        private bool disposed;

        public OutpostMembership Membership { get; private set; } = OutpostMembership.NotJoined;
        public event Action<OutpostSessionSnapshot> SnapshotChanged;
        public event Action<ExpeditionSessionSnapshot> ExpeditionChanged;
        public event Action<CommandResult> CommandCompleted;
        public event Action<OutpostCommand, string> CommandAbandoned;
        public event Action<ExpeditionCommand, string> ExpeditionCommandAbandoned;
        public event Action<OutpostMembership> MembershipChanged;

        public LocalExpeditionSessionPort(IExpeditionAuthority authority)
        {
            this.authority = authority ?? throw new ArgumentNullException(nameof(authority));
            authority.SnapshotChanged += OnSnapshot;
            authority.CommandCompleted += OnResult;
        }

        public OutpostMembership Join()
        {
            if (disposed) throw new ObjectDisposedException(nameof(LocalExpeditionSessionPort));
            if (Membership.IsJoined) return Membership;
            OutpostMembership result = authority.Join(); // raises the snapshot with the new member first
            if (result.IsJoined && authority.TryGetSnapshot(out ExpeditionSessionSnapshot current)) Store(current);
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
            long zone = snapshot != null && snapshot.TryGetCrew(Membership.Player, out ExpeditionCrewState local)
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

        private void OnSnapshot(ExpeditionSessionSnapshot next)
        {
            if (next != null) Store(next);
        }

        private void Store(ExpeditionSessionSnapshot next)
        {
            if (!next.IsNewerThan(snapshot)) return;
            snapshot = next;
            SnapshotChanged?.Invoke(next.Outpost);
            ExpeditionChanged?.Invoke(next);
        }

        private void OnResult(PlayerId sender, CommandResult result)
        {
            if (Membership.IsJoined && sender == Membership.Player) CommandCompleted?.Invoke(result);
        }
    }
}
