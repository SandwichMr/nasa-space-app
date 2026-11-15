namespace Trainer.Simulation.LifeSupport
{
    /// <summary>
    /// Diagnostic mass ledger of <see cref="OutpostBaseSimulation"/> since the last Reset (immutable, kilograms).
    /// Not part of the published session snapshot; for the authority, tests and validation.
    ///
    /// Tracked system = potable + untreated + stored food + cabin O2 + cabin CO2 + O2 store. Closure:
    /// tracked(t) = tracked(0) − CrewOxygenIntake − CrewPotableWaterIntake − CrewFoodIntake
    ///              + CrewCarbonDioxideReturn + CrewUntreatedWaterReturn − CarbonDioxideRemoved − TransferBoundaryOutflow.
    /// O2 moved from the store to the cabin is internal and does not change the total.
    /// </summary>
    public sealed class LifeSupportLedger
    {
        public static readonly LifeSupportLedger Zero = new LifeSupportLedger(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        /// <summary>O2 left in the supply store.</summary>
        public double OxygenStoreKilograms { get; }
        /// <summary>O2 moved from the store into the cabin (internal transfer).</summary>
        public double OxygenSuppliedKilograms { get; }
        /// <summary>O2 respired by the crew (leaves the tracked system).</summary>
        public double CrewOxygenIntakeKilograms { get; }
        /// <summary>CO2 exhaled into the cabin (enters the tracked system).</summary>
        public double CrewCarbonDioxideReturnKilograms { get; }
        /// <summary>CO2 extracted by the removal service (leaves the modelled boundary).</summary>
        public double CarbonDioxideRemovedKilograms { get; }
        /// <summary>Potable water drunk by the crew.</summary>
        public double CrewPotableWaterIntakeKilograms { get; }
        /// <summary>Water returned by the crew into the untreated stock.</summary>
        public double CrewUntreatedWaterReturnKilograms { get; }
        /// <summary>Return that did not fit because the untreated stock was full (stays outside; not created).</summary>
        public double UntreatedReturnNotStoredKilograms { get; }
        /// <summary>Stored food eaten by the crew.</summary>
        public double CrewFoodIntakeKilograms { get; }
        /// <summary>Sum of BoundaryOutflowKilograms of applied transfers (negative = net inflow).</summary>
        public double TransferBoundaryOutflowKilograms { get; }
        /// <summary>Requested water intake not served because the potable stock was empty.</summary>
        public double UnservedPotableWaterKilograms { get; }
        /// <summary>Requested food intake not served because the food stock was empty.</summary>
        public double UnservedFoodKilograms { get; }

        public LifeSupportLedger(double oxygenStoreKilograms, double oxygenSuppliedKilograms,
            double crewOxygenIntakeKilograms, double crewCarbonDioxideReturnKilograms,
            double carbonDioxideRemovedKilograms, double crewPotableWaterIntakeKilograms,
            double crewUntreatedWaterReturnKilograms, double untreatedReturnNotStoredKilograms,
            double crewFoodIntakeKilograms, double transferBoundaryOutflowKilograms,
            double unservedPotableWaterKilograms, double unservedFoodKilograms)
        {
            OxygenStoreKilograms = oxygenStoreKilograms;
            OxygenSuppliedKilograms = oxygenSuppliedKilograms;
            CrewOxygenIntakeKilograms = crewOxygenIntakeKilograms;
            CrewCarbonDioxideReturnKilograms = crewCarbonDioxideReturnKilograms;
            CarbonDioxideRemovedKilograms = carbonDioxideRemovedKilograms;
            CrewPotableWaterIntakeKilograms = crewPotableWaterIntakeKilograms;
            CrewUntreatedWaterReturnKilograms = crewUntreatedWaterReturnKilograms;
            UntreatedReturnNotStoredKilograms = untreatedReturnNotStoredKilograms;
            CrewFoodIntakeKilograms = crewFoodIntakeKilograms;
            TransferBoundaryOutflowKilograms = transferBoundaryOutflowKilograms;
            UnservedPotableWaterKilograms = unservedPotableWaterKilograms;
            UnservedFoodKilograms = unservedFoodKilograms;
        }
    }
}
