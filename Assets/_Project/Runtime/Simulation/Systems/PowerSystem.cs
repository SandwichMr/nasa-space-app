using System;

namespace Trainer.Simulation
{
    /// <summary>
    /// Minimal electrical model implementing IPowerSimulation. Units: seconds, joules, watts.
    ///
    /// Within one Advance all configured powers are constant. Generation feeds the essential load
    /// first, then optional science; the battery covers any deficit up to MaxDischargeWatts while it
    /// holds energy, and absorbs surplus up to MaxChargeWatts while below capacity. Generation that
    /// cannot be used or stored is curtailed (not stored, not reported as served). Charge and
    /// discharge are lossless in this model.
    ///
    /// When the battery reaches empty or full inside the interval, the interval is split at that
    /// exact instant and the operating point is recomputed, so stepping is exact (up to floating-point
    /// rounding) and independent of step size. Snapshot power values describe the operating point
    /// at SimulatedTimeSeconds given the current energy and the latest science request.
    /// </summary>
    public sealed class PowerSystem : IPowerSimulation
    {
        private readonly SimulationClock clock = new SimulationClock();
        private PowerState state;
        private PowerSnapshot snapshot;

        public PowerSnapshot Snapshot
        {
            get
            {
                RequireReset();
                return snapshot;
            }
        }

        public void Reset(PowerConfiguration configuration)
        {
            if (configuration == null)
                throw new ArgumentNullException(nameof(configuration));
            clock.Reset();
            state = new PowerState(configuration);
            snapshot = BuildSnapshot();
        }

        public void Advance(double elapsedSeconds, bool scienceRequested)
        {
            RequireReset();
            SimulationClock.Validate(elapsedSeconds, clock.ElapsedSeconds);
            if (elapsedSeconds == 0d)
                return; // Contract: zero is a no-op, including the science request.

            state.ScienceRequested = scienceRequested;
            double capacity = state.Configuration.CapacityJoules;
            double remaining = elapsedSeconds;

            // At most two segments occur (e.g. discharge to empty, then steady), loop is bounded.
            while (remaining > 0d)
            {
                OperatingPoint point = Evaluate();
                double rate = point.BatteryWatts;
                if (rate == 0d)
                    break;

                double secondsToBoundary = rate > 0d
                    ? (capacity - state.EnergyJoules) / rate
                    : state.EnergyJoules / -rate;

                if (secondsToBoundary <= remaining)
                {
                    // Event inside (or at the end of) the interval: land exactly on the boundary.
                    state.EnergyJoules = rate > 0d ? capacity : 0d;
                    remaining -= secondsToBoundary;
                }
                else
                {
                    double next = state.EnergyJoules + rate * remaining;
                    // remaining < secondsToBoundary, so mathematically next stays inside [0, capacity];
                    // only last-bit rounding could cross it, which would be the boundary itself.
                    if (next > capacity) next = capacity;
                    if (next < 0d) next = 0d;
                    state.EnergyJoules = next;
                    remaining = 0d;
                }
            }

            clock.Advance(elapsedSeconds);
            snapshot = BuildSnapshot();
        }

        private void RequireReset()
        {
            if (state == null)
                throw new InvalidOperationException("Reset must be called before using the power simulation.");
        }

        private PowerSnapshot BuildSnapshot()
        {
            PowerConfiguration config = state.Configuration;
            OperatingPoint point = Evaluate();
            return new PowerSnapshot(
                clock.ElapsedSeconds,
                state.EnergyJoules,
                config.CapacityJoules,
                config.GenerationWatts,
                config.EssentialConsumptionWatts,
                point.EssentialServedWatts,
                point.ScienceRequestedWatts,
                point.ScienceServedWatts);
        }

        /// <summary>Instantaneous dispatch for the current energy and science request.</summary>
        private OperatingPoint Evaluate()
        {
            PowerConfiguration config = state.Configuration;
            double generation = config.GenerationWatts;
            double essentialDemand = config.EssentialConsumptionWatts;
            double scienceDemand = state.ScienceRequested ? config.ScienceConsumptionWatts : 0d;

            double dischargeLimit = state.EnergyJoules > 0d ? config.MaxDischargeWatts : 0d;
            double chargeLimit = state.EnergyJoules < config.CapacityJoules ? config.MaxChargeWatts : 0d;

            double available = generation + dischargeLimit;
            double essentialServed = Math.Min(essentialDemand, available);
            double scienceServed = Math.Min(scienceDemand, available - essentialServed);

            double net = generation - essentialServed - scienceServed;
            double batteryWatts = net > 0d ? Math.Min(net, chargeLimit) : net;

            return new OperatingPoint(essentialServed, scienceDemand, scienceServed, batteryWatts);
        }

        private readonly struct OperatingPoint
        {
            public readonly double EssentialServedWatts;
            public readonly double ScienceRequestedWatts;
            public readonly double ScienceServedWatts;
            /// <summary>Positive charges the battery, negative discharges it.</summary>
            public readonly double BatteryWatts;

            public OperatingPoint(double essentialServed, double scienceRequested,
                double scienceServed, double batteryWatts)
            {
                EssentialServedWatts = essentialServed;
                ScienceRequestedWatts = scienceRequested;
                ScienceServedWatts = scienceServed;
                BatteryWatts = batteryWatts;
            }
        }
    }
}
