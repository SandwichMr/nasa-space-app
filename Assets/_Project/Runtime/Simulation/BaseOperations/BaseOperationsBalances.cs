using System;
using Trainer.Simulation.Eva;
using Trainer.Simulation.LifeSupport;

namespace Trainer.Simulation.BaseOperations
{
    /// <summary>
    /// Mass-balance and demand audits for phase 4 (D1, plain C#, read-only). For the authority's own checks, tests and
    /// D5 validators; nothing here changes a model. Kilograms.
    /// </summary>
    public static class BaseOperationsBalances
    {
        /// <summary>
        /// Processor closure: batch input − brine − released to potable − released for reprocess − hold. Zero up to
        /// rounding for any sequence of applied effects.
        /// </summary>
        public static double ProcessorResidualKilograms(BaseEquipmentSnapshot equipment)
        {
            if (equipment == null) throw new ArgumentNullException(nameof(equipment));
            EquipmentLedger l = equipment.Ledger;
            return l.BatchInputKilograms - l.BrineOutflowKilograms - l.ReleasedToPotableKilograms
                   - l.ReleasedForReprocessKilograms - equipment.WaterProcessor.HoldKilograms;
        }

        /// <summary>Water and food inside the base boundary: potable + untreated + stored food + processor hold.</summary>
        public static double WaterAndFoodKilograms(LifeSupportSnapshot life, BaseEquipmentSnapshot equipment)
        {
            if (life == null) throw new ArgumentNullException(nameof(life));
            if (equipment == null) throw new ArgumentNullException(nameof(equipment));
            return life.Water.PotableKilograms + life.Water.UntreatedKilograms + life.Food.StoredKilograms
                   + equipment.WaterProcessor.HoldKilograms;
        }

        /// <summary>
        /// Closure of water + food across the base AND the processor, using only flows that cross the outer boundary:
        /// <code>
        /// tracked − [initial − crew water − crew food + crew return stored + externalIn − externalOut − brine]
        /// </code>
        /// <paramref name="externalInflowKilograms"/> = unloaded deliveries (water + food); <paramref name="externalOutflowKilograms"/>
        /// = other boundary transfers of the base such as phase 3 suit fills. Transfers between the base and the processor
        /// cancel out, so a pair applied on one side only shows up as a nonzero residual.
        /// </summary>
        public static double WaterAndFoodResidualKilograms(double initialKilograms, LifeSupportSnapshot life, LifeSupportLedger ledger,
            BaseEquipmentSnapshot equipment, double externalInflowKilograms, double externalOutflowKilograms)
        {
            if (ledger == null) throw new ArgumentNullException(nameof(ledger));
            double tracked = WaterAndFoodKilograms(life, equipment);
            double predicted = initialKilograms - ledger.CrewPotableWaterIntakeKilograms - ledger.CrewFoodIntakeKilograms
                               + ledger.CrewUntreatedWaterReturnKilograms + externalInflowKilograms - externalOutflowKilograms
                               - equipment.Ledger.BrineOutflowKilograms;
            return tracked - predicted;
        }

        /// <summary>
        /// Each present member is charged exactly once per step: to the cabin (unsealed, phase 2 <c>CrewAboard</c>) or to
        /// one suit (sealed, phase 3 <see cref="EvaDemand"/>), and the beds load with the same aboard count as the cabin.
        /// Returns null when consistent, otherwise a diagnostic text.
        /// </summary>
        public static string CrewPartitionError(int presentMembers, OutpostDemand outpost, EvaDemand eva, EquipmentDemand equipment)
        {
            if (outpost == null) throw new ArgumentNullException(nameof(outpost));
            if (eva == null) throw new ArgumentNullException(nameof(eva));
            if (equipment == null) throw new ArgumentNullException(nameof(equipment));
            if (presentMembers < 0) throw new ArgumentOutOfRangeException(nameof(presentMembers));
            int charged = outpost.CrewAboard + eva.Suits.Count;
            if (charged != presentMembers)
                return $"{presentMembers} present members but {outpost.CrewAboard} aboard + {eva.Suits.Count} sealed = {charged} charged.";
            if (equipment.CrewAboard != outpost.CrewAboard)
                return $"Beds load for {equipment.CrewAboard} members but the cabin charges {outpost.CrewAboard}.";
            return null;
        }
    }
}
