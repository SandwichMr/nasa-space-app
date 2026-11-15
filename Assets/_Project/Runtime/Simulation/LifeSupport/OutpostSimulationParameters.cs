using System;
using Trainer.Simulation.Crew;

namespace Trainer.Simulation.LifeSupport
{
    /// <summary>
    /// Model parameters of <see cref="OutpostBaseSimulation"/> that the frozen <see cref="OutpostConfiguration"/>
    /// does not carry: per-member crew demand and the air services (immutable). Passed once to the constructor
    /// by the composer (D2); <see cref="IOutpostSimulation.Reset"/> keeps them.
    /// </summary>
    public sealed class OutpostSimulationParameters
    {
        /// <summary>Label shared by every value of <see cref="SyntheticTraining"/>.</summary>
        public const string SyntheticProvenance =
            "SINTÉTICO · valores ilustrativos de desarrollo y pruebas, no verificados contra BVAD ni otra fuente; " +
            "no son datos de la NASA, de hardware ni tasas reutilizables.";

        public CrewMetabolicDemand Crew { get; }
        public AirRevitalizationParameters Air { get; }

        public OutpostSimulationParameters(CrewMetabolicDemand crew, AirRevitalizationParameters air)
        {
            Crew = crew ?? throw new ArgumentNullException(nameof(crew));
            Air = air ?? throw new ArgumentNullException(nameof(air));
        }

        /// <summary>
        /// SYNTHETIC set for development, fixtures and the first integration. Per crew member and day: 0.8 kg O2,
        /// 1.0 kg CO2, 2.5 kg potable water, 1.5 kg food, 1.5 kg water returned as untreated. Air: 20 kg O2 store,
        /// supply up to 6.4 kg/day, CO2 removal up to 8 kg/day. Chosen only to be plausible in order of magnitude;
        /// replace with a retrieved, contextualized source before presenting them as evidence.
        /// </summary>
        public static OutpostSimulationParameters SyntheticTraining() =>
            new OutpostSimulationParameters(
                CrewMetabolicDemand.FromPerCrewDay(0.8, 1.0, 2.5, 1.5, 1.5, SyntheticProvenance),
                new AirRevitalizationParameters(20d, 6.4 / CrewMetabolicDemand.SecondsPerDay,
                    8.0 / CrewMetabolicDemand.SecondsPerDay, SyntheticProvenance));
    }
}
