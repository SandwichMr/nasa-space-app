using System;

namespace Trainer.Application
{
    public enum SessionAction { SetScienceEnabled, Pause, Resume, Restart }

    /// <summary>Intent only. Authority determines identity and permissions externally.</summary>
    public sealed class SessionCommand
    {
        public Guid Id { get; }
        public long Epoch { get; }
        public SessionAction Action { get; }
        public bool? RequestedScienceEnabled { get; }

        public SessionCommand(Guid id, long epoch, SessionAction action, bool? requestedScienceEnabled = null)
        {
            Id = id;
            Epoch = epoch;
            Action = action;
            RequestedScienceEnabled = requestedScienceEnabled;
        }
    }
}
