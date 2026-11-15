using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using Trainer.Application.Expedition;
using Trainer.Tooling.BuildChecks.Phase03;
using Trainer.Tooling.Composition.Phase03;
using Trainer.Unity.Bootstrap;
using Trainer.Unity.Configuration;
using Trainer.Unity.Controls;
using Trainer.Unity.Networking.Expedition;
using Trainer.Unity.Presentation.Expedition;
using Trainer.Unity.World.Expedition;
using Trainer.Unity.World.Outpost;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>D2/D5 phase 3: the shipped scene and asset exist, are wired and compose the real authority.</summary>
public sealed class Phase03SceneTests
{
    [Test]
    public void D4ViewsReadTheRealPortAndReleaseMovementAndInteractionOnDisconnect()
    {
        var definition = AssetDatabase.LoadAssetAtPath<ExpeditionScenarioDefinition>(Phase03SceneBuilder.ScenarioPath);
        var authority = ExpeditionBootstrap.Create(definition);
        var root = new GameObject("D4 integration test");
        ExpeditionPanel panel = null;
        ExpeditionWorldView world = null;
        using (var port = new LocalExpeditionSessionPort(authority))
        {
            try
            {
                var keyboard = root.AddComponent<CrewKeyboardInput>();
                var input = root.AddComponent<ExpeditionInteractionInput>();
                keyboard.Bind(port, authority.Scenario.Outpost);
                var intents = input.Bind(port, authority.Scenario);
                var baseView = Spawn(Phase03SceneBuilder.BaseInteriorPrefab, root).GetComponent<BaseInteriorView>();
                baseView.Configure(authority.Scenario.Outpost);
                world = Spawn(Phase03SceneBuilder.WorldPrefab, root).GetComponent<ExpeditionWorldView>();
                var so = new SerializedObject(world);
                so.FindProperty("baseView").objectReferenceValue = baseView;
                so.ApplyModifiedPropertiesWithoutUndo();
                panel = Spawn(Phase03SceneBuilder.PanelPrefab, root).GetComponent<ExpeditionPanel>();
                var context = new ExpeditionViewContext(port, authority.Scenario, intents, (owner, blocked) =>
                {
                    if (blocked) { keyboard.Block(owner); input.Block(owner); }
                    else { keyboard.Unblock(owner); input.Unblock(owner); }
                });
                world.Bind(context);
                panel.Bind(context);
                Assert.That(panel.Live, Is.False);
                Assert.That(port.Join().IsJoined, Is.True);
                Assert.That(panel.Live && world.Live, Is.True);
                Assert.That(authority.TryGetSnapshot(out var snapshot), Is.True);
                Assert.That(panel.Shown, Is.SameAs(snapshot), "D4 reads the actual authority snapshot");
                Assert.That(world.Shown, Is.SameAs(snapshot));
                Assert.That(world.GetComponentsInChildren<Collider2D>(true), Is.Empty);
                Assert.That(world.GetComponentsInChildren<OutpostStationView>().Length, Is.EqualTo(2));
                Assert.That(baseView.Stations.Count(s => !s.gameObject.activeSelf), Is.EqualTo(4),
                    "two science stations and two hatch panels are replaced, not duplicated");

                typeof(ExpeditionPanel).GetMethod("ToggleDetails", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(panel, null);
                Assert.That(keyboard.IsBlocked && input.IsBlocked, Is.True, "focused details own both inputs");
                port.Leave();
                Assert.That(panel.Live || world.Live, Is.False);
                Assert.That(panel.Shown, Is.Null);
                Assert.That(keyboard.IsBlocked || input.IsBlocked, Is.False, "disconnect cannot retain a panel's input ownership");
                Assert.That(baseView.Stations.All(s => s.gameObject.activeSelf), Is.True);
                port.Join();
                Assert.That(panel.Live && world.Live, Is.True);
                Assert.That(world.transform.childCount, Is.EqualTo(1), "rejoin does not duplicate geometry");
                panel.Unbind(); world.Unbind();
                keyboard.Bind(null, null); input.Bind(null, null);
            }
            finally
            {
                if (panel != null) panel.Unbind();
                if (world != null) world.Unbind();
                Object.DestroyImmediate(root);
            }
        }
    }

    private static GameObject Spawn(string path, GameObject root) =>
        Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(path), root.transform);

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
