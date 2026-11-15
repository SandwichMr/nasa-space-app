using System;
using System.Collections.Generic;
using Trainer.Application.Outpost;

namespace Trainer.Application.Crew
{
    /// <summary>
    /// Client side of station interaction (plain C#). Sends StartTask/CancelTask for the LOCAL player through
    /// <see cref="IOutpostSessionPort"/> to the mission authority (D2), which alone decides: this class only
    /// pre-selects a station from the confirmed snapshot as a hint and never changes occupancy, progress or
    /// stocks. One request in flight at a time; its answer is the authority's CommandResult, or an abandonment
    /// (result unknown: the next snapshot is the truth).
    /// </summary>
    public sealed class StationInteractionClient : IDisposable
    {
        private readonly IOutpostSessionPort port;
        private readonly OutpostScenario scenario;
        private bool disposed;

        public OutpostCommand Pending { get; private set; }
        public CommandResult LastResult { get; private set; }
        public string LastAbandonReason { get; private set; }

        /// <summary>The authority answered the pending request.</summary>
        public event Action<OutpostCommand, CommandResult> Completed;
        /// <summary>The pending request will get no answer on this link (reason: NotConnected, ConnectionLost...).</summary>
        public event Action<OutpostCommand, string> Abandoned;

        public StationInteractionClient(IOutpostSessionPort port, OutpostScenario scenario)
        {
            this.port = port ?? throw new ArgumentNullException(nameof(port));
            this.scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
            port.CommandCompleted += OnCompleted;
            port.CommandAbandoned += OnAbandoned;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            port.CommandCompleted -= OnCompleted;
            port.CommandAbandoned -= OnAbandoned;
            Pending = null;
        }

        /// <summary>Tasks the scenario offers at <paramref name="station"/>, in scenario order.</summary>
        public IReadOnlyList<TaskDefinition> TasksAt(StationId station)
        {
            var tasks = new List<TaskDefinition>();
            foreach (TaskDefinition task in scenario.Tasks) if (task.Station == station) tasks.Add(task);
            return tasks;
        }

        /// <summary>Station whose reach contains the local player's confirmed position (nearest first).
        /// A hint for the UI: the authority re-checks reach on its own confirmed position.</summary>
        public bool TryFindStationInReach(out StationPlacement station)
        {
            station = null;
            if (!TryGetLocal(out OutpostSessionSnapshot _, out CrewMemberState local)) return false;
            double best = double.PositiveInfinity;
            foreach (StationPlacement placement in scenario.Layout.Stations)
            {
                double distance = placement.Position.DistanceTo(local.Pose.Position);
                if (placement.IsWithinReach(local.Pose.Position) && distance < best)
                {
                    best = distance;
                    station = placement;
                }
            }
            return station != null;
        }

        /// <summary>Station where the local player currently performs a task, if any.</summary>
        public bool TryGetOwnTask(out StationState station)
        {
            station = null;
            if (!TryGetLocal(out OutpostSessionSnapshot snapshot, out CrewMemberState local)) return false;
            foreach (StationState state in snapshot.Stations)
                if (state.Occupant == local.Player)
                {
                    station = state;
                    return true;
                }
            return false;
        }

        public bool RequestStart(StationId station, TaskTypeId task) =>
            Send(epoch => OutpostCommand.StartTask(Guid.NewGuid(), epoch, station, task));

        public bool RequestCancel(StationId station) =>
            Send(epoch => OutpostCommand.CancelTask(Guid.NewGuid(), epoch, station));

        /// <summary>"Interact" key: cancels the local player's own task if there is one; otherwise starts the
        /// first task offered by the nearest station in reach. False when nothing was sent.</summary>
        public bool Interact()
        {
            if (TryGetOwnTask(out StationState own)) return RequestCancel(own.Station);
            if (!TryFindStationInReach(out StationPlacement station)) return false;
            IReadOnlyList<TaskDefinition> tasks = TasksAt(station.Id);
            return tasks.Count > 0 && RequestStart(station.Id, tasks[0].Id);
        }

        private bool Send(Func<long, OutpostCommand> build)
        {
            if (disposed) throw new ObjectDisposedException(nameof(StationInteractionClient));
            if (Pending != null || !port.Membership.IsJoined || !port.TryGetSnapshot(out OutpostSessionSnapshot snapshot))
                return false;
            OutpostCommand command = build(snapshot.Epoch);
            Pending = command; // Before Submit: the answer may arrive synchronously.
            LastAbandonReason = null;
            try { port.Submit(command); }
            catch
            {
                // Other ports can also throw after an ambiguous delivery. Clear only this request if
                // no synchronous completion/abandonment already did it; never manufacture a result.
                if (ReferenceEquals(Pending, command)) OnAbandoned(command, OutpostReasons.ConnectionLost);
                throw;
            }
            return true;
        }

        private bool TryGetLocal(out OutpostSessionSnapshot snapshot, out CrewMemberState local)
        {
            local = null;
            snapshot = null;
            return port.Membership.IsJoined && port.TryGetSnapshot(out snapshot)
                && snapshot.TryGetCrew(port.Membership.Player, out local);
        }

        private void OnCompleted(CommandResult result)
        {
            OutpostCommand pending = Pending;
            if (pending == null || result.CommandId != pending.Id || result.Epoch != pending.Epoch) return;
            Pending = null;
            LastResult = result;
            Completed?.Invoke(pending, result);
        }

        private void OnAbandoned(OutpostCommand command, string reason)
        {
            if (!ReferenceEquals(command, Pending)) return;
            Pending = null;
            LastAbandonReason = reason;
            Abandoned?.Invoke(command, reason);
        }
    }
}
