using System;
using System.Collections.Generic;
using Trainer.Application.Outpost;
using Trainer.Simulation;
using Trainer.Simulation.LifeSupport;

namespace Trainer.Application.Development
{
    /// <summary>
    /// DEVELOPMENT ONLY. Synthetic phase 2 scenario and snapshot builders for the fake, presentation work and
    /// contract tests. Every number is an illustrative fixture (see <see cref="Provenance"/>): not NASA data,
    /// not hardware, not a rate, efficiency or limit to reuse. The builders assemble states the fixture
    /// chooses; they do not simulate, move crew or decide task results.
    /// </summary>
    public static class OutpostFixtures
    {
        public const string ScenarioId = "fixture-two-rooms";
        public const string Provenance =
            "Valores sintéticos ilustrativos para desarrollo y pruebas. No representan hardware, datos ni requisitos de la NASA.";

        public static readonly StationId WaterProcessor = new StationId("water-processor");
        public static readonly StationId ScienceBench = new StationId("science-bench");
        public static readonly StationId AirMonitor = new StationId("air-monitor");
        public static readonly TaskTypeId TreatWater = new TaskTypeId("treat-water");
        public static readonly TaskTypeId RunExperiment = new TaskTypeId("run-experiment");
        public static readonly TaskTypeId CheckCabinAir = new TaskTypeId("check-cabin-air");

        /// <summary>Two rooms of 8 x 10 m joined by a 2 m door at x = 8 m; four spawn points in the left room.</summary>
        public static OutpostScenario Scenario()
        {
            var layout = new OutpostLayout(
                new BaseRect(0, 0, 16, 10),
                new[] { new BaseRect(7.8, 0, 8.2, 4), new BaseRect(7.8, 6, 8.2, 10) },
                new[]
                {
                    new StationPlacement(WaterProcessor, new BasePosition(3, 8), 1.2),
                    new StationPlacement(ScienceBench, new BasePosition(13, 8), 1.2),
                    new StationPlacement(AirMonitor, new BasePosition(13, 2), 1.2)
                },
                new[] { new BasePosition(3, 3), new BasePosition(4.5, 3), new BasePosition(3, 4.5), new BasePosition(4.5, 4.5) });
            // Synthetic effect: 2 kg untreated in, 1.8 kg potable out, 0.2 kg residue leaves the boundary.
            var treatWater = new ResourceEffect(new[]
            {
                new ResourceChange(ResourceKind.UntreatedWater, -2.0),
                new ResourceChange(ResourceKind.PotableWater, 1.8)
            }, 0.2);
            var tasks = new[]
            {
                new TaskDefinition(TreatWater, WaterProcessor, TaskCategory.Support, 20, 150, treatWater),
                new TaskDefinition(RunExperiment, ScienceBench, TaskCategory.Science, 30, 0, ResourceEffect.None),
                new TaskDefinition(CheckCabinAir, AirMonitor, TaskCategory.Support, 8, 0, ResourceEffect.None)
            };
            var simulation = new OutpostConfiguration(
                new PowerConfiguration(2400, 6000, 100, 60, 80, 100, 200),
                new LifeSupportConfiguration(new WaterStock(20, 10, 40, 20), new FoodStock(15, 30),
                    new CabinConfiguration(40, 295, 21.0, 0.5)));
            return new OutpostScenario(ScenarioId, Provenance, layout, new CrewMovementSettings(2.0, 0.35), tasks, simulation);
        }

        /// <summary>Readings derived from the configuration at time zero (no simulation): initial stocks, battery
        /// at its initial energy, essential load served, no science or service load, cabin readings valid.</summary>
        public static OutpostSimulationSnapshot InitialResources(OutpostScenario scenario)
        {
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            PowerConfiguration p = scenario.Simulation.Power;
            LifeSupportConfiguration life = scenario.Simulation.LifeSupport;
            var power = new PowerSnapshot(0, p.InitialEnergyJoules, p.CapacityJoules, p.GenerationWatts,
                p.EssentialConsumptionWatts, p.EssentialConsumptionWatts, 0, 0);
            var cabin = new CabinAirObservation(
                GasReading.Valid(life.Cabin.InitialOxygenPartialPressureKilopascals, 0),
                GasReading.Valid(life.Cabin.InitialCarbonDioxidePartialPressureKilopascals, 0));
            return new OutpostSimulationSnapshot(power, 0, 0, new LifeSupportSnapshot(life.InitialWater, life.InitialFood, cabin));
        }

