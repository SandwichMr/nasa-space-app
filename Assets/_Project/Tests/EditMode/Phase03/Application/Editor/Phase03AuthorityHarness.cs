using System;
using System.Collections.Generic;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.Crew;
using Trainer.Application.Development;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation;
using Trainer.Simulation.Eva;
using Trainer.Simulation.LifeSupport;

/// <summary>
/// Test driver of the REAL phase 3 stack (D2 authority + D1 base and EVA models + D3 roster) on a scenario. It acts
/// like players: held movement directions toward waypoints (never positions), commands stamped with the confirmed
/// zone sequence and airlock cycle, and the authority clock. It decides nothing itself.
/// </summary>
public sealed class Phase03AuthorityHarness
{
    public readonly ExpeditionScenario Scenario;
    public readonly OutpostBaseSimulation Base;
    public readonly EvaSuitSimulation Eva;
    public readonly CrewRoster Roster;
    public readonly ExpeditionAuthority Authority;
    public readonly List<ExpeditionSessionSnapshot> Published = new List<ExpeditionSessionSnapshot>();
    private readonly Dictionary<PlayerId, long> sequences = new Dictionary<PlayerId, long>();
    private readonly Dictionary<Guid, CommandResult> results = new Dictionary<Guid, CommandResult>();

    public Phase03AuthorityHarness(ExpeditionScenario scenario = null)
    {
        Scenario = scenario ?? ExpeditionFixtures.Scenario(false);
        Base = new OutpostBaseSimulation(OutpostSimulationParameters.SyntheticTraining());
        Eva = new EvaSuitSimulation();
        Roster = CrewRoster.For(Scenario.Outpost);
        Authority = new ExpeditionAuthority(Scenario, Base, Eva, Roster);
        Authority.SnapshotChanged += s => Published.Add(s);
        Authority.CommandCompleted += (sender, result) => results[result.CommandId] = result;
    }

    public ExpeditionSessionSnapshot Now
    {
        get
        {
            Authority.TryGetSnapshot(out ExpeditionSessionSnapshot s);
            return s;
        }
    }

    public double Time => Now.SimulatedTimeSeconds;

    public PlayerId Join()
    {
        OutpostMembership m = Authority.Join();
        Assert.That(m.IsJoined, m.Reason);
        return m.Player;
    }

    public ExpeditionCrewState Crew(PlayerId p)
    {
        Assert.That(Now.TryGetCrew(p, out ExpeditionCrewState c), p + " present");
        return c;
    }

    public BasePosition Position(PlayerId p)
    {
        Assert.That(Now.Outpost.TryGetCrew(p, out CrewMemberState c), p + " present");
        return c.Pose.Position;
    }

    public CommandResult Send(PlayerId p, ExpeditionCommand command)
    {
        Authority.Submit(p, command);
        Assert.That(results.TryGetValue(command.Id, out CommandResult r), "every command is answered");
        return r;
    }

    public CommandResult Session(PlayerId p, ExpeditionAction action) =>
        Send(p, ExpeditionCommand.Session(Guid.NewGuid(), Now.Epoch, action));

    public void Start(PlayerId p = default) => Assert.That(Session(p.IsValid ? p : Now.Crew[0].Player, ExpeditionAction.Resume).Accepted);

    public CommandResult StartTask(PlayerId p, StationId station, TaskTypeId task) =>
        Send(p, ExpeditionCommand.StartTask(Guid.NewGuid(), Now.Epoch, station, task, Crew(p).ZoneSequence));

    public CommandResult Cancel(PlayerId p, StationId station) =>
        Send(p, ExpeditionCommand.CancelTask(Guid.NewGuid(), Now.Epoch, station));

    public CommandResult Cycle(PlayerId p, StationId panel) =>
        Send(p, ExpeditionCommand.CycleAirlock(Guid.NewGuid(), Now.Epoch, panel, Crew(p).ZoneSequence, Now.Airlock.CycleSequence));

