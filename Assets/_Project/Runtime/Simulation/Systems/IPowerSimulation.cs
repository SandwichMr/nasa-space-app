namespace Trainer.Simulation
{
    public interface IPowerSimulation
    {
        /// <summary>Initializes or resets energy and time (to zero) using the supplied configuration.</summary>
        void Reset(PowerConfiguration configuration);
        /// <summary>Requires Reset first; returns an immutable observation.</summary>
        PowerSnapshot Snapshot { get; }
        /// <summary>Requires Reset first and finite, nonnegative elapsedSeconds; zero is a no-op.
        /// scienceRequested expresses demand, not guaranteed service.</summary>
        void Advance(double elapsedSeconds, bool scienceRequested);
    }
}
