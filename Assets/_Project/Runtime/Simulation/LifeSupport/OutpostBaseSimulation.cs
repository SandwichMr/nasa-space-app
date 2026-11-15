using System;
using System.Collections.Generic;
using Trainer.Simulation.Crew;

namespace Trainer.Simulation.LifeSupport
{
    /// <summary>
    /// Phase 2 base model (D1): electrical dispatch with a service channel, potable/untreated water, stored food and
    /// one well-mixed cabin with separate O2 and CO2. Plain C#, single-threaded, owned and called only by the session
    /// authority. Units: seconds, joules, watts, kilograms, kilopascals, cubic metres, kelvin.
    ///
    /// Who applies what (single application of every cost):
    /// <list type="bullet">
    /// <item>Continuous crew demand (O2, CO2, water, food) and all electrical energy: here, in <see cref="Advance"/>,
    /// from <see cref="OutpostDemand.CrewAboard"/>, <see cref="OutpostDemand.ServiceLoadWatts"/> and
    /// <see cref="OutpostDemand.ScienceRequested"/>. The caller never debits anything.</item>
    /// <item>Task material effects: here, in <see cref="Apply"/>, all or nothing, once per transfer id per Reset.</item>
    /// <item>Task progress, completion and when to call Apply: the task authority (D2), never this class.</item>
    /// </list>
    ///
    /// Within one Advance the demand is constant and every rate is piecewise constant; the interval is split at the
    /// exact instant of each event (battery empty/full, a stock empty/full, O2 store empty, cabin gas reaching its
    /// control target or zero), so results are independent of step size up to floating-point rounding. All work is
    /// done on a copy and committed at the end: invalid input throws without changing state.
    ///
    /// Not modelled: total pressure, humidity, temperature change, multiple zones, leaks, instrument failures,
    /// water processing (only through task effects), nutrients, waste, crops, any clinical consequence.
    /// </summary>
    public sealed class OutpostBaseSimulation : IOutpostSimulation
    {
        private const int MaxSegmentsPerAdvance = 64;

        private readonly OutpostSimulationParameters parameters;
        private readonly SimulationClock clock = new SimulationClock();
        private readonly Dictionary<Guid, ResourceEffect> appliedTransfers = new Dictionary<Guid, ResourceEffect>();

        private OutpostConfiguration configuration;
        private double oxygenTargetKilograms;
        private double carbonDioxideFloorKilograms;
        private ModelState state;
        private bool scienceRequested;
        private double serviceLoadWatts;
        private int crewAboard;
        private GasReading lastOxygenReading;
        private GasReading lastCarbonDioxideReading;
        private OutpostSimulationSnapshot snapshot;
        private LifeSupportLedger ledger;

        public OutpostBaseSimulation(OutpostSimulationParameters parameters)
        {
            this.parameters = parameters ?? throw new ArgumentNullException(nameof(parameters));
        }

        /// <summary>Model parameters given at construction (unchanged by Reset).</summary>
        public OutpostSimulationParameters Parameters => parameters;

        public OutpostSimulationSnapshot Snapshot
        {
            get
            {
                RequireReset();
                return snapshot;
            }
        }

        /// <summary>Diagnostic mass ledger since the last Reset. Requires Reset.</summary>
        public LifeSupportLedger Ledger
        {
            get
            {
                RequireReset();
                return ledger;
            }
        }

        /// <summary>Physical O2 mass in the cabin gas, kg (model truth, not an instrument reading).</summary>
        public double CabinOxygenKilograms
        {
            get
            {
                RequireReset();
                return state.CabinOxygen;
            }
        }

        /// <summary>Physical CO2 mass in the cabin gas, kg (model truth, not an instrument reading).</summary>
        public double CabinCarbonDioxideKilograms
        {
            get
            {
                RequireReset();
                return state.CabinCarbonDioxide;
            }
        }

        public double LastFullyServedServiceSeconds { get; private set; }

        public double LastFullyServedScienceSeconds { get; private set; }

