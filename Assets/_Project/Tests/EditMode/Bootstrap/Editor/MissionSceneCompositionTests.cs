using System;
using System.Collections.Generic;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.Development;
using Trainer.Simulation;
using Trainer.Unity.Bootstrap;
using Trainer.Unity.Configuration;
using Trainer.Unity.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using SessionState = Trainer.Application.SessionState;

/// <summary>Composition root, scenario asset and mission scene. EditMode: Awake/Start do not run, so the tests
/// call ApplyRole/ComposeAuthority as Awake/Start would.</summary>
public sealed class MissionSceneCompositionTests
{
    public const string ScenePath = "Assets/_Project/Scenes/Phase01_PowerCoop.unity";
    public const string ScenarioPath = "Assets/_Project/Data/Scenarios/Phase01_PowerCoop_Synthetic.asset";

    private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();
    private MissionSceneComposition composition;
    private MissionBootstrap bootstrap;
    private SimulationRunner runner;
    private MissionHUD hud;
    private GameObject authorityObject;
    private GameObject clientObject;

    [SetUp] public void SetUp()
    {
        created.Clear();
        var scenario = ScriptableObject.CreateInstance<ScenarioDefinition>();
        created.Add(scenario);
        authorityObject = New("Authority");
        authorityObject.SetActive(false);
        runner = authorityObject.AddComponent<SimulationRunner>();
        bootstrap = authorityObject.AddComponent<MissionBootstrap>();
        Set(bootstrap, "scenario", scenario);
        Set(bootstrap, "runner", runner);
        clientObject = New("Client");
        clientObject.SetActive(false);
        hud = New("HUD").AddComponent<MissionHUD>();
        composition = New("Composition").AddComponent<MissionSceneComposition>();
        Set(composition, "bootstrap", bootstrap);
        Set(composition, "hud", hud);
        var so = new SerializedObject(composition);
        SetArray(so.FindProperty("authorityOnly"), authorityObject);
        SetArray(so.FindProperty("clientOnly"), clientObject);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    [TearDown] public void TearDown()
    {
        if (bootstrap != null) bootstrap.Shutdown();
        for (int i = created.Count - 1; i >= 0; i--)
            if (created[i] != null) UnityEngine.Object.DestroyImmediate(created[i]);
    }

    private GameObject New(string name)
    {
        var go = new GameObject(name);
        created.Add(go);
        return go;
    }

    private static void Set(UnityEngine.Object target, string field, UnityEngine.Object value)
    {
        var so = new SerializedObject(target);
        SerializedProperty property = so.FindProperty(field);
        Assert.That(property, Is.Not.Null, field);
        property.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void SetArray(SerializedProperty property, GameObject value)
    {
        property.arraySize = 1;
        property.GetArrayElementAtIndex(0).objectReferenceValue = value;
    }

    [Test] public void RoleComesFromSettingCommandLineAndPlatform()
    {
        string[] none = { "Unity" };
        Assert.That(MissionSceneComposition.ResolveRole(MissionProcessRole.Authority, none, false),
            Is.EqualTo(MissionProcessRole.Authority));
        Assert.That(MissionSceneComposition.ResolveRole(MissionProcessRole.Client, null, false),
            Is.EqualTo(MissionProcessRole.Client));
        Assert.That(MissionSceneComposition.ResolveRole(MissionProcessRole.Authority,
            new[] { "game", "-trainer-role", "client" }, false), Is.EqualTo(MissionProcessRole.Client));
        Assert.That(MissionSceneComposition.ResolveRole(MissionProcessRole.Client,
            new[] { "game", "-batchmode", "-trainer-role", "Server" }, false), Is.EqualTo(MissionProcessRole.Authority));
        Assert.That(MissionSceneComposition.ResolveRole(MissionProcessRole.Authority,
            new[] { "-trainer-role", "authority" }, true), Is.EqualTo(MissionProcessRole.Client), "WebGL never hosts.");
        Assert.Throws<ArgumentException>(() => MissionSceneComposition.ResolveRole(MissionProcessRole.Authority,
            new[] { "-trainer-role", "both" }, false));
    }

    [Test] public void AuthorityComposesOneSessionWithOneRunnerAndShowsItLocally()
    {
        composition.ApplyRole(MissionProcessRole.Authority);
        Assert.That(authorityObject.activeSelf, Is.True);
        Assert.That(clientObject.activeSelf, Is.False);

        MissionSession session = composition.ComposeAuthority();
        Assert.That(session, Is.Not.Null);
        Assert.That(bootstrap.Session, Is.SameAs(session));
        Assert.That(runner.IsBound, Is.True);
        Assert.That(composition.PresentedPort, Is.SameAs(session));
        Assert.That(composition.ComposeAuthority(), Is.SameAs(session), "Composing twice keeps the single session.");
        Assert.That(session.TryGetSnapshot(out MissionSnapshot snapshot), Is.True);
        Assert.That(composition.DescribeOutcome(snapshot), Is.EqualTo(MissionOutcome.InProgress));

        Assert.Throws<InvalidOperationException>(() => composition.PresentRemoteSession(new FakeSessionPort(), true, "x"));
        Assert.Throws<InvalidOperationException>(() => composition.ApplyRole(MissionProcessRole.Client));

        bootstrap.Shutdown();
        Assert.That(runner.IsBound, Is.False);
        Assert.That(composition.PresentedPort, Is.Null, "The HUD drops a released session.");
    }

    [Test] public void ClientNeverComposesAndFollowsTheRemoteLink()
    {
        composition.ApplyRole(MissionProcessRole.Client);
        Assert.That(authorityObject.activeSelf, Is.False);
        Assert.That(clientObject.activeSelf, Is.True);
        Assert.Throws<InvalidOperationException>(() => composition.ComposeAuthority());
        Assert.That(bootstrap.Session, Is.Null);
        Assert.That(runner.IsBound, Is.False);

        // A development port stands in for the network adapter's ISessionPort; it is never an authority here.
        var remote = new FakeSessionPort();
        composition.PresentRemoteSession(remote, false, "Conectando");
        Assert.That(composition.PresentedPort, Is.Null, "Not confirmed yet: nothing is shown.");
        composition.PresentRemoteSession(remote, true, "Conectado");
        Assert.That(composition.PresentedPort, Is.SameAs(remote));
        composition.PresentRemoteSession(remote, false, "Se perdió la conexión");
        Assert.That(composition.PresentedPort, Is.Null);
        composition.PresentRemoteSession(remote, true, "Conectado");
        Assert.That(composition.PresentedPort, Is.SameAs(remote), "Rejoin binds the same adapter again.");

        Assert.That(remote.TryGetSnapshot(out MissionSnapshot ready), Is.True);
        Assert.That(composition.DescribeOutcome(ready), Is.EqualTo(MissionOutcome.InProgress));
        remote.PublishExample(SessionState.Completed, 1e6);
        Assert.That(remote.TryGetSnapshot(out MissionSnapshot done), Is.True);
        Assert.That(composition.DescribeOutcome(done), Is.EqualTo(MissionOutcome.ObjectiveAchieved),
            "Clients describe a published result with the build's scenario rules.");
    }

    [Test] public void SyntheticScenarioAssetIsLabelledAndMatchesTheAnalysedValues()
    {
        var scenario = AssetDatabase.LoadAssetAtPath<ScenarioDefinition>(ScenarioPath);
        Assert.That(scenario, Is.Not.Null, ScenarioPath);
        Assert.That(scenario.Validate(), Is.Null);
        StringAssert.Contains("sintético", scenario.DisplayName);
        StringAssert.Contains("No representan hardware ni datos de la NASA", scenario.Provenance);

        PowerConfiguration c = scenario.ToPowerConfiguration();
        Assert.That(c.InitialEnergyJoules, Is.EqualTo(MissionScenarioTests.InitialEnergyJoules));
        Assert.That(c.CapacityJoules, Is.EqualTo(MissionScenarioTests.CapacityJoules));
        Assert.That(c.GenerationWatts, Is.EqualTo(MissionScenarioTests.GenerationWatts));
        Assert.That(c.EssentialConsumptionWatts, Is.EqualTo(MissionScenarioTests.EssentialWatts));
        Assert.That(c.ScienceConsumptionWatts, Is.EqualTo(MissionScenarioTests.ScienceWatts));
        Assert.That(c.MaxChargeWatts, Is.EqualTo(MissionScenarioTests.MaxChargeWatts));
        Assert.That(c.MaxDischargeWatts, Is.EqualTo(MissionScenarioTests.MaxDischargeWatts));
        MissionRules rules = scenario.ToMissionRules();
        Assert.That(rules.ScienceWorkTargetSeconds, Is.EqualTo(MissionScenarioTests.TargetSeconds));
        Assert.That(rules.MissionDurationSeconds, Is.EqualTo(MissionScenarioTests.DurationSeconds));
        Assert.That(scenario.TimeScale, Is.EqualTo(2));
        Assert.That(scenario.MaxStepSeconds, Is.EqualTo(1));
    }

    [Test] public void MissionSceneHasOneCompositionOneAuthorityAndTheScenario()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            var compositions = new List<MissionSceneComposition>();
            var bootstraps = new List<MissionBootstrap>();
            var runners = new List<SimulationRunner>();
            var huds = new List<MissionHUD>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                compositions.AddRange(root.GetComponentsInChildren<MissionSceneComposition>(true));
                bootstraps.AddRange(root.GetComponentsInChildren<MissionBootstrap>(true));
                runners.AddRange(root.GetComponentsInChildren<SimulationRunner>(true));
                huds.AddRange(root.GetComponentsInChildren<MissionHUD>(true));
            }
            Assert.That(compositions.Count, Is.EqualTo(1));
            Assert.That(bootstraps.Count, Is.EqualTo(1));
            Assert.That(runners.Count, Is.EqualTo(1), "A single clock per shared session.");
            Assert.That(huds.Count, Is.EqualTo(1));

            MissionSceneComposition root0 = compositions[0];
            Assert.That(root0.Bootstrap, Is.SameAs(bootstraps[0]));
            Assert.That(root0.Hud, Is.SameAs(huds[0]));
            Assert.That(PrefabUtility.IsPartOfPrefabInstance(huds[0]), Is.True, "The HUD comes from the D4 prefab.");
            Assert.That(AssetDatabase.GetAssetPath(bootstraps[0].Scenario), Is.EqualTo(ScenarioPath));
            var bootstrapData = new SerializedObject(bootstraps[0]);
            Assert.That(bootstrapData.FindProperty("runner").objectReferenceValue, Is.SameAs(runners[0]));
            Assert.That(bootstraps[0].gameObject.activeSelf, Is.False,
                "Authority objects are saved inactive and enabled only in the authority process.");
            var compositionData = new SerializedObject(root0);
            SerializedProperty authorityOnly = compositionData.FindProperty("authorityOnly");
            bool listed = false;
            for (int i = 0; i < authorityOnly.arraySize; i++)
                listed |= authorityOnly.GetArrayElementAtIndex(i).objectReferenceValue == bootstraps[0].gameObject;
            Assert.That(listed, Is.True);
            Assert.That(compositionData.FindProperty("clientOnly").arraySize, Is.GreaterThanOrEqualTo(1));
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }
}
