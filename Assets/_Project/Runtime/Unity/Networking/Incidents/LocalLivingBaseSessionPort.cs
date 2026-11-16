using System;
using Trainer.Application;
using Trainer.Application.BaseOperations;
using Trainer.Application.Expedition;
using Trainer.Application.Incidents;
using Trainer.Application.Outpost;

namespace Trainer.Unity.Networking.Incidents
{
    /// <summary>
    /// <see cref="ILivingBaseSessionPort"/> for a player inside the authority process (host that also plays, D3). It joins the same
    /// <see cref="ILivingBaseAuthority"/> as remote players: a normal member with its own PlayerId and the same validation, only
    /// without the network. Results are filtered to this player; phase 2 commands are stamped with the local zone sequence like the
    /// network adapter. Events per stored revision: phase 2 → 3 → 4 → 5. Leave (or Dispose) ends the identity once.
    /// </summary>
    public sealed class LocalLivingBaseSessionPort : ILivingBaseSessionPort, IDisposable
    {
        private readonly ILivingBaseAuthority authority;
        private LivingBaseSnapshot snapshot;
        private bool disposed;

        public OutpostMembership Membership { get; private set; } = OutpostMembership.NotJoined;
        public event Action<OutpostSessionSnapshot> SnapshotChanged;
        public event Action<ExpeditionSessionSnapshot> ExpeditionChanged;
        public event Action<BaseOperationsSnapshot> BaseOperationsChanged;
        public event Action<LivingBaseSnapshot> LivingBaseChanged;
        public event Action<CommandResult> CommandCompleted;
        public event Action<OutpostCommand, string> CommandAbandoned;
        public event Action<ExpeditionCommand, string> ExpeditionCommandAbandoned;
        public event Action<BaseOperationCommand, string> BaseOperationCommandAbandoned;
        public event Action<IncidentCommand, string> IncidentCommandAbandoned;
        public event Action<OutpostMembership> MembershipChanged;

        public LocalLivingBaseSessionPort(ILivingBaseAuthority authority)
        {
            this.authority = authority ?? throw new ArgumentNullException(nameof(authority));
            authority.SnapshotChanged += OnSnapshot;
            authority.CommandCompleted += OnResult;
        }

        public OutpostMembership Join()
        {
            if (disposed) throw new ObjectDisposedException(nameof(LocalLivingBaseSessionPort));
            if (Membership.IsJoined) return Membership;
            OutpostMembership result = authority.Join();
            if (result.IsJoined && authority.TryGetSnapshot(out LivingBaseSnapshot current) && current != null) Store(current);
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
            value = snapshot?.BaseOperations.Outpost;
            return value != null;
        }

        public bool TryGetExpedition(out ExpeditionSessionSnapshot value)
        {
            value = snapshot?.BaseOperations.Expedition;
            return value != null;
        }

        public bool TryGetBaseOperations(out BaseOperationsSnapshot value)
        {
            value = snapshot?.BaseOperations;
            return value != null;
        }

        public bool TryGetLivingBase(out LivingBaseSnapshot value)
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
            long zone = snapshot != null && snapshot.BaseOperations.Expedition.TryGetCrew(Membership.Player, out ExpeditionCrewState local)
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

        public void Submit(IncidentCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (disposed || !Membership.IsJoined)
            {
                IncidentCommandAbandoned?.Invoke(command, OutpostReasons.NotConnected);
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

        private void OnSnapshot(LivingBaseSnapshot next)
        {
            if (next != null) Store(next);
        }

        private void Store(LivingBaseSnapshot next)
        {
            if (snapshot != null && !next.BaseOperations.IsNewerThan(snapshot.BaseOperations)) return;
            snapshot = next;
            SnapshotChanged?.Invoke(next.BaseOperations.Outpost);
            ExpeditionChanged?.Invoke(next.BaseOperations.Expedition);
            BaseOperationsChanged?.Invoke(next.BaseOperations);
            LivingBaseChanged?.Invoke(next);
        }

        private void OnResult(PlayerId sender, CommandResult result)
        {
            if (Membership.IsJoined && sender == Membership.Player) CommandCompleted?.Invoke(result);
        }
    }
}
