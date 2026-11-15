using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.Outpost;
using Trainer.Tests.Phase02;
using Trainer.Unity.Bootstrap;
using Trainer.Unity.Configuration;
using UnityEditor;
using UnityEngine;
using SessionState = Trainer.Application.SessionState;

/// <summary>D2: the shipped phase 2 scenario asset, its conversion and the authority-side bootstrap. The full
/// interior mission is played over the real asset with D2's test doubles of D1/D3 (not their implementations).</summary>
public sealed class OutpostScenarioDefinitionTests
{
    public const string AssetPath = "Assets/_Project/Data/Scenarios/Phase02/Phase02_InteriorBase_Synthetic.asset";
    private readonly List<UnityEngine.Object> created = new List<UnityEngine.Object>();

    private static OutpostScenarioDefinition Shipped()
    {
        var definition = AssetDatabase.LoadAssetAtPath<OutpostScenarioDefinition>(AssetPath);
        Assert.That(definition, Is.Not.Null, AssetPath);
        return definition;
    }

    [TearDown]
    public void TearDown()
    {
        foreach (UnityEngine.Object o in created) if (o != null) UnityEngine.Object.DestroyImmediate(o);
        created.Clear();
    }

    [Test]
    public void ShippedAssetIsValidAndLabelledSynthetic()
    {
        OutpostScenarioDefinition definition = Shipped();
        Assert.That(definition.Validate(), Is.Null);
        OutpostScenario scenario = definition.ToScenario();
        Assert.That(scenario.Id, Is.EqualTo("interior-base-synthetic"));
        Assert.That(scenario.Provenance, Does.Contain("sintéticos").And.Contain("No representan"));
        Assert.That(scenario.Layout.MaxCrew, Is.EqualTo(4));
        Assert.That(scenario.Layout.Stations.Select(s => s.Id.Value),
            Is.EqualTo(new[] { "air-scrubber", "water-recycler", "science-lab", "sample-analyzer" }));
        Assert.That(scenario.Tasks.Where(t => t.Category == TaskCategory.Science).All(t => t.PowerWatts == 0));
        OutpostMissionRules rules = definition.ToRules();
        Assert.That(rules.Objectives.Select(o => (o.Key, o.TargetCount, o.Required)),
            Is.EqualTo(new[] { ("treat-water", 2, true), ("check-cabin-air", 1, true), ("science", 2, false) }));
        Assert.That(definition.TimeScale, Is.GreaterThan(0));
    }

    [Test]
    public void EveryStationCanBeReachedFromAnAdmissiblePosition()
    {
        OutpostScenario scenario = Shipped().ToScenario();
        double radius = scenario.Movement.CrewRadiusMeters;
        foreach (StationPlacement station in scenario.Layout.Stations)
        {
            bool reachable = false;
            for (double dx = -station.InteractionRadiusMeters; dx <= station.InteractionRadiusMeters && !reachable; dx += 0.05)
                for (double dy = -station.InteractionRadiusMeters; dy <= station.InteractionRadiusMeters && !reachable; dy += 0.05)
                {
                    var p = new BasePosition(station.Position.X + dx, station.Position.Y + dy);
                    reachable = station.IsWithinReach(p) && scenario.Layout.IsAdmissible(p, radius);
                }
            Assert.That(reachable, Is.True, station.Id.Value);
        }
    }

    [Test]
    public void ValidateReportsInvalidDataWithoutThrowing()
    {
        var empty = ScriptableObject.CreateInstance<OutpostScenarioDefinition>();
        created.Add(empty);
        Assert.That(empty.Validate(), Is.Not.Null.And.Not.Empty, "no stations, spawns or tasks");
        Assert.Throws<InvalidOperationException>(() =>
            OutpostBootstrap.Create(empty, s => new ScriptedOutpostSimulation(), s => new ScriptedCrewRoster(s)));
    }

