using System;
using Trainer.Application.Expedition;
using Trainer.Application.Incidents;
using Trainer.Application.Outpost;

namespace Trainer.Application.Crew
{
    /// <summary>
    /// Phase 5 intents of the LOCAL player (D3, implements <see cref="ILivingBaseIntents"/>). It stamps the epoch, the zone sequence
    /// and the CURRENT power observation from the last confirmed snapshot; the view never invents them and the client never
    /// predicts the answer. One request in flight; a result, an abandonment or a lost membership releases it. Pure C#.
    /// </summary>
    public sealed class LivingBaseInteractionClient : ILivingBaseIntents, IDisposable
    {
        private readonly ILivingBaseSessionPort port;
        private readonly LivingBaseScenario scenario;
        private bool disposed;

        public IncidentCommand Pending { get; private set; }
        public bool HasPending => Pending != null;
        public string LastReason { get; private set; }
        public event Action<string> Answered;

        public LivingBaseInteractionClient(ILivingBaseSessionPort port, LivingBaseScenario scenario)
        {
            this.port = port ?? throw new ArgumentNullException(nameof(port));
            this.scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
            port.CommandCompleted += OnCompleted;
            port.IncidentCommandAbandoned += OnAbandoned;
            port.MembershipChanged += OnMembership;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            port.CommandCompleted -= OnCompleted;
            port.IncidentCommandAbandoned -= OnAbandoned;
            port.MembershipChanged -= OnMembership;
            Pending = null;
        }

        /// <summary>The phase 5 stations: the power panel and the upgrade station.</summary>
        public bool IsLivingBaseStation(StationId station) =>
            station.IsValid && (station == scenario.PowerStation || station == scenario.Upgrade.Station);

        /// <summary>Nearest phase 5 station whose reach contains the local player (confirmed pose).</summary>
        public bool TryFindStationInReach(out StationPlacement station)
        {
            station = null;
            if (!TryGetLocal(out LivingBaseSnapshot snapshot, out _)) return false;
            if (!snapshot.BaseOperations.Outpost.TryGetCrew(port.Membership.Player, out CrewMemberState pose)) return false;
            double best = double.PositiveInfinity;
            foreach (StationPlacement placement in scenario.BaseOperations.Layout.Stations)
            {
                if (!IsLivingBaseStation(placement.Id) || !placement.IsWithinReach(pose.Pose.Position)) continue;
                double distance = placement.Position.DistanceTo(pose.Pose.Position);
                if (distance < best)
                {
                    best = distance;
                    station = placement;
                }
            }
            return station != null;
        }

        public bool TryGetOwnOperation(out IncidentOperationProgress operation)
        {
            operation = null;
            if (!TryGetLocal(out LivingBaseSnapshot snapshot, out _)) return false;
            foreach (IncidentOperationProgress op in snapshot.Operations)
                if (op.Performer == port.Membership.Player)
                {
                    operation = op;
                    return true;
                }
            return false;
        }

        /// <summary>The power observation the authority still marks current, if any.</summary>
        public bool TryGetCurrentObservation(out PowerObservation observation)
        {
            observation = null;
            if (!TryGetLocal(out LivingBaseSnapshot snapshot, out _)) return false;
            PowerObservation latest = snapshot.Power.Observation;
            if (latest == null || !latest.Current) return false;
            observation = latest;
            return true;
        }

        /// <summary>E with an own phase 5 operation in progress cancels it.</summary>
        public bool Interact() => TryGetOwnOperation(out IncidentOperationProgress own) && RequestCancelOperation(own.Station);

        public bool RequestPower(IncidentAction action)
        {
            if (!IncidentCommand.IsPowerAction(action)) return false;
            return Send((snapshot, me) =>
            {
                long observation = 0;
                if (IncidentCommand.NeedsObservation(action))
                {
                    if (!TryGetCurrentObservation(out PowerObservation current)) return null;
                    observation = current.Sequence;
                }
                return new IncidentCommand(Guid.NewGuid(), snapshot.Epoch, action, scenario.PowerStation, 0, observation, Zone(me));
            });
        }

        public bool RequestAssignment(IncidentAction action, long assignmentSequence)
        {
            if (!IncidentCommand.IsAssignmentAction(action) || assignmentSequence < 1) return false;
            return Send((snapshot, me) => new IncidentCommand(Guid.NewGuid(), snapshot.Epoch, action, StationId.None, assignmentSequence, 0, 0));
        }

        public bool RequestInstallUpgrade() =>
            Send((snapshot, me) => new IncidentCommand(Guid.NewGuid(), snapshot.Epoch, IncidentAction.InstallUpgrade, scenario.Upgrade.Station, 0, 0,
                Zone(me)));

        public bool RequestCancelOperation(StationId station) =>
            Send((snapshot, me) => station.IsValid
                ? new IncidentCommand(Guid.NewGuid(), snapshot.Epoch, IncidentAction.CancelOperation, station, 0, 0, Zone(me)) : null);

        private static long Zone(ExpeditionCrewState me) => me.ZoneSequence < 0 ? 0 : me.ZoneSequence;

        private bool Send(Func<LivingBaseSnapshot, ExpeditionCrewState, IncidentCommand> build)
        {
            if (disposed) throw new ObjectDisposedException(nameof(LivingBaseInteractionClient));
            if (Pending != null || !TryGetLocal(out LivingBaseSnapshot snapshot, out ExpeditionCrewState me)) return false;
            IncidentCommand command = build(snapshot, me);
            if (command == null || command.ShapeError() != null) return false;
            Pending = command; // before Submit: the answer may arrive synchronously
            try
            {
                port.Submit(command);
            }
            catch (Exception)
            {
                if (ReferenceEquals(Pending, command)) Release(OutpostReasons.ConnectionLost);
            }
            return true;
        }

        private bool TryGetLocal(out LivingBaseSnapshot snapshot, out ExpeditionCrewState me)
        {
            me = null;
            snapshot = null;
            return port.Membership.IsJoined && port.TryGetLivingBase(out snapshot) && snapshot != null
                && snapshot.BaseOperations.Expedition.TryGetCrew(port.Membership.Player, out me);
        }

        private void OnCompleted(CommandResult result)
        {
            IncidentCommand pending = Pending;
            if (pending == null || result == null || result.CommandId != pending.Id || result.Epoch != pending.Epoch) return;
            Release(result.Reason);
        }

        private void OnAbandoned(IncidentCommand command, string reason)
        {
            if (command != null && ReferenceEquals(command, Pending)) Release(reason);
        }

        private void OnMembership(OutpostMembership membership)
        {
            if (Pending == null || membership.IsJoined) return;
            Release(membership.Reason == OutpostReasons.LocalDisconnect ? OutpostReasons.LocalDisconnect : OutpostReasons.ConnectionLost);
        }

        private void Release(string reason)
        {
            Pending = null;
            LastReason = reason;
            Answered?.Invoke(reason);
        }
    }
}
