using System;
using Trainer.Simulation.Crew;

namespace Trainer.Simulation.LifeSupport
{
    /// <summary>
    /// Reduced cabin air services (immutable). Oxygen SUPPLY and carbon dioxide REMOVAL are separate devices with
    /// separate limits: supplying O2 never removes CO2 and removing CO2 never adds O2. Both are part of the phase 1
    /// essential electrical load (no extra watts): they run only while the essential load is fully served.
    ///
    /// Control model (declared simplification): the O2 supply adds up to its maximum flow from a finite store
    /// until the cabin O2 partial pressure returns to the scenario's initial value, then follows consumption;
    /// the CO2 removal extracts up to its maximum flow while the cabin CO2 partial pressure is above the
    /// scenario's initial value, then follows production. The initial composition is the only target used:
    /// no alarm, clinical or safety threshold exists in this model. Removed CO2 leaves the modelled boundary.
    /// </summary>
    public sealed class AirRevitalizationParameters
    {
        /// <summary>Oxygen available to the supply at Reset, kg. Not a published stock; see the ledger.</summary>
        public double OxygenStoreKilograms { get; }
        /// <summary>Maximum O2 flow from the store into the cabin, kg/s.</summary>
        public double MaxOxygenSupplyKilogramsPerSecond { get; }
        /// <summary>Maximum CO2 flow removed from the cabin, kg/s.</summary>
        public double MaxCarbonDioxideRemovalKilogramsPerSecond { get; }
        /// <summary>Where the numbers come from. Required.</summary>
        public string Provenance { get; }

        public AirRevitalizationParameters(double oxygenStoreKilograms, double maxOxygenSupplyKilogramsPerSecond,
            double maxCarbonDioxideRemovalKilogramsPerSecond, string provenance)
        {
            OxygenStoreKilograms = CrewMetabolicDemand.NonNegative(oxygenStoreKilograms, nameof(oxygenStoreKilograms));
            MaxOxygenSupplyKilogramsPerSecond = CrewMetabolicDemand.NonNegative(maxOxygenSupplyKilogramsPerSecond, nameof(maxOxygenSupplyKilogramsPerSecond));
            MaxCarbonDioxideRemovalKilogramsPerSecond = CrewMetabolicDemand.NonNegative(maxCarbonDioxideRemovalKilogramsPerSecond, nameof(maxCarbonDioxideRemovalKilogramsPerSecond));
            if (string.IsNullOrWhiteSpace(provenance))
                throw new ArgumentException("Provenance is required.", nameof(provenance));
            Provenance = provenance;
        }
    }
}
