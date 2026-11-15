using System;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;

namespace Trainer.Application.Crew
{
    /// <summary>
    /// Client side of phase 3 interaction for the LOCAL player (D3, plain C#), the implementation of
    /// <see cref="IExpeditionIntents"/> that the scene hands to D4's views. It pre-selects from the CONFIRMED snapshot
    /// (nearest station in reach) and stamps every location-dependent intent with the local zone sequence and the
    /// airlock cycle it saw, so an intent made before a zone change or another cycle is refused by the authority
    /// instead of acting on a different situation. It never decides, moves anyone or changes state; one request in
    /// flight; its answer is the authority's result or an abandonment (result unknown: the next snapshot is the truth).
    /// </summary>
    public sealed class ExpeditionInteractionClient : IExpeditionIntents, IDisposable
    {
        private readonly IExpeditionSessionPort port;
        private readonly ExpeditionScenario scenario;
        private bool disposed;

        public ExpeditionCommand Pending { get; private set; }
        public bool HasPending => Pending != null;
        public string LastReason { get; private set; }
        public event Action<string> Answered;

        public ExpeditionInteractionClient(IExpeditionSessionPort port, ExpeditionScenario scenario)
        {
            this.port = port ?? throw new ArgumentNullException(nameof(port));
            this.scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
            port.CommandCompleted += OnCompleted;
            port.ExpeditionCommandAbandoned += OnAbandoned;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            port.CommandCompleted -= OnCompleted;
            port.ExpeditionCommandAbandoned -= OnAbandoned;
            Pending = null;
        }

        /// <summary>Station whose reach contains the local confirmed position, nearest first (a UI hint only).</summary>
        public bool TryFindStationInReach(out StationPlacement station)
        {
            station = null;
            if (!TryGetLocal(out _, out CrewMemberState pose, out _)) return false;
            double best = double.PositiveInfinity;
            foreach (StationPlacement placement in scenario.Layout.Stations)
            {
                double distance = placement.Position.DistanceTo(pose.Pose.Position);
                if (placement.IsWithinReach(pose.Pose.Position) && distance < best)
                {
                    best = distance;
                    station = placement;
                }
            }
            return station != null;
        }

        public bool Interact()
        {
            if (!TryGetLocal(out ExpeditionSessionSnapshot snapshot, out CrewMemberState local, out _)) return false;
            foreach (StationState state in snapshot.Outpost.Stations)
                if (state.Occupant == local.Player) return RequestCancel(state.Station);
            if (!TryFindStationInReach(out StationPlacement station)) return false;
            if (scenario.Airlock.IsPanel(station.Id)) return RequestCycle(station.Id);
            foreach (TaskDefinition task in scenario.Outpost.Tasks)
                if (task.Station == station.Id) return RequestStart(station.Id, task.Id);
            return false;
        }

        public bool RequestStart(StationId station, TaskTypeId task) =>
            Send((snapshot, me) => ExpeditionCommand.StartTask(Guid.NewGuid(), snapshot.Epoch, station, task, me.ZoneSequence));

        public bool RequestCancel(StationId station) =>
            Send((snapshot, me) => ExpeditionCommand.CancelTask(Guid.NewGuid(), snapshot.Epoch, station));

        public bool RequestCycle(StationId airlockPanel) =>
            Send((snapshot, me) => ExpeditionCommand.CycleAirlock(Guid.NewGuid(), snapshot.Epoch, airlockPanel, me.ZoneSequence,
                snapshot.Airlock.CycleSequence));

        public bool RequestDeclareReturn() => Send((snapshot, me) => ExpeditionCommand.DeclareReturn(Guid.NewGuid(), snapshot.Epoch));

        private bool Send(Func<ExpeditionSessionSnapshot, ExpeditionCrewState, ExpeditionCommand> build)
        {
            if (disposed) throw new ObjectDisposedException(nameof(ExpeditionInteractionClient));
            if (Pending != null || !TryGetLocal(out ExpeditionSessionSnapshot snapshot, out _, out ExpeditionCrewState me)) return false;
            ExpeditionCommand command = build(snapshot, me);
            Pending = command; // before Submit: the answer may arrive synchronously
            try { port.Submit(command); }
            catch
            {
                if (ReferenceEquals(Pending, command)) OnAbandoned(command, OutpostReasons.ConnectionLost);
                throw;
            }
            return true;
        }

        private bool TryGetLocal(out ExpeditionSessionSnapshot snapshot, out CrewMemberState pose, out ExpeditionCrewState me)
        {
            pose = null;
            me = null;
            snapshot = null;
            return port.Membership.IsJoined && port.TryGetExpedition(out snapshot)
                && snapshot.Outpost.TryGetCrew(port.Membership.Player, out pose) && snapshot.TryGetCrew(port.Membership.Player, out me);
        }

        private void OnCompleted(CommandResult result)
        {
            ExpeditionCommand pending = Pending;
            if (pending == null || result.CommandId != pending.Id || result.Epoch != pending.Epoch) return;
            Pending = null;
            LastReason = result.Reason;
            Answered?.Invoke(result.Reason);
        }

        private void OnAbandoned(ExpeditionCommand command, string reason)
        {
            if (!ReferenceEquals(command, Pending)) return;
            Pending = null;
            LastReason = reason;
            Answered?.Invoke(reason);
        }
    }
}