        public void Reset(OutpostConfiguration configuration)
        {
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));
            CabinConfiguration cabin = configuration.LifeSupport.Cabin;
            WaterStock water = configuration.LifeSupport.InitialWater;

            this.configuration = configuration;
            clock.Reset();
            appliedTransfers.Clear();
            LastFullyServedServiceSeconds = 0d;
            LastFullyServedScienceSeconds = 0d;
            scienceRequested = false;
            serviceLoadWatts = 0d;
            crewAboard = 0;

            oxygenTargetKilograms = CabinGas.MassKilograms(
                cabin.InitialOxygenPartialPressureKilopascals, CabinGas.OxygenMolarMass, cabin);
            carbonDioxideFloorKilograms = CabinGas.MassKilograms(
                cabin.InitialCarbonDioxidePartialPressureKilopascals, CabinGas.CarbonDioxideMolarMass, cabin);

            state = new ModelState
            {
                Energy = configuration.Power.InitialEnergyJoules,
                Potable = water.PotableKilograms,
                Untreated = water.UntreatedKilograms,
                Food = configuration.LifeSupport.InitialFood.StoredKilograms,
                CabinOxygen = oxygenTargetKilograms,
                CabinCarbonDioxide = carbonDioxideFloorKilograms,
                OxygenStore = parameters.Air.OxygenStoreKilograms
            };
            lastOxygenReading = GasReading.Unavailable;
            lastCarbonDioxideReading = GasReading.Unavailable;
            Publish();
        }

        public void Advance(double elapsedSeconds, OutpostDemand demand)
        {
            RequireReset();
            if (demand == null) throw new ArgumentNullException(nameof(demand));
            SimulationClock.Validate(elapsedSeconds, clock.ElapsedSeconds);
            if (elapsedSeconds == 0d)
                return; // Contract: zero is a no-op, including the demand.

            ModelState work = state;
            double fullService = 0d, fullScience = 0d, remaining = elapsedSeconds;
            int segments = 0;
            while (remaining > 0d)
            {
                if (++segments > MaxSegmentsPerAdvance)
                    throw new InvalidOperationException("Life support step did not converge; state left unchanged.");
                OutpostPowerDispatch power = OutpostPowerDispatch.Evaluate(configuration.Power, work.Energy,
                    demand.ScienceRequested, demand.ServiceLoadWatts);
                Rates rates = ComputeRates(work, power, demand.CrewAboard);
                double dt = Math.Min(remaining, NextEvent(work, power, rates));
                if (power.ServiceFullyServed) fullService += dt;
                if (power.ScienceFullyServed) fullScience += dt;
                Integrate(ref work, power, rates, dt);
                remaining -= dt;
            }

            state = work;
            LastFullyServedServiceSeconds = fullService;
            LastFullyServedScienceSeconds = fullScience;
            scienceRequested = demand.ScienceRequested;
            serviceLoadWatts = demand.ServiceLoadWatts;
            crewAboard = demand.CrewAboard;
            clock.Advance(elapsedSeconds);
            Publish();
        }

        public double SecondsUntilServiceChange(OutpostDemand demand)
        {
            RequireReset();
            if (demand == null) throw new ArgumentNullException(nameof(demand));
            OutpostPowerDispatch power = OutpostPowerDispatch.Evaluate(configuration.Power, state.Energy,
                demand.ScienceRequested, demand.ServiceLoadWatts);
            if (!power.EssentialFullyServed) return 0d;
            return power.SecondsToBatteryBoundary(state.Energy, configuration.Power.CapacityJoules);
        }

        public ResourceTransferStatus Apply(ResourceTransfer transfer)
        {
            RequireReset();
            if (transfer == null) throw new ArgumentNullException(nameof(transfer));
            if (appliedTransfers.TryGetValue(transfer.Id, out ResourceEffect previous))
                return SameEffect(previous, transfer.Effect)
                    ? ResourceTransferStatus.AlreadyApplied
                    : ResourceTransferStatus.IdConflict;

            ResourceEffect effect = transfer.Effect;
            WaterStock water = configuration.LifeSupport.InitialWater;
            double potable = state.Potable + effect.DeltaFor(ResourceKind.PotableWater);
            double untreated = state.Untreated + effect.DeltaFor(ResourceKind.UntreatedWater);
            double food = state.Food + effect.DeltaFor(ResourceKind.StoredFood);
            if (potable < 0d || untreated < 0d || food < 0d)
                return ResourceTransferStatus.InsufficientStock;
            if (potable > water.PotableCapacityKilograms || untreated > water.UntreatedCapacityKilograms
                || food > configuration.LifeSupport.InitialFood.CapacityKilograms)
                return ResourceTransferStatus.ExceedsCapacity;

            state.Potable = potable;
            state.Untreated = untreated;
            state.Food = food;
            state.TransferBoundaryOutflow += effect.BoundaryOutflowKilograms;
            appliedTransfers.Add(transfer.Id, effect);
            Publish();
            return ResourceTransferStatus.Applied;
        }

        // ---------------------------------------------------------------- model

        private Rates ComputeRates(in ModelState s, in OutpostPowerDispatch power, int crew)
        {
            CrewMetabolicDemand perMember = parameters.Crew;
            AirRevitalizationParameters air = parameters.Air;
            double untreatedCapacity = configuration.LifeSupport.InitialWater.UntreatedCapacityKilograms;
            var r = new Rates();

            double waterDemand = crew * perMember.PotableWaterIntakeKilogramsPerSecond;
            r.Drink = s.Potable > 0d ? waterDemand : 0d;
            r.UnservedWater = waterDemand - r.Drink;
            double returned = r.Drink > 0d ? crew * perMember.UntreatedWaterReturnKilogramsPerSecond : 0d;
            r.ReturnStored = s.Untreated < untreatedCapacity ? returned : 0d;
            r.ReturnNotStored = returned - r.ReturnStored;

            double foodDemand = crew * perMember.FoodIntakeKilogramsPerSecond;
            r.Eat = s.Food > 0d ? foodDemand : 0d;
            r.UnservedFood = foodDemand - r.Eat;

            // Air services are part of the essential load: without full essential service both stop.
            bool airPowered = power.EssentialFullyServed;
            double oxygenDemand = crew * perMember.OxygenConsumptionKilogramsPerSecond;
            double supplyAvailable = airPowered && s.OxygenStore > 0d ? air.MaxOxygenSupplyKilogramsPerSecond : 0d;
            r.Supply = s.CabinOxygen < oxygenTargetKilograms
                ? supplyAvailable
                : Math.Min(supplyAvailable, oxygenDemand);
            // With no O2 left in the cabin, respiration is limited to what the supply delivers.
            double respirationFraction = s.CabinOxygen > 0d || oxygenDemand == 0d
                ? 1d
                : Math.Min(1d, r.Supply / oxygenDemand);
            r.Respire = respirationFraction * oxygenDemand;
            r.Exhale = respirationFraction * crew * perMember.CarbonDioxideProductionKilogramsPerSecond;

            double removalAvailable = airPowered ? air.MaxCarbonDioxideRemovalKilogramsPerSecond : 0d;
            r.Remove = s.CabinCarbonDioxide > carbonDioxideFloorKilograms
                ? removalAvailable
                : Math.Min(removalAvailable, r.Exhale);
            return r;
        }

        /// <summary>Seconds to the earliest event that changes a rate (positive or infinity).</summary>
        private double NextEvent(in ModelState s, in OutpostPowerDispatch power, in Rates r)
        {
            double untreatedCapacity = configuration.LifeSupport.InitialWater.UntreatedCapacityKilograms;
            double next = power.SecondsToBatteryBoundary(s.Energy, configuration.Power.CapacityJoules);
            next = Math.Min(next, TimeToReach(s.Potable, -r.Drink, 0d));
            next = Math.Min(next, TimeToReach(s.Food, -r.Eat, 0d));
            next = Math.Min(next, TimeToReach(s.Untreated, r.ReturnStored, untreatedCapacity));
            next = Math.Min(next, TimeToReach(s.OxygenStore, -r.Supply, 0d));
            double oxygenRate = r.Supply - r.Respire;
            next = Math.Min(next, s.CabinOxygen < oxygenTargetKilograms
                ? TimeToReach(s.CabinOxygen, oxygenRate, oxygenTargetKilograms)
                : double.PositiveInfinity);
            next = Math.Min(next, TimeToReach(s.CabinOxygen, oxygenRate, 0d));
            next = Math.Min(next, s.CabinCarbonDioxide > carbonDioxideFloorKilograms
                ? TimeToReach(s.CabinCarbonDioxide, r.Exhale - r.Remove, carbonDioxideFloorKilograms)
                : double.PositiveInfinity);
            return next;
        }

        private static double TimeToReach(double value, double rate, double boundary)
        {
            if (rate > 0d && boundary > value) return (boundary - value) / rate;
            if (rate < 0d && boundary < value) return (value - boundary) / -rate;
            return double.PositiveInfinity;
        }

        private void Integrate(ref ModelState s, in OutpostPowerDispatch power, in Rates r, double dt)
        {
            PowerConfiguration p = configuration.Power;
            double untreatedCapacity = configuration.LifeSupport.InitialWater.UntreatedCapacityKilograms;

            s.Energy = Step(s.Energy, power.BatteryWatts, dt, 0d, p.CapacityJoules);
            s.Potable = Step(s.Potable, -r.Drink, dt, 0d, double.PositiveInfinity);
            s.Food = Step(s.Food, -r.Eat, dt, 0d, double.PositiveInfinity);
            s.Untreated = Step(s.Untreated, r.ReturnStored, dt, 0d, untreatedCapacity);
            s.OxygenStore = Step(s.OxygenStore, -r.Supply, dt, 0d, double.PositiveInfinity);
            double oxygenCeiling = s.CabinOxygen <= oxygenTargetKilograms ? oxygenTargetKilograms : double.PositiveInfinity;
            s.CabinOxygen = Step(s.CabinOxygen, r.Supply - r.Respire, dt, 0d, oxygenCeiling);
            double carbonDioxideFloor = s.CabinCarbonDioxide >= carbonDioxideFloorKilograms ? carbonDioxideFloorKilograms : 0d;
            s.CabinCarbonDioxide = Step(s.CabinCarbonDioxide, r.Exhale - r.Remove, dt, carbonDioxideFloor, double.PositiveInfinity);

            s.OxygenSupplied += r.Supply * dt;
            s.CrewOxygen += r.Respire * dt;
            s.CrewCarbonDioxide += r.Exhale * dt;
            s.CarbonDioxideRemoved += r.Remove * dt;
            s.CrewWater += r.Drink * dt;
            s.CrewWaterReturn += r.ReturnStored * dt;
            s.ReturnNotStored += r.ReturnNotStored * dt;
            s.CrewFood += r.Eat * dt;
            s.UnservedWater += r.UnservedWater * dt;
            s.UnservedFood += r.UnservedFood * dt;
        }

        /// <summary>value + rate·dt kept inside [min, max]. When the boundary the rate moves toward is reached
        /// within dt (the event that ended the segment), the value lands exactly on it; otherwise the clamp only
        /// absorbs last-bit rounding.</summary>
        private static double Step(double value, double rate, double dt, double min, double max)
        {
            if (rate == 0d) return value;
            double boundary = rate > 0d ? max : min;
            if (TimeToReach(value, rate, boundary) <= dt) return boundary;
            double next = value + rate * dt;
            if (next < min) next = min;
            if (next > max) next = max;
            return next;
        }

        // ---------------------------------------------------------------- publication

        private void Publish()
        {
            PowerConfiguration p = configuration.Power;
            OutpostPowerDispatch power = OutpostPowerDispatch.Evaluate(p, state.Energy, scienceRequested, serviceLoadWatts);
            var powerSnapshot = new PowerSnapshot(clock.ElapsedSeconds, state.Energy, p.CapacityJoules,
                p.GenerationWatts, power.EssentialRequestedWatts, power.EssentialServedWatts,
                power.ScienceRequestedWatts, power.ScienceServedWatts);

            WaterStock w = configuration.LifeSupport.InitialWater;
            var water = new WaterStock(state.Potable, state.Untreated, w.PotableCapacityKilograms, w.UntreatedCapacityKilograms);
            var food = new FoodStock(state.Food, configuration.LifeSupport.InitialFood.CapacityKilograms);

            snapshot = new OutpostSimulationSnapshot(powerSnapshot, power.ServiceRequestedWatts,
                power.ServiceServedWatts, new LifeSupportSnapshot(water, food, ObserveCabin(power)));
            ledger = new LifeSupportLedger(state.OxygenStore, state.OxygenSupplied, state.CrewOxygen,
                state.CrewCarbonDioxide, state.CarbonDioxideRemoved, state.CrewWater, state.CrewWaterReturn,
                state.ReturnNotStored, state.CrewFood, state.TransferBoundaryOutflow, state.UnservedWater,
                state.UnservedFood);
        }

        /// <summary>Cabin instruments are on the essential load: Valid while it is fully served at the published
        /// operating point; otherwise the last valid value is shown as Stale, or Unavailable if none exists.
        /// A missing reading is never reported as zero.</summary>
        private CabinAirObservation ObserveCabin(in OutpostPowerDispatch power)
        {
            if (power.EssentialFullyServed)
            {
                CabinConfiguration cabin = configuration.LifeSupport.Cabin;
                double now = clock.ElapsedSeconds;
                lastOxygenReading = GasReading.Valid(
                    CabinGas.PartialPressureKilopascals(state.CabinOxygen, CabinGas.OxygenMolarMass, cabin), now);
                lastCarbonDioxideReading = GasReading.Valid(
                    CabinGas.PartialPressureKilopascals(state.CabinCarbonDioxide, CabinGas.CarbonDioxideMolarMass, cabin), now);
                return new CabinAirObservation(lastOxygenReading, lastCarbonDioxideReading);
            }
            return new CabinAirObservation(AsStale(lastOxygenReading), AsStale(lastCarbonDioxideReading));
        }

        private static GasReading AsStale(GasReading reading) =>
            reading.Status == ReadingStatus.Unavailable
                ? GasReading.Unavailable
                : new GasReading(ReadingStatus.Stale, reading.PartialPressureKilopascals, reading.ObservedAtSeconds);

        private static bool SameEffect(ResourceEffect a, ResourceEffect b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a.BoundaryOutflowKilograms != b.BoundaryOutflowKilograms || a.Changes.Count != b.Changes.Count)
                return false;
            foreach (ResourceChange change in a.Changes)
            {
                bool found = false;
                foreach (ResourceChange other in b.Changes)
                    if (other.Kind == change.Kind && other.DeltaKilograms == change.DeltaKilograms) { found = true; break; }
                if (!found) return false;
            }
            return true;
        }

        private void RequireReset()
        {
            if (configuration == null)
                throw new InvalidOperationException("Reset must be called before using the outpost simulation.");
        }

        /// <summary>Mutable model state (value type so a step can work on a copy and commit atomically).</summary>
        private struct ModelState
        {
            public double Energy, Potable, Untreated, Food, CabinOxygen, CabinCarbonDioxide, OxygenStore;
            public double OxygenSupplied, CrewOxygen, CrewCarbonDioxide, CarbonDioxideRemoved;
            public double CrewWater, CrewWaterReturn, ReturnNotStored, CrewFood, TransferBoundaryOutflow;
            public double UnservedWater, UnservedFood;
        }

        /// <summary>Constant rates of one segment, kg/s.</summary>
        private struct Rates
        {
            public double Drink, UnservedWater, ReturnStored, ReturnNotStored, Eat, UnservedFood;
            public double Supply, Respire, Exhale, Remove;
        }
    }
}
