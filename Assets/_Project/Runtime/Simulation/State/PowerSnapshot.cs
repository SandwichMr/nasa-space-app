namespace Trainer.Simulation
{
    /// <summary>Immutable observation at SimulatedTimeSeconds. Power values are instantaneous watts.</summary>
    public sealed class PowerSnapshot
    {
        public double SimulatedTimeSeconds { get; }
        public double EnergyJoules { get; }
        public double CapacityJoules { get; }
        public double GenerationWatts { get; }
        public double EssentialRequestedWatts { get; }
        public double EssentialServedWatts { get; }
        public double ScienceRequestedWatts { get; }
        public double ScienceServedWatts { get; }

        public PowerSnapshot(double simulatedTimeSeconds, double energyJoules, double capacityJoules,
            double generationWatts, double essentialRequestedWatts, double essentialServedWatts,
            double scienceRequestedWatts, double scienceServedWatts)
        {
            SimulatedTimeSeconds = simulatedTimeSeconds;
            EnergyJoules = energyJoules;
            CapacityJoules = capacityJoules;
            GenerationWatts = generationWatts;
            EssentialRequestedWatts = essentialRequestedWatts;
            EssentialServedWatts = essentialServedWatts;
            ScienceRequestedWatts = scienceRequestedWatts;
            ScienceServedWatts = scienceServedWatts;
        }
    }
}
