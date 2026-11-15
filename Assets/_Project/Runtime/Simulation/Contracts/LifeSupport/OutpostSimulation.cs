using System;

namespace Trainer.Simulation.LifeSupport
{
    /// <summary>Initial stocks/capacities and cabin declaration of a scenario (configuration, immutable).</summary>
    public sealed class LifeSupportConfiguration
    {
        public WaterStock InitialWater { get; }
        public FoodStock InitialFood { get; }
        public CabinConfiguration Cabin { get; }

        public LifeSupportConfiguration(WaterStock initialWater, FoodStock initialFood, CabinConfiguration cabin)
        {
            InitialWater = initialWater ?? throw new ArgumentNullException(nameof(initialWater));
            InitialFood = initialFood ?? throw new ArgumentNullException(nameof(initialFood));
            Cabin = cabin ?? throw new ArgumentNullException(nameof(cabin));
        }
    }

    /// <summary>
    /// Everything <see cref="IOutpostSimulation.Reset"/> needs. <see cref="Power"/> is the phase 1 type with
    /// its phase 1 meaning (essential and science channels, battery); phase 2 service loads are declared per
    /// step through <see cref="OutpostDemand.ServiceLoadWatts"/>, never folded into those fields.
    /// </summary>
    public sealed class OutpostConfiguration
    {
        public PowerConfiguration Power { get; }
        public LifeSupportConfiguration LifeSupport { get; }

        public OutpostConfiguration(PowerConfiguration power, LifeSupportConfiguration lifeSupport)
        {
            Power = power ?? throw new ArgumentNullException(nameof(power));
            LifeSupport = lifeSupport ?? throw new ArgumentNullException(nameof(lifeSupport));
        }
    }

    /// <summary>
    /// Demand held constant during one <see cref="IOutpostSimulation.Advance"/> call, declared by the session
    /// authority (the only caller). The simulation turns it into consumption; the caller never debits stocks.
    /// </summary>
    public sealed class OutpostDemand
    {
        /// <summary>Crew members inside the modelled cabin (present players). Their continuous demand is applied
        /// only by the simulation, from its own documented model.</summary>
        public int CrewAboard { get; }
        /// <summary>Phase 1 science channel (PowerConfiguration.ScienceConsumptionWatts), requested while a
        /// Science task is in progress.</summary>
        public bool ScienceRequested { get; }
        /// <summary>Sum of the power of the Support tasks in progress, watts (finite, nonnegative).</summary>
        public double ServiceLoadWatts { get; }

        public OutpostDemand(int crewAboard, bool scienceRequested, double serviceLoadWatts)
        {
            if (crewAboard < 0) throw new ArgumentOutOfRangeException(nameof(crewAboard));
            CrewAboard = crewAboard;
            ScienceRequested = scienceRequested;
            ServiceLoadWatts = LifeSupportGuard.NonNegative(serviceLoadWatts, nameof(serviceLoadWatts));
        }
    }

    /// <summary>Life support part of a published observation (immutable): ledger stocks and cabin readings.</summary>
    public sealed class LifeSupportSnapshot
    {
        public WaterStock Water { get; }
        public FoodStock Food { get; }
        public CabinAirObservation Cabin { get; }

        public LifeSupportSnapshot(WaterStock water, FoodStock food, CabinAirObservation cabin)
        {
            Water = water ?? throw new ArgumentNullException(nameof(water));
            Food = food ?? throw new ArgumentNullException(nameof(food));
            Cabin = cabin ?? throw new ArgumentNullException(nameof(cabin));
        }
    }

    /// <summary>
    /// Immutable observation of the whole base model at <c>Power.SimulatedTimeSeconds</c>.
    /// <see cref="Power"/> keeps the phase 1 meaning. Service load watts are instantaneous, like phase 1 power
    /// values (last step's operating point); served never exceeds requested.
    /// </summary>
    public sealed class OutpostSimulationSnapshot
    {
        public PowerSnapshot Power { get; }
        public double ServiceLoadRequestedWatts { get; }
        public double ServiceLoadServedWatts { get; }
        public LifeSupportSnapshot LifeSupport { get; }

        public OutpostSimulationSnapshot(PowerSnapshot power, double serviceLoadRequestedWatts,
            double serviceLoadServedWatts, LifeSupportSnapshot lifeSupport)
        {
            Power = power ?? throw new ArgumentNullException(nameof(power));
            ServiceLoadRequestedWatts = LifeSupportGuard.NonNegative(serviceLoadRequestedWatts, nameof(serviceLoadRequestedWatts));
            ServiceLoadServedWatts = LifeSupportGuard.NonNegative(serviceLoadServedWatts, nameof(serviceLoadServedWatts));
            if (serviceLoadServedWatts > serviceLoadRequestedWatts)
                throw new ArgumentException("Served service load exceeds the request.", nameof(serviceLoadServedWatts));
            LifeSupport = lifeSupport ?? throw new ArgumentNullException(nameof(lifeSupport));
        }
    }

    /// <summary>
    /// Phase 2 base model (power + water + food + cabin). Implemented by D1; called ONLY by the session
    /// authority, which owns the single instance. Plain C#, single-threaded, no UnityEngine. Phase 1
    /// <see cref="IPowerSimulation"/> and its implementations are not changed by this contract.
    ///
    /// Cost ownership: continuous crew demand and electrical energy are applied inside <see cref="Advance"/>;
    /// task material effects are applied inside <see cref="Apply"/>, once per transfer id. Nothing else in
    /// the game debits or credits these stocks.
    /// </summary>
    public interface IOutpostSimulation
    {
        /// <summary>Initializes or restores time zero, initial stocks and the cabin declaration. Clears the
        /// record of applied transfer ids. Throws ArgumentNullException for null.</summary>
        void Reset(OutpostConfiguration configuration);

        /// <summary>Requires Reset; immutable observation.</summary>
        OutpostSimulationSnapshot Snapshot { get; }

        /// <summary>Requires Reset and finite, nonnegative seconds (zero is a no-op that changes nothing).
        /// Applies demand, energy and continuous consumption for the interval. Invalid input throws without
        /// changing state.</summary>
        void Advance(double elapsedSeconds, OutpostDemand demand);

        /// <summary>Seconds of the last nonzero Advance during which the requested service load was fully
        /// served (the elapsed time when no service load was requested). Zero after Reset.</summary>
        double LastFullyServedServiceSeconds { get; }

        /// <summary>Seconds of the last nonzero Advance with the science channel fully served, as phase 1
        /// <see cref="IIntegratedPowerSimulation.LastFullyServedScienceSeconds"/>. Zero after Reset.</summary>
        double LastFullyServedScienceSeconds { get; }

        /// <summary>Seconds until the dispatch or service of <paramref name="demand"/> changes; infinity when
        /// steady, zero when essential service is already insufficient. Does not mutate the model.</summary>
        double SecondsUntilServiceChange(OutpostDemand demand);

        /// <summary>Applies <paramref name="transfer"/> atomically, or nothing; see <see cref="ResourceTransferStatus"/>.
        /// Requires Reset. Never throws for insufficient stock or capacity.</summary>
        ResourceTransferStatus Apply(ResourceTransfer transfer);
    }
}
