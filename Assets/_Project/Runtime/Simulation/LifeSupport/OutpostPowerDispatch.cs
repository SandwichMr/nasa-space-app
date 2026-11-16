using System;

namespace Trainer.Simulation.LifeSupport
{
    /// <summary>
    /// Instantaneous electrical dispatch of the phase 2 base: the phase 1 rules of <see cref="PowerSystem"/>
    /// (generation first, battery covers deficits up to MaxDischargeWatts while it holds energy, absorbs surplus up
    /// to MaxChargeWatts while below capacity, the rest is curtailed, lossless) with one extra channel.
    ///
    /// Priority (documented by D1): 1) essential load (includes cabin air services and instruments),
    /// 2) phase 2 service load (Support tasks in progress), 3) phase 1 science channel. Each channel is served
    /// up to what is left; a channel served below its request is "partial". With no service load the result is
    /// identical to <see cref="PowerSystem"/>.
    /// </summary>
    internal readonly struct OutpostPowerDispatch
    {
        public readonly double EssentialRequestedWatts;
        public readonly double EssentialServedWatts;
        public readonly double ServiceRequestedWatts;
        public readonly double ServiceServedWatts;
        public readonly double ScienceRequestedWatts;
        public readonly double ScienceServedWatts;
        /// <summary>Positive charges the battery, negative discharges it.</summary>
        public readonly double BatteryWatts;

        private OutpostPowerDispatch(double essentialRequested, double essentialServed, double serviceRequested,
            double serviceServed, double scienceRequested, double scienceServed, double batteryWatts)
        {
            EssentialRequestedWatts = essentialRequested;
            EssentialServedWatts = essentialServed;
            ServiceRequestedWatts = serviceRequested;
            ServiceServedWatts = serviceServed;
            ScienceRequestedWatts = scienceRequested;
            ScienceServedWatts = scienceServed;
            BatteryWatts = batteryWatts;
        }

        public bool EssentialFullyServed => EssentialServedWatts >= EssentialRequestedWatts;
        /// <summary>True when no service load is requested, as the contract counts elapsed time then.</summary>
        public bool ServiceFullyServed => ServiceServedWatts >= ServiceRequestedWatts;
        public bool ScienceFullyServed => ScienceRequestedWatts > 0d && ScienceServedWatts >= ScienceRequestedWatts;

        public static OutpostPowerDispatch Evaluate(PowerConfiguration config, double energyJoules,
            bool scienceRequested, double serviceLoadWatts) =>
            Evaluate(config, energyJoules, scienceRequested, serviceLoadWatts, null);

        /// <summary>Phase 5 (D1): the same dispatch with the effective capacity of <paramref name="capacity"/> (generation
        /// and discharge limit multiplied by their factors, added essential load). Null or nominal reproduces phase 4
        /// exactly (multiplying by 1 and adding 0 are exact in IEEE 754).</summary>
        public static OutpostPowerDispatch Evaluate(PowerConfiguration config, double energyJoules,
            bool scienceRequested, double serviceLoadWatts, Trainer.Simulation.Incidents.PowerCapacityState capacity)
        {
            double generation = EffectiveGeneration(config, capacity);
            double essentialDemand = config.EssentialConsumptionWatts + (capacity == null ? 0d : capacity.AddedEssentialWatts);
            double scienceDemand = scienceRequested ? config.ScienceConsumptionWatts : 0d;

            double maxDischarge = config.MaxDischargeWatts * (capacity == null ? 1d : capacity.DischargeFactor);
            double dischargeLimit = energyJoules > 0d ? maxDischarge : 0d;
            double chargeLimit = energyJoules < config.CapacityJoules ? config.MaxChargeWatts : 0d;

            double available = generation + dischargeLimit;
            double essentialServed = Math.Min(essentialDemand, available);
            double serviceServed = Math.Min(serviceLoadWatts, available - essentialServed);
            double scienceServed = Math.Min(scienceDemand, available - essentialServed - serviceServed);

            double net = generation - essentialServed - serviceServed - scienceServed;
            double batteryWatts = net > 0d ? Math.Min(net, chargeLimit) : net;

            return new OutpostPowerDispatch(essentialDemand, essentialServed, serviceLoadWatts, serviceServed,
                scienceDemand, scienceServed, batteryWatts);
        }

        /// <summary>Generation in force: configured generation × generation factor.</summary>
        public static double EffectiveGeneration(PowerConfiguration config, Trainer.Simulation.Incidents.PowerCapacityState capacity) =>
            config.GenerationWatts * (capacity == null ? 1d : capacity.GenerationFactor);

        /// <summary>Seconds until the battery reaches empty or full at this dispatch; infinity when steady.</summary>
        public double SecondsToBatteryBoundary(double energyJoules, double capacityJoules)
        {
            if (BatteryWatts > 0d) return (capacityJoules - energyJoules) / BatteryWatts;
            if (BatteryWatts < 0d) return energyJoules / -BatteryWatts;
            return double.PositiveInfinity;
        }
    }
}
