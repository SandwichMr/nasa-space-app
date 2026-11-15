using System;

namespace Trainer.Simulation.Crew
{
    /// <summary>
    /// Continuous demand of ONE crew member inside the modelled cabin, in kilograms per simulated second
    /// (immutable). The phase 2 simulation multiplies it by <c>OutpostDemand.CrewAboard</c> inside
    /// <c>IOutpostSimulation.Advance</c>; nothing else in the game applies crew demand.
    ///
    /// The crew is a model boundary: what a member takes in (O2, potable water, stored food) leaves the tracked
    /// stocks; what returns (exhaled CO2 into the cabin, captured water into the untreated stock) re-enters.
    /// Body storage, metabolic water, nutrients and solid waste are not modelled. Values are scenario inputs
    /// with their own <see cref="Provenance"/>: this type carries no universal rate, recovery fraction or
    /// clinical meaning.
    /// </summary>
    public sealed class CrewMetabolicDemand
    {
        /// <summary>Seconds in one day, for the per-crew-day helper.</summary>
        public const double SecondsPerDay = 86400d;

        /// <summary>Oxygen taken from the cabin gas, kg/s per member.</summary>
        public double OxygenConsumptionKilogramsPerSecond { get; }
        /// <summary>Carbon dioxide released into the cabin gas, kg/s per member, while respiration is served.</summary>
        public double CarbonDioxideProductionKilogramsPerSecond { get; }
        /// <summary>Potable water drunk, kg/s per member.</summary>
        public double PotableWaterIntakeKilogramsPerSecond { get; }
        /// <summary>Stored food eaten (as stowed, moisture included), kg/s per member.</summary>
        public double FoodIntakeKilogramsPerSecond { get; }
        /// <summary>Water returned to the UNTREATED stock while the member drinks (for example captured
        /// condensate/urine), kg/s per member. Never above the potable intake, so water is never created:
        /// the difference leaves through the crew boundary. It is not potable water.</summary>
        public double UntreatedWaterReturnKilogramsPerSecond { get; }
        /// <summary>Where the numbers come from (synthetic, contextual reference, ...). Required.</summary>
        public string Provenance { get; }

        public CrewMetabolicDemand(double oxygenConsumptionKilogramsPerSecond,
            double carbonDioxideProductionKilogramsPerSecond, double potableWaterIntakeKilogramsPerSecond,
            double foodIntakeKilogramsPerSecond, double untreatedWaterReturnKilogramsPerSecond, string provenance)
        {
            OxygenConsumptionKilogramsPerSecond = NonNegative(oxygenConsumptionKilogramsPerSecond, nameof(oxygenConsumptionKilogramsPerSecond));
            CarbonDioxideProductionKilogramsPerSecond = NonNegative(carbonDioxideProductionKilogramsPerSecond, nameof(carbonDioxideProductionKilogramsPerSecond));
            PotableWaterIntakeKilogramsPerSecond = NonNegative(potableWaterIntakeKilogramsPerSecond, nameof(potableWaterIntakeKilogramsPerSecond));
            FoodIntakeKilogramsPerSecond = NonNegative(foodIntakeKilogramsPerSecond, nameof(foodIntakeKilogramsPerSecond));
            UntreatedWaterReturnKilogramsPerSecond = NonNegative(untreatedWaterReturnKilogramsPerSecond, nameof(untreatedWaterReturnKilogramsPerSecond));
            if (untreatedWaterReturnKilogramsPerSecond > potableWaterIntakeKilogramsPerSecond)
                throw new ArgumentException("Untreated water return cannot exceed potable intake (no water from nothing).",
                    nameof(untreatedWaterReturnKilogramsPerSecond));
            if (string.IsNullOrWhiteSpace(provenance))
                throw new ArgumentException("Provenance is required.", nameof(provenance));
            Provenance = provenance;
        }

        /// <summary>Builds the demand from kilograms per crew member per day (converted with 86 400 s/day).</summary>
        public static CrewMetabolicDemand FromPerCrewDay(double oxygenKilograms, double carbonDioxideKilograms,
            double potableWaterKilograms, double foodKilograms, double untreatedWaterReturnKilograms, string provenance) =>
            new CrewMetabolicDemand(oxygenKilograms / SecondsPerDay, carbonDioxideKilograms / SecondsPerDay,
                potableWaterKilograms / SecondsPerDay, foodKilograms / SecondsPerDay,
                untreatedWaterReturnKilograms / SecondsPerDay, provenance);

        internal static double NonNegative(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0d)
                throw new ArgumentOutOfRangeException(name, value, "Must be finite and nonnegative.");
            return value;
        }
    }
}