        /// <summary>Example objectives: one required (treat water twice) and one optional (science seconds).</summary>
        public static IReadOnlyList<ObjectiveProgress> ExampleObjectives(double treated = 0, double scienceSeconds = 0) =>
            new[]
            {
                new ObjectiveProgress("treat-water", treated, 2, ProgressUnit.Count, true),
                new ObjectiveProgress("science", scienceSeconds, 30, ProgressUnit.Seconds, false)
            };

        /// <summary>Ready at revision 0 of <paramref name="epoch"/>, with <paramref name="players"/> idle at the
        /// spawn points of slots 0, 1, ... and every station free.</summary>
        public static OutpostSessionSnapshot Initial(OutpostScenario scenario, long epoch, params PlayerId[] players)
        {
            if (scenario == null) throw new ArgumentNullException(nameof(scenario));
            if (players == null) throw new ArgumentNullException(nameof(players));
            var crew = new List<CrewMemberState>();
            for (int slot = 0; slot < players.Length; slot++)
                crew.Add(new CrewMemberState(players[slot], slot, CrewPose.IdleAt(scenario.Layout.SpawnPoints[slot]), 0));
            var stations = new List<StationState>();
            foreach (StationPlacement placement in scenario.Layout.Stations) stations.Add(StationState.Free(placement.Id));
            return new OutpostSessionSnapshot(epoch, 0, SessionState.Ready, OutpostOutcome.InProgress, scenario.Id,
                crew, stations, InitialResources(scenario), ExampleObjectives());
        }

        /// <summary>Copy of <paramref name="previous"/> with Revision + 1 and the given parts replaced. Pass
        /// <paramref name="newEpoch"/> to start an epoch at revision 0 instead.</summary>
        public static OutpostSessionSnapshot Next(OutpostSessionSnapshot previous,
            IEnumerable<CrewMemberState> crew = null, IEnumerable<StationState> stations = null,
            SessionState? state = null, OutpostOutcome? outcome = null, OutpostSimulationSnapshot resources = null,
            IEnumerable<ObjectiveProgress> objectives = null, long? newEpoch = null)
        {
            if (previous == null) throw new ArgumentNullException(nameof(previous));
            long epoch = newEpoch ?? previous.Epoch;
            long revision = newEpoch.HasValue ? 0 : checked(previous.Revision + 1);
            return new OutpostSessionSnapshot(epoch, revision, state ?? previous.State, outcome ?? previous.Outcome,
                previous.ScenarioId, crew ?? previous.Crew, stations ?? previous.Stations,
                resources ?? previous.Resources, objectives ?? previous.Objectives);
        }

        /// <summary>Copy of <paramref name="previous"/> crew with one member replaced (same slot).</summary>
        public static IReadOnlyList<CrewMemberState> WithMember(OutpostSessionSnapshot previous, PlayerId player,
            CrewPose pose, long lastInputSequence)
        {
            if (previous == null) throw new ArgumentNullException(nameof(previous));
            var crew = new List<CrewMemberState>();
            bool found = false;
            foreach (CrewMemberState member in previous.Crew)
            {
                if (member.Player == player)
                {
                    crew.Add(new CrewMemberState(player, member.Slot, pose, lastInputSequence));
                    found = true;
                }
                else crew.Add(member);
            }
            if (!found) throw new ArgumentException($"{player} is not in the snapshot.", nameof(player));
            return crew;
        }

        /// <summary>Copy of <paramref name="previous"/> stations with one station replaced.</summary>
        public static IReadOnlyList<StationState> WithStation(OutpostSessionSnapshot previous, StationState station)
        {
            if (previous == null) throw new ArgumentNullException(nameof(previous));
            if (station == null) throw new ArgumentNullException(nameof(station));
            var stations = new List<StationState>();
            bool found = false;
            foreach (StationState current in previous.Stations)
            {
                bool match = current.Station == station.Station;
                stations.Add(match ? station : current);
                found |= match;
            }
            if (!found) throw new ArgumentException($"Station '{station.Station}' is not in the snapshot.", nameof(station));
            return stations;
        }

        /// <summary>Copy of <paramref name="previous"/> crew without <paramref name="player"/>.</summary>
        public static IReadOnlyList<CrewMemberState> WithoutMember(OutpostSessionSnapshot previous, PlayerId player)
        {
            if (previous == null) throw new ArgumentNullException(nameof(previous));
            var crew = new List<CrewMemberState>();
            foreach (CrewMemberState member in previous.Crew)
                if (member.Player != player) crew.Add(member);
            return crew;
        }
    }
}
