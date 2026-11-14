using System;

namespace Trainer.Application
{
    public sealed class CommandResult
    {
        public Guid CommandId { get; }
        /// <summary>Epoch supplied by the command, including for Restart.</summary>
        public long Epoch { get; }
        public bool Accepted { get; }
        public string Reason { get; }

        public CommandResult(Guid commandId, long epoch, bool accepted, string reason)
        {
            CommandId = commandId;
            Epoch = epoch;
            Accepted = accepted;
            Reason = reason ?? string.Empty;
        }
    }
}
