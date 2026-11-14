namespace Trainer.Simulation
{
    /// <summary>Optional exact service observations; existing IPowerSimulation remains unchanged.</summary>
    public interface IIntegratedPowerSimulation : IPowerSimulation
    {
        /// <summary>Seconds of full science service during the last nonzero Advance, reset to zero by Reset.</summary>
        double LastFullyServedScienceSeconds { get; }
        /// <summary>Time until dispatch changes for this intention; infinity if steady, zero if
        /// essential service is already lost. Does not mutate the model.</summary>
        double SecondsUntilServiceChange(bool scienceRequested);
    }
}
