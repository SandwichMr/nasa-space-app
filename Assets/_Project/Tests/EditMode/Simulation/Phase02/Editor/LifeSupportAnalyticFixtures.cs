using System;
using Trainer.Simulation;
using Trainer.Simulation.Crew;
using Trainer.Simulation.LifeSupport;

namespace Trainer.Tests.Simulation.Phase02
{
    /// <summary>
    /// SYNTHETIC, round values chosen so expected results are closed-form (hand-checkable). Not NASA data, not
    /// hardware, not rates to reuse. Units: s, J, W, kg, kPa, m³, K.
    /// Per member: O2 1 g/s, CO2 1.2 g/s, potable 2 g/s, food 1 g/s, untreated return 1.5 g/s.
    /// Air: 1 kg O2 store, supply up to 3 g/s, CO2 removal up to 4 g/s.
    /// </summary>
    internal static class LifeSupportAnalyticFixtures
    {
        public const string Label = "SINTÉTICO · fixture analítica de pruebas D1";
        public const double Exact = 1e-9;

        public const double O2PerMember = 0.001;
        public const double Co2PerMember = 0.0012;
        public const double WaterPerMember = 0.002;
        public const double FoodPerMember = 0.001;
        public const double ReturnPerMember = 0.0015;

        public static CrewMetabolicDemand Crew(double o2 = O2PerMember, double co2 = Co2PerMember,
            double water = WaterPerMember, double food = FoodPerMember, double returned = ReturnPerMember) =>
            new CrewMetabolicDemand(o2, co2, water, food, returned, Label);

        public static AirRevitalizationParameters Air(double store = 1.0, double maxSupply = 0.003,
            double maxRemoval = 0.004) => new AirRevitalizationParameters(store, maxSupply, maxRemoval, Label);

        public static OutpostSimulationParameters Parameters(CrewMetabolicDemand crew = null,
            AirRevitalizationParameters air = null) => new OutpostSimulationParameters(crew ?? Crew(), air ?? Air());

        /// <summary>Ample steady power: 1 kW generation, 100 W essential, 200 W science, 10 kJ battery half full.</summary>
        public static PowerConfiguration AmplePower() => new PowerConfiguration(5000, 10000, 1000, 100, 200, 1000, 1000);

        public static CabinConfiguration Cabin(double o2Kpa = 21.0, double co2Kpa = 0.5) =>
            new CabinConfiguration(40, 295, o2Kpa, co2Kpa);

        public static OutpostConfiguration Config(PowerConfiguration power = null, WaterStock water = null,
            FoodStock food = null, CabinConfiguration cabin = null) =>
            new OutpostConfiguration(power ?? AmplePower(),
                new LifeSupportConfiguration(water ?? new WaterStock(20, 5, 40, 20), food ?? new FoodStock(15, 30),
                    cabin ?? Cabin()));

        public static OutpostBaseSimulation Started(OutpostConfiguration config = null,
            OutpostSimulationParameters parameters = null)
        {
            var simulation = new OutpostBaseSimulation(parameters ?? Parameters());
            simulation.Reset(config ?? Config());
            return simulation;
        }

        public static OutpostDemand Demand(int aboard, bool science = false, double serviceWatts = 0d) =>
            new OutpostDemand(aboard, science, serviceWatts);

        /// <summary>Synthetic task effect shaped like the common fixture: 2 kg untreated in, 1.8 kg potable out,
        /// 0.2 kg residue leaves the boundary.</summary>
        public static ResourceEffect TreatWater() => new ResourceEffect(new[]
        {
            new ResourceChange(ResourceKind.UntreatedWater, -2.0),
            new ResourceChange(ResourceKind.PotableWater, 1.8)
        }, 0.2);

        /// <summary>Total tracked mass: water, food, cabin O2/CO2 and O2 store.</summary>
        public static double TrackedMass(OutpostBaseSimulation s)
        {
            LifeSupportSnapshot life = s.Snapshot.LifeSupport;
            return life.Water.PotableKilograms + life.Water.UntreatedKilograms + life.Food.StoredKilograms
                   + s.CabinOxygenKilograms + s.CabinCarbonDioxideKilograms + s.Ledger.OxygenStoreKilograms;
        }

        /// <summary>Tracked mass predicted from the initial total and the ledger's boundary flows.</summary>
        public static double PredictedTrackedMass(double initialTracked, LifeSupportLedger l) =>
            initialTracked - l.CrewOxygenIntakeKilograms - l.CrewPotableWaterIntakeKilograms - l.CrewFoodIntakeKilograms
            + l.CrewCarbonDioxideReturnKilograms + l.CrewUntreatedWaterReturnKilograms
            - l.CarbonDioxideRemovedKilograms - l.TransferBoundaryOutflowKilograms;

        public static double O2Mass(double kpa, CabinConfiguration cabin) =>
            kpa * 1000d * cabin.FreeGasVolumeCubicMeters * 0.0319988 / (8.314462618 * cabin.GasTemperatureKelvin);

        public static double Co2Mass(double kpa, CabinConfiguration cabin) =>
            kpa * 1000d * cabin.FreeGasVolumeCubicMeters * 0.0440095 / (8.314462618 * cabin.GasTemperatureKelvin);
    }
}