    public CommandResult DeclareReturn(PlayerId p) => Send(p, ExpeditionCommand.DeclareReturn(Guid.NewGuid(), Now.Epoch));

    public void Tick(double seconds, double dt = 0.1)
    {
        for (double left = seconds; left > 1e-12; left -= dt) Authority.Tick(Math.Min(dt, left));
    }

    /// <summary>Ticks until <paramref name="done"/> or <paramref name="maxSeconds"/> of simulated time.</summary>
    public void Until(Func<bool> done, double maxSeconds, double dt = 0.1)
    {
        double start = Time;
        while (!done())
        {
            if (Time - start > maxSeconds) Assert.Fail($"Condition not reached within {maxSeconds} s (t = {Time:0.###}).");
            if (Now.Outpost.State != SessionState.Running) Assert.Fail($"Session not running (state {Now.Outpost.State}, outcome {Now.Outcome}).");
            Authority.Tick(dt);
        }
    }

    /// <summary>Walks <paramref name="p"/> through <paramref name="waypoints"/> with held directions only.</summary>
    public void Walk(PlayerId p, params BasePosition[] waypoints)
    {
        foreach (BasePosition target in waypoints)
        {
            for (int guard = 0; ; guard++)
            {
                Assert.That(guard, Is.LessThan(2000), $"{p} could not reach {target} (at {Position(p)})");
                BasePosition at = Position(p);
                double dx = target.X - at.X, dy = target.Y - at.Y, length = Math.Sqrt(dx * dx + dy * dy);
                if (length <= 0.02) break;
                double speed = Scenario.Outpost.Movement.MaxSpeedMetersPerSecond * (Crew(p).OnSuitSupply ? Scenario.SuitedSpeedFactor : 1d);
                Move(p, MoveDirection.FromAxes(dx / length, dy / length));
                double before = length;
                Authority.Tick(Math.Min(0.1, length / speed));
                if (Now.Outpost.State != SessionState.Running)
                {
                    Move(p, MoveDirection.Zero);
                    return; // the session ended on the way (for example the last member stepped back in)
                }
                if (Position(p).DistanceTo(target) >= before - 1e-9) Assert.Fail($"{p} is blocked at {Position(p)} on the way to {target}.");
            }
            Move(p, MoveDirection.Zero);
        }
    }

    public void Move(PlayerId p, MoveDirection direction)
    {
        sequences.TryGetValue(p, out long s);
        sequences[p] = ++s;
        Assert.That(Authority.SubmitMovement(p, new MovementInput(Now.Epoch, s, direction)), Is.EqualTo(MovementInputDisposition.Applied));
    }

    /// <summary>Runs a task to completion: start, then tick until the station ends it.</summary>
    public void DoTask(PlayerId p, StationId station, TaskTypeId task, double maxSeconds = 60)
    {
        CommandResult r = StartTask(p, station, task);
        Assert.That(r.Accepted, $"{task} at {station}: {r.Reason}");
        Until(() => Now.Outpost.TryGetStation(station, out StationState s) && s.ActiveTask == null, maxSeconds);
        Now.Outpost.TryGetStation(station, out StationState end);
        Assert.That(end.LastEnd.Reason, Is.EqualTo(TaskEndReason.Completed), $"{task} ended {end.LastEnd.Reason}");
    }

    // ---- Fixture routes (waypoints that keep the disc clear of walls) ----------------------------------------

