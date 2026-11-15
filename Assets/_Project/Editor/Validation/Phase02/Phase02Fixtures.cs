using System;
using Trainer.Application.Outpost;
using Trainer.Simulation;
using Trainer.Simulation.LifeSupport;

namespace Trainer.Tooling.Validation.Phase02
{
    /// <summary>
    /// D5's own synthetic scenario for conformance runs, independent from the common-base fixture and from
    /// D2's scenario data. Values are illustrative: not NASA data, hardware figures or reusable rates.
    ///
    /// Plan (metres, top-down): bounds 12 x 8; a thin wall x∈[5.9, 6.1], y∈[0, 5] leaves a 3 m door above it.
    /// Spawn 0 (2, 3) is within reach of dock-a (2, 2); spawn 1 (4, 3) of dock-b (4, 2). far-lab (10, 2) is
    /// behind the wall: reachable only through the door. Two spawn points, so MaxCrew = 2 and a third Join
    /// must be rejected with SessionFull.
    /// </summary>
    public static class Phase02Fixtures
    {
        public const string ScenarioId = "d5-conformance";
        public const string Provenance =
            "D5 · valores sintéticos para pruebas de conformidad. No son datos de la NASA ni tasas reutilizables.";

        public static readonly StationId DockA = new StationId("dock-a");
        public static readonly StationId DockB = new StationId("dock-b");
        public static readonly StationId FarLab = new StationId("far-lab");
        public static readonly StationId Missing = new StationId("no-such-station");

        public static readonly TaskTypeId FixA = new TaskTypeId("fix-a");
        public static readonly TaskTypeId FixB = new TaskTypeId("fix-b");
        public static readonly TaskTypeId LabRun = new TaskTypeId("lab-run");
        public static readonly TaskTypeId Overdraw = new TaskTypeId("overdraw");
        public static readonly TaskTypeId MissingTask = new TaskTypeId("no-such-task");

        public const double CrewRadius = 0.3;
        public const double Speed = 2.0;
        public const double FixADuration = 4;
        public const double FixAPower = 100;

        public static readonly BaseRect Bounds = new BaseRect(0, 0, 12, 8);
        public static readonly BaseRect Wall = new BaseRect(5.9, 0, 6.1, 5);

        /// <summary>Completion effect of fix-a: 1 kg untreated in, 0.9 kg potable out, 0.1 kg leaves the boundary.</summary>
        public static ResourceEffect FixAEffect() => new ResourceEffect(new[]
        {
            new ResourceChange(ResourceKind.UntreatedWater, -1.0),
            new ResourceChange(ResourceKind.PotableWater, 0.9)
        }, 0.1);

        /// <summary>Balanced but larger than the untreated stock (10 kg): a start must be rejected beforehand.</summary>
        public static ResourceEffect OverdrawEffect() => new ResourceEffect(new[]
        {
            new ResourceChange(ResourceKind.UntreatedWater, -15.0),
            new ResourceChange(ResourceKind.PotableWater, 15.0)
        }, 0);

        public static OutpostLayout Layout() => new OutpostLayout(
            Bounds,
            new[] { Wall },
            new[]
            {
                new StationPlacement(DockA, new BasePosition(2, 2), 1.2),
                new StationPlacement(DockB, new BasePosition(4, 2), 1.2),
                new StationPlacement(FarLab, new BasePosition(10, 2), 1.2)
            },
            new[] { new BasePosition(2, 3), new BasePosition(4, 3) });

        public static CrewMovementSettings Movement() => new CrewMovementSettings(Speed, CrewRadius);

        public static OutpostConfiguration Simulation() => new OutpostConfiguration(
            new PowerConfiguration(20000, 40000, 400, 100, 80, 300, 500),
            new LifeSupportConfiguration(new WaterStock(20, 10, 40, 20), new FoodStock(15, 30),
                new CabinConfiguration(40, 295, 21.0, 0.5)));

        public static OutpostScenario Scenario() => new OutpostScenario(ScenarioId, Provenance, Layout(), Movement(),
            new[]
            {
                new TaskDefinition(FixA, DockA, TaskCategory.Support, FixADuration, FixAPower, FixAEffect()),
                new TaskDefinition(FixB, DockB, TaskCategory.Support, 3, 0, ResourceEffect.None),
                new TaskDefinition(LabRun, FarLab, TaskCategory.Science, 5, 0, ResourceEffect.None),
                new TaskDefinition(Overdraw, DockA, TaskCategory.Support, 1, 0, OverdrawEffect())
            },
            Simulation());

        /// <summary>Same plan with dock-b walled in on every side: navigable bounds, unreachable station.</summary>
        public static OutpostScenario EnclosedStationScenario()
        {
            var layout = new OutpostLayout(Bounds,
                new[]
                {
                    Wall,
                    new BaseRect(3.0, 0.0, 3.2, 2.6), new BaseRect(4.8, 0.0, 5.0, 2.6), new BaseRect(3.0, 2.6, 5.0, 2.8)
                },
                new[]
                {
                    new StationPlacement(DockA, new BasePosition(2, 2), 1.2),
                    new StationPlacement(DockB, new BasePosition(4, 1), 0.5),
                    new StationPlacement(FarLab, new BasePosition(10, 2), 1.2)
                },
                new[] { new BasePosition(2, 3), new BasePosition(4, 4) });
            return WithLayout(layout);
        }

        /// <summary>The wall closes the door: spawn 1 and far-lab end in a different region than spawn 0.</summary>
        public static OutpostScenario SplitPlanScenario()
        {
            var layout = new OutpostLayout(Bounds,
                new[] { new BaseRect(5.9, 0, 6.1, 8) },
                new[]
                {
                    new StationPlacement(DockA, new BasePosition(2, 2), 1.2),
                    new StationPlacement(DockB, new BasePosition(4, 2), 1.2),
                    new StationPlacement(FarLab, new BasePosition(10, 2), 1.2)
                },
                new[] { new BasePosition(2, 3), new BasePosition(10, 3) });
            return WithLayout(layout);
        }

        public static OutpostScenario WithLayout(OutpostLayout layout) => WithLayoutAndMovement(layout, Movement());

        public static OutpostScenario WithLayoutAndMovement(OutpostLayout layout, CrewMovementSettings movement) =>
            new OutpostScenario(ScenarioId, Provenance, layout, movement,
                new[]
                {
                    new TaskDefinition(FixA, DockA, TaskCategory.Support, FixADuration, FixAPower, FixAEffect()),
                    new TaskDefinition(FixB, DockB, TaskCategory.Support, 3, 0, ResourceEffect.None),
                    new TaskDefinition(LabRun, FarLab, TaskCategory.Science, 5, 0, ResourceEffect.None)
                },
                Simulation());

        public static Guid NewId() => Guid.NewGuid();
    }
}
