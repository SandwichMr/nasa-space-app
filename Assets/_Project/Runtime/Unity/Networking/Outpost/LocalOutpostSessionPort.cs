using System;
using Trainer.Application;
using Trainer.Application.Outpost;

namespace Trainer.Unity.Networking.Outpost
{
    /// <summary>
    /// <see cref="IOutpostSessionPort"/> for a player inside the authority process (host with HUD or a single
    /// process run). It joins the same <see cref="IOutpostAuthority"/> as remote players, so the host's astronaut
    /// is a normal crew member with its own PlayerId; it only skips the network. Results are filtered to this
    /// player. <see cref="Leave"/> (or Dispose) ends the identity at the authority exactly once.
    /// </summary>
    public sealed class LocalOutpostSessionPort : IOutpostSessionPort, IDisposable
    {
        private readonly IOutpostAuthority authority;
        private OutpostSessionSnapshot snapshot;
        private bool disposed;

        public OutpostMembership Membership { get; private set; } = OutpostMembership.NotJoined;
        public event Action<OutpostSessionSnapshot> SnapshotChanged;
        public event Action<CommandResult> CommandCompleted;
        public event Action<OutpostCommand, string> CommandAbandoned;
        public event Action<OutpostMembership> MembershipChanged;

        public LocalOutpostSessionPort(IOutpostAuthority authority)
        {
            this.authority = authority ?? throw new ArgumentNullException(nameof(authority));
            authority.SnapshotChanged += OnSnapshot;
            authority.CommandCompleted += OnResult;
        }

        /// <summary>Joins the authority. Returns the membership (Joined, or Rejected with SessionFull).</summary>
        public OutpostMembership Join()
        {
            if (disposed) throw new ObjectDisposedException(nameof(LocalOutpostSessionPort));
            if (Membership.IsJoined) return Membership;
            OutpostMembership result = authority.Join(); // Raises the snapshot with the new member first.
            if (result.IsJoined && authority.TryGetSnapshot(out OutpostSessionSnapshot current)) Store(current);
            Membership = result;
            MembershipChanged?.Invoke(result);
            return result;
        }

        /// <summary>Ends this identity at the authority (its task is cancelled there, it leaves the crew).</summary>
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

        private void OnSnapshot(OutpostSessionSnapshot next)
        {
            if (next != null) Store(next);
        }

        private void Store(OutpostSessionSnapshot next)
        {
            if (!next.IsNewerThan(snapshot)) return;
            snapshot = next;
            SnapshotChanged?.Invoke(next);
        }

        private void OnResult(PlayerId sender, CommandResult result)
        {
            if (Membership.IsJoined && sender == Membership.Player) CommandCompleted?.Invoke(result);
        }
    }
}
