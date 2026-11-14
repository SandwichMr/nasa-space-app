using System;
using System.Collections.Generic;
using Trainer.Simulation;

namespace Trainer.Application.Development
{
    /// <summary>DEVELOPMENT ONLY. Scripted examples; no physics, networking or permission validation.
    /// Single-threaded. Completion is immediate, but consumers must use CommandCompleted.</summary>
    public sealed class FakeSessionPort : ISessionPort
    {
        private readonly Dictionary<Guid, SessionCommand> commands = new Dictionary<Guid, SessionCommand>();
        private readonly Dictionary<Guid, CommandResult> results = new Dictionary<Guid, CommandResult>();
        private MissionSnapshot snapshot = Example(0, 0, SessionState.Ready, false);
        public event Action<MissionSnapshot> SnapshotChanged;
        public event Action<CommandResult> CommandCompleted;

        public bool TryGetSnapshot(out MissionSnapshot value) { value = snapshot; return true; }

        public void Submit(SessionCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (commands.TryGetValue(command.Id, out SessionCommand previous))
            {
                bool same = previous.Epoch == command.Epoch && previous.Action == command.Action
                    && previous.RequestedScienceEnabled == command.RequestedScienceEnabled;
                // Replay a prior result without applying any state change, even after Restart.
                CommandCompleted?.Invoke(same ? results[command.Id] :
                    new CommandResult(command.Id, command.Epoch, false, "CommandIdConflict"));
                return;
            }

            string rejection = Validate(command);
            MissionSnapshot next = snapshot;
            if (rejection == null)
            {
                if (command.Action == SessionAction.Restart)
                    next = Example(checked(snapshot.Epoch + 1), 0, SessionState.Ready, false);
                else
                {
                    SessionState state = command.Action == SessionAction.Pause ? SessionState.Paused :
                        command.Action == SessionAction.Resume ? SessionState.Running : snapshot.State;
                    bool science = command.Action == SessionAction.SetScienceEnabled
                        ? command.RequestedScienceEnabled.Value : snapshot.ScienceEnabled;
                    next = Example(snapshot.Epoch, checked(snapshot.Revision + 1), state, science, snapshot.ScienceWorkSeconds);
                }
            }
            var result = new CommandResult(command.Id, command.Epoch, rejection == null, rejection ?? "Accepted");
            commands.Add(command.Id, command);
            results.Add(command.Id, result);
            snapshot = next;
            // State and deduplication are committed before callbacks (which may submit more commands).
            try
            {
                if (result.Accepted) SnapshotChanged?.Invoke(next);
            }
            finally { CommandCompleted?.Invoke(result); }
        }

        private string Validate(SessionCommand command)
        {
            if (command.Id == Guid.Empty) return "InvalidCommandId";
            if (command.Epoch != snapshot.Epoch) return "EpochMismatch";
            if (!Enum.IsDefined(typeof(SessionAction), command.Action)) return "UnknownAction";
            if ((command.Action == SessionAction.SetScienceEnabled) != command.RequestedScienceEnabled.HasValue)
                return "InvalidActionValue";
            if (command.Action == SessionAction.Restart) return null;
            if (snapshot.State == SessionState.Completed || snapshot.State == SessionState.Interrupted)
                return "SessionEnded";
            if (command.Action == SessionAction.Pause && snapshot.State != SessionState.Running)
                return "InvalidState";
            if (command.Action == SessionAction.Resume && snapshot.State != SessionState.Ready
                && snapshot.State != SessionState.Paused) return "InvalidState";
            return null;
        }

        /// <summary>Publishes a scripted fixture in the current epoch. Never advances a simulation.</summary>
        public void PublishExample(SessionState state, double scienceWorkSeconds = 0)
        {
            if (!Enum.IsDefined(typeof(SessionState), state)) throw new ArgumentOutOfRangeException(nameof(state));
            if (double.IsNaN(scienceWorkSeconds) || double.IsInfinity(scienceWorkSeconds) || scienceWorkSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(scienceWorkSeconds));
            snapshot = Example(snapshot.Epoch, checked(snapshot.Revision + 1), state,
                snapshot.ScienceEnabled, scienceWorkSeconds);
            SnapshotChanged?.Invoke(snapshot);
        }

        private static MissionSnapshot Example(long epoch, long revision, SessionState state,
            bool science, double scienceWorkSeconds = 0)
        {
            return new MissionSnapshot(epoch, revision, state,
                new PowerSnapshot(0, 5000, 10000, 100, 60, 60, science ? 20 : 0, science ? 20 : 0),
                scienceWorkSeconds, science);
        }
    }
}
