namespace Trainer.Simulation
{
    /// <summary>Configuration values in joules and watts; all must be finite and nonnegative.
    /// InitialEnergyJoules must not exceed CapacityJoules.</summary>
    public sealed class PowerConfiguration
    {
        public double InitialEnergyJoules { get; }
        public double CapacityJoules { get; }
        public double GenerationWatts { get; }
        public double EssentialConsumptionWatts { get; }
        public double ScienceConsumptionWatts { get; }
        public double MaxChargeWatts { get; }
        public double MaxDischargeWatts { get; }

        public PowerConfiguration(double initialEnergyJoules, double capacityJoules,
            double generationWatts, double essentialConsumptionWatts, double scienceConsumptionWatts,
            double maxChargeWatts, double maxDischargeWatts)
        {
            double[] values = { initialEnergyJoules, capacityJoules, generationWatts,
                essentialConsumptionWatts, scienceConsumptionWatts, maxChargeWatts, maxDischargeWatts };
            foreach (double value in values)
                if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
                    throw new System.ArgumentOutOfRangeException(nameof(values));
            if (initialEnergyJoules > capacityJoules)
                throw new System.ArgumentException("Initial energy exceeds capacity.");
            InitialEnergyJoules = initialEnergyJoules;
            CapacityJoules = capacityJoules;
            GenerationWatts = generationWatts;
            EssentialConsumptionWatts = essentialConsumptionWatts;
            ScienceConsumptionWatts = scienceConsumptionWatts;
            MaxChargeWatts = maxChargeWatts;
            MaxDischargeWatts = maxDischargeWatts;
        }
    }
}
