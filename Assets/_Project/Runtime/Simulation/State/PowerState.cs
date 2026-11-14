namespace Trainer.Simulation
{
    /// <summary>
    /// Mutable electrical state owned by PowerSystem. Internal on purpose: other layers
    /// observe PowerSnapshot through IPowerSimulation or the session port, never this object.
    /// </summary>
    internal sealed class PowerState
    {
        /// <summary>Configuration in force since the last Reset (immutable type).</summary>
        public PowerConfiguration Configuration { get; private set; }

        /// <summary>Stored energy in joules, kept within [0, CapacityJoules].</summary>
        public double EnergyJoules { get; set; }

        /// <summary>Science demand from the most recent nonzero Advance; false after Reset.</summary>
        public bool ScienceRequested { get; set; }

        public PowerState(PowerConfiguration configuration)
        {
            Configuration = configuration;
            EnergyJoules = configuration.InitialEnergyJoules;
            ScienceRequested = false;
        }
    }
}
