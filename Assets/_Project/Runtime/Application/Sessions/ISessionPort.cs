using System;

namespace Trainer.Application
{
    public interface ISessionPort
    {
        void Submit(SessionCommand command);
        bool TryGetSnapshot(out MissionSnapshot snapshot);
        event Action<MissionSnapshot> SnapshotChanged;
        event Action<CommandResult> CommandCompleted;
    }
}