    [Test]
    public void TwoCrewMembersCompleteTheInteriorMissionOnTheShippedScenario()
    {
        ScriptedCrewRoster roster = null;
        ScriptedOutpostSimulation simulation = null;
        OutpostAuthority authority = OutpostBootstrap.Create(Shipped(),
            s => simulation = new ScriptedOutpostSimulation(), s => roster = new ScriptedCrewRoster(s));
        var results = new List<CommandResult>();
        authority.CommandCompleted += (p, r) => results.Add(r);

        PlayerId ana = authority.Join().Player, leo = authority.Join().Player;
        StationId air = new StationId("air-scrubber"), water = new StationId("water-recycler"),
            lab = new StationId("science-lab"), analyzer = new StationId("sample-analyzer");
        void Do(PlayerId p, OutpostCommand c) { authority.Submit(p, c); Assert.That(results.Last().Accepted, Is.True, results.Last().Reason); }
        OutpostCommand Start(StationId s, string t) => OutpostCommand.StartTask(Guid.NewGuid(), 0, s, new TaskTypeId(t));
        void Run(double seconds) { for (int i = 0; i < Math.Round(seconds * 10); i++) authority.Tick(0.1); }

        Do(ana, new OutpostCommand(Guid.NewGuid(), 0, OutpostAction.Resume));
        roster.Place(ana, new BasePosition(15, 8.6));
        roster.Place(leo, new BasePosition(9, 8.6));
        Do(ana, Start(lab, "run-experiment"));        // optional science first: the mission ends with the last required task
        Do(leo, Start(water, "treat-water"));
        authority.Submit(leo, Start(lab, "run-experiment"));
        Assert.That(results.Last().Reason, Is.EqualTo(OutpostReasons.PlayerBusy));
        Run(20.5);
        Do(leo, Start(water, "treat-water"));
        Run(10);
        roster.Place(ana, new BasePosition(15, 1.4));
        Do(ana, Start(analyzer, "analyze-sample"));
        Run(25.5);
        Assert.That(authority.TryGetSnapshot(out OutpostSessionSnapshot mid), Is.True);
        Assert.That(mid.State, Is.EqualTo(SessionState.Running), "check-cabin-air is still required");
        Assert.That(mid.Objectives.Select(o => o.Current), Is.EqualTo(new[] { 2d, 0d, 2d }));

        roster.Place(leo, new BasePosition(3, 8.6));
        Do(leo, Start(air, "check-cabin-air"));
        Run(10.5);
        authority.TryGetSnapshot(out OutpostSessionSnapshot end);
        Assert.That(end.State, Is.EqualTo(SessionState.Completed));
        Assert.That(end.Outcome, Is.EqualTo(OutpostOutcome.ObjectiveAchieved));
        Assert.That(end.Objectives.All(o => o.IsMet));
        Assert.That(simulation.ApplyCalls.Count, Is.EqualTo(2), "two water treatments, each applied once");
        Assert.That(end.Resources.LifeSupport.Water.UntreatedKilograms, Is.EqualTo(2).Within(1e-9));
        Assert.That(end.Resources.LifeSupport.Water.PotableKilograms, Is.EqualTo(23.6).Within(1e-9));

        Do(ana, new OutpostCommand(Guid.NewGuid(), 0, OutpostAction.Restart));
        authority.TryGetSnapshot(out OutpostSessionSnapshot restarted);
        Assert.That((restarted.Epoch, restarted.State), Is.EqualTo((1L, SessionState.Ready)));
        Assert.That(restarted.Objectives.All(o => o.Current == 0));
        Assert.That(restarted.Crew.Count, Is.EqualTo(2));
    }

    [Test]
    public void BootstrapComposesOnceBindsTheRunnerAndShutsDown()
    {
        var go = new GameObject("outpost-bootstrap-test");
        created.Add(go);
        go.SetActive(false);
        OutpostRunner runner = go.AddComponent<OutpostRunner>();
        OutpostBootstrap bootstrap = go.AddComponent<OutpostBootstrap>();
        var so = new SerializedObject(bootstrap);
        so.FindProperty("scenario").objectReferenceValue = Shipped();
        so.FindProperty("runner").objectReferenceValue = runner;
        so.ApplyModifiedPropertiesWithoutUndo();

        OutpostAuthority composed = null, released = null;
        bootstrap.Composed += a => composed = a;
        bootstrap.ShutDown += a => released = a;
        OutpostAuthority authority = bootstrap.Compose(s => new ScriptedOutpostSimulation(), s => new ScriptedCrewRoster(s));
        Assert.That(composed, Is.SameAs(authority));
        Assert.That(bootstrap.Authority, Is.SameAs(authority));
        Assert.That(runner.IsBound, Is.True);
        Assert.Throws<InvalidOperationException>(() => bootstrap.Compose(s => new ScriptedOutpostSimulation(), s => new ScriptedCrewRoster(s)));

        bootstrap.Shutdown();
        Assert.That(released, Is.SameAs(authority));
        Assert.That(bootstrap.Authority, Is.Null);
        Assert.That(runner.IsBound, Is.False);
        bootstrap.Shutdown();
    }
}
