using System.Linq;
using NUnit.Framework;
using Trainer.Application.Expedition;
using Trainer.Tooling.BuildChecks.Phase03;
using Trainer.Tooling.Composition.Phase03;
using Trainer.Unity.Bootstrap;
using Trainer.Unity.Configuration;
using UnityEditor;
using UnityEngine;

/// <summary>D2/D5 phase 3: the shipped scene and asset exist, are wired and compose the real authority.</summary>
public sealed class Phase03SceneTests
{
    [Test]
    public void TheShippedSceneIsWiredWithoutPhysicsFakesOrASecondCable()
    {
        var checks = Phase03BuildChecks.AuditScene(Phase03SceneBuilder.ScenePath);
        Assert.That(checks.All(c => c.Passed), string.Join("\n", checks.Where(c => !c.Passed)));
        Assert.That(Phase03BuildChecks.ScanProductionFakeUsage().Passed);
        Assert.That(Phase03BuildChecks.Phase2DiscoveryUnchanged().Passed, Phase03BuildChecks.Phase2DiscoveryUnchanged().Detail);
    }

    [Test]
    public void TheScenarioAssetMatchesTheShippedDefaultsAndComposesTheRealAuthority()
    {
        var asset = AssetDatabase.LoadAssetAtPath<ExpeditionScenarioDefinition>(Phase03SceneBuilder.ScenarioPath);
        Assert.That(asset, Is.Not.Null, Phase03SceneBuilder.ScenarioPath);
        Assert.That(asset.Validate(), Is.Null);
        var defaults = ScriptableObject.CreateInstance<ExpeditionScenarioDefinition>();
        defaults.name = asset.name; // only the asset name differs from a fresh instance
        try
        {
            Assert.That(EditorJsonUtility.ToJson(asset), Is.EqualTo(EditorJsonUtility.ToJson(defaults)), "the asset is the shipped synthetic scenario");
        }
        finally { Object.DestroyImmediate(defaults); }
        ExpeditionAuthority authority = ExpeditionBootstrap.Create(asset);
        Assert.That(authority.TryGetSnapshot(out ExpeditionSessionSnapshot s) && s.Outpost.ScenarioId == "eva-shelter-synthetic");
        Assert.That(s.Suits.Count, Is.EqualTo(2));
        Assert.That(authority.Scenario.Episode.Synthetic, Is.True);
        Assert.That(authority.Route.ReachableCells, Is.GreaterThan(1000));
    }
}