    public static readonly BasePosition AtRackA = new BasePosition(1.5, 8.3);
    public static readonly BasePosition AtRackB = new BasePosition(3.5, 8.3);
    public static readonly BasePosition AtCharger = new BasePosition(5.5, 8.3);
    public static readonly BasePosition AtConsumables = new BasePosition(7.5, 8.3);
    public static readonly BasePosition AtSuitCheck = new BasePosition(10, 7.2);
    public static readonly BasePosition BeforeInnerHatch = new BasePosition(11.2, 5.0);
    public static readonly BasePosition ChamberA = new BasePosition(12.9, 4.4);
    public static readonly BasePosition ChamberB = new BasePosition(12.9, 5.6);
    public static readonly BasePosition OutsideHatch = new BasePosition(15.8, 5.0);
    public static readonly BasePosition AtRegolith = new BasePosition(21, 5);
    public static readonly BasePosition TowardCrater = new BasePosition(24, 9);
    public static readonly BasePosition AtCrater = new BasePosition(28, 8.8);
    public static readonly BasePosition ShelterDoorAbove = new BasePosition(3, 4.2);
    public static readonly BasePosition InShelter = new BasePosition(2.2, 1.6);
    public static readonly BasePosition AtWater = new BasePosition(10, 2.3);

    /// <summary>Takes suit <paramref name="rack"/> and completes the whole checklist (walking between stations).</summary>
    public void PrepareSuit(PlayerId p, bool rackA)
    {
        Walk(p, rackA ? AtRackA : AtRackB);
        DoTask(p, rackA ? ExpeditionFixtures.RackA : ExpeditionFixtures.RackB, rackA ? ExpeditionFixtures.DonA : ExpeditionFixtures.DonB);
        Walk(p, AtCharger);
        DoTask(p, ExpeditionFixtures.Charger, ExpeditionFixtures.ChargeSuit);
        Walk(p, AtConsumables);
        DoTask(p, ExpeditionFixtures.Consumables, ExpeditionFixtures.FillSuit);
        Walk(p, AtSuitCheck);
        DoTask(p, ExpeditionFixtures.SuitCheck, ExpeditionFixtures.CheckSuit);
        Assert.That(Now.TryGetSuitOf(p, out SuitState suit) && suit.IsReady, "suit ready after the checklist");
    }

    /// <summary>From the suit check to the chamber, cycle out and step outside.</summary>
    public void GoOut(PlayerId p, BasePosition chamberSpot)
    {
        Walk(p, BeforeInnerHatch, chamberSpot);
        CommandResult r = Cycle(p, ExpeditionFixtures.InnerPanel);
        Assert.That(r.Accepted, r.Reason);
        Until(() => Now.Airlock.State == AirlockState.DepressurizedOuterOpen, 30);
        Walk(p, OutsideHatch);
        Assert.That(Crew(p).Zone, Is.EqualTo(CrewZone.Exterior));
    }

    /// <summary>From outside back into the chamber, cycle in and step into the base.</summary>
    public void ComeBack(PlayerId p, BasePosition chamberSpot, bool cycle = true)
    {
        Walk(p, OutsideHatch, new BasePosition(14.2, chamberSpot.Y), chamberSpot);
        if (!cycle) return;
        CommandResult r = Cycle(p, ExpeditionFixtures.InnerPanel);
        Assert.That(r.Accepted, r.Reason);
        Until(() => Now.Airlock.State == AirlockState.PressurizedInnerOpen, 30);
        Assert.That(Crew(p).OnSuitSupply, Is.False);
        Walk(p, BeforeInnerHatch);
    }

    public static ExpeditionScenario With(ExpeditionScenario s, EvaConfiguration eva = null, ParticleEventEpisode episode = null,
        bool clearEpisode = false, ExpeditionMissionRules rules = null, OutpostScenario outpost = null) =>
        new ExpeditionScenario(outpost ?? s.Outpost, s.Zones, s.Airlock, s.Racks, eva ?? s.Eva, s.TaskRules, s.Shelter, s.Comms,
            clearEpisode ? null : episode ?? s.Episode, rules ?? s.Rules, s.SuitedSpeedFactor);

    /// <summary>The fixture plan with another base power configuration.</summary>
    public static OutpostScenario WithPower(OutpostScenario o, PowerConfiguration power) =>
        new OutpostScenario(o.Id, o.Provenance, o.Layout, o.Movement, o.Tasks,
            new OutpostConfiguration(power, o.Simulation.LifeSupport));
}
