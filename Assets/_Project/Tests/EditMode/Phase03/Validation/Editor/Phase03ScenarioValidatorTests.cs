using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Trainer.Application.Development;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Tooling.Validation.Phase03;
using Trainer.Unity.Configuration;
using UnityEngine;

/// <summary>D5 phase 3: the whole-plan validator accepts the shipped and fixture scenarios and detects broken plans.</summary>
public sealed class Phase03ScenarioValidatorTests
{
    private static ExpeditionScenario Shipped()
    {
        var definition = ScriptableObject.CreateInstance<ExpeditionScenarioDefinition>();
        try { return definition.ToScenario(); }
        finally { Object.DestroyImmediate(definition); }
    }

    private static string Failures(List<Phase03Check> checks) => string.Join("\n", checks.Where(c => !c.Passed));

    [Test]
    public void ShippedAndFixtureScenariosPassEveryCheck()
    {
        foreach (ExpeditionScenario s in new[] { Shipped(), ExpeditionFixtures.Scenario() })
        {
            List<Phase03Check> checks = ExpeditionScenarioValidator.Validate(s);
            Assert.That(checks.All(c => c.Passed), s.Id + ":\n" + Failures(checks));
            Assert.That(checks.Count, Is.GreaterThanOrEqualTo(10));
        }
    }

    [Test]
    public void AHatchWithoutWallIsDetectedAsALeakBetweenBaseAndExterior()
    {
        ExpeditionScenario s = ExpeditionFixtures.Scenario();
        // Remove the base east wall segment below the inner hatch: the base now opens into the airlock module gap.
        var obstacles = s.Layout.Obstacles.Where(o => !(o.MinX == 12 && o.MinY == 0 && o.MaxX == 12.3)).ToList();
        obstacles.Remove(obstacles.First(o => o.MinX == 12.3 && o.MinY == 0)); // and the module block under the chamber
        var layout = new OutpostLayout(s.Layout.Bounds, obstacles, s.Layout.Stations, s.Layout.SpawnPoints);
        var outpost = new OutpostScenario(s.Outpost.Id, s.Outpost.Provenance, layout, s.Outpost.Movement, s.Outpost.Tasks, s.Outpost.Simulation);
        var zones = s.Zones.Select(z => z.Zone == CrewZone.Interior ? new ZoneRegion(CrewZone.Interior, new BaseRect(0, 0, 15.3, 10)) : z)
            .Concat(new[] { new ZoneRegion(CrewZone.Interior, new BaseRect(0, 0, 32, 10)) });
        var broken = new ExpeditionScenario(outpost, zones, s.Airlock, s.Racks, s.Eva, s.TaskRules, s.Shelter, s.Comms, s.Episode, s.Rules,
            s.SuitedSpeedFactor);
        List<Phase03Check> checks = ExpeditionScenarioValidator.Validate(broken);
        Assert.That(checks.Single(c => c.Name.StartsWith("airlock closed")).Passed, Is.False, Failures(checks));
    }

    [Test]
    public void AnUnreachableStationAndAMissingEpisodeEndAreDetected()
    {
        ExpeditionScenario s = ExpeditionFixtures.Scenario();
        // Wall off the crater site completely.
        var obstacles = s.Layout.Obstacles.Concat(new[] { new BaseRect(27.4, 6.8, 32, 10) }).ToList();
        var layout = new OutpostLayout(s.Layout.Bounds, obstacles, s.Layout.Stations, s.Layout.SpawnPoints);
        var outpost = new OutpostScenario(s.Outpost.Id, s.Outpost.Provenance, layout, s.Outpost.Movement, s.Outpost.Tasks, s.Outpost.Simulation);
        var noEnd = new ParticleEventEpisode("no-end", true, "SINTÉTICO de prueba", null, 40, 90, new[] { s.Episode.Messages[0] });
        var broken = new ExpeditionScenario(outpost, s.Zones, s.Airlock, s.Racks, s.Eva, s.TaskRules, s.Shelter, s.Comms, noEnd, s.Rules,
            s.SuitedSpeedFactor);
        List<Phase03Check> checks = ExpeditionScenarioValidator.Validate(broken);
        Assert.That(checks.Single(c => c.Name.StartsWith("every station")).Passed, Is.False);
        Assert.That(checks.Single(c => c.Name.StartsWith("particle episode")).Passed, Is.False, "an instruction must end explicitly");
    }
}
