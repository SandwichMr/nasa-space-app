using Trainer.Simulation;

namespace Trainer.Application
{
    public sealed class MissionSnapshot
    {
        public long Epoch { get; }
        public long Revision { get; }
        public SessionState State { get; }
        public PowerSnapshot Power { get; }
        public double ScienceWorkSeconds { get; }
        public bool ScienceEnabled { get; }

        public MissionSnapshot(long epoch, long revision, SessionState state, PowerSnapshot power,
            double scienceWorkSeconds, bool scienceEnabled)
        {
            Epoch = epoch;
            Revision = revision;
            State = state;
            Power = power ?? throw new System.ArgumentNullException(nameof(power));
            ScienceWorkSeconds = scienceWorkSeconds;
            ScienceEnabled = scienceEnabled;
        }
    }
}
