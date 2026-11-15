using System;
using System.Collections.Generic;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.BaseOperations;
using Trainer.Application.Crew;
using Trainer.Application.Development;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.Eva;
using Trainer.Simulation.LifeSupport;

/// <summary>
/// Test driver of the REAL phase 4 stack: D2 continuous authority + D1 base, EVA and equipment models + D3 roster, on the
/// synthetic fixture scenario. It acts like players (held directions, never positions; commands stamped with the confirmed
/// zone sequence and the observation they saw) and drives the single clock. It decides nothing itself.
/// </summary>
public sealed class Phase04LivingBaseHarness
{
    public readonly BaseOperationsScenario Scenario;
    public readonly OutpostBaseSimulation Base;
    public readonly BaseEquipmentSimulation Equipment;
    public readonly EvaSuitSimulation Eva;
    public readonly CrewRoster Roster;
    public readonly BaseOperationsAuthority Authority;
    public readonly List<BaseOperationsSnapshot> Published = new List<BaseOperationsSnapshot>();
    public readonly List<(PlayerId Sender, CommandResult Result)> Results = new List<(PlayerId, CommandResult)>();
    private readonly Dictionary<PlayerId, long> sequences = new Dictionary<PlayerId, long>();

    public static readonly BasePosition AtWater = new BasePosition(10, 2.3);
    public static readonly BasePosition AtAir = new BasePosition(2.6, 6.0);
    public static readonly BasePosition AtLogistics = new BasePosition(5.0, 5.4);
    public static readonly BasePosition AtRackA = new BasePosition(1.5, 8.3);
    public static readonly BasePosition AtCharger = new BasePosition(5.5, 8.3);
    public static readonly BasePosition AtConsumables = new BasePosition(7.5, 8.3);
    public static readonly BasePosition AtSuitCheck = new BasePosition(10, 7.2);
    public static readonly BasePosition BeforeInnerHatch = new BasePosition(11.2, 5.0);
    public static readonly BasePosition ChamberA = new BasePosition(12.9, 4.4);
    public static readonly BasePosition OutsideHatch = new BasePosition(15.8, 5.0);
    public static readonly BasePosition AtRegolith = new BasePosition(21, 5);

    public Phase04LivingBaseHarness(BaseOperationsScenario scenario = null, BaseSessionMode mode = BaseSessionMode.Continuous,
        Func<BaseEquipmentSimulation, IBaseEquipmentModel> equipmentDecorator = null,
        Func<EvaSuitSimulation, IEvaSimulation> evaDecorator = null)
    {
        Scenario = scenario ?? BaseOperationsFixtures.Scenario(null);
        Base = new OutpostBaseSimulation(OutpostSimulationParameters.SyntheticTraining());
        Equipment = new BaseEquipmentSimulation();
        Eva = new EvaSuitSimulation();
        Roster = CrewRoster.For(Scenario.Expedition.Outpost);
        Authority = new BaseOperationsAuthority(Scenario, Base, equipmentDecorator?.Invoke(Equipment) ?? Equipment,
            evaDecorator?.Invoke(Eva) ?? Eva, Roster, mode);
        Authority.SnapshotChanged += s => Published.Add(s);
        Authority.CommandCompleted += (sender, result) => Results.Add((sender, result));
    }

    /// <summary>The fixture scenario with a different base power configuration (and optional degradation time).</summary>
    public static BaseOperationsScenario WithPower(PowerConfiguration power, double? degradeAt = null)
    {
        BaseOperationsScenario s = BaseOperationsFixtures.Scenario(degradeAt);
        OutpostScenario o = s.Expedition.Outpost;
        var outpost = new OutpostScenario(o.Id, o.Provenance, o.Layout, o.Movement, o.Tasks, new OutpostConfiguration(power, o.Simulation.LifeSupport));
        ExpeditionScenario e = s.Expedition;
        var expedition = new ExpeditionScenario(outpost, e.Zones, e.Airlock, e.Racks, e.Eva, e.TaskRules, e.Shelter, e.Comms, e.Episode, e.Rules,
            e.SuitedSpeedFactor);
        return new BaseOperationsScenario(expedition, s.Equipment, s.WaterStation, s.AirStation, s.Operations, s.ObservationValiditySeconds,
            s.Logistics, s.Manifests, s.Gauges, s.Irreversible, s.ReportIntervalSeconds, s.Science, s.Provenance);
    }

    public BaseOperationsSnapshot Now
    {
        get
        {
            Assert.That(Authority.TryGetSnapshot(out BaseOperationsSnapshot s), Is.True);
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
        Assert.That(Now.Expedition.TryGetCrew(p, out ExpeditionCrewState c), p + " present");
        return c;
    }

    public BasePosition Position(PlayerId p)
    {
        Assert.That(Now.Outpost.TryGetCrew(p, out CrewMemberState c), p + " present");
        return c.Pose.Position;
    }

    private CommandResult Last(Guid id)
    {
        for (int i = Results.Count - 1; i >= 0; i--)
            if (Results[i].Result.CommandId == id) return Results[i].Result;
        Assert.Fail("every command is answered");
        return null;
    }

    public CommandResult Send(PlayerId p, ExpeditionCommand command)
    {
        Authority.Submit(p, command);
        return Last(command.Id);
    }

    public CommandResult Send(PlayerId p, BaseOperationCommand command)
    {
        Authority.Submit(p, command);
        return Last(command.Id);
    }

    public CommandResult Session(PlayerId p, ExpeditionAction action) =>
        Send(p, ExpeditionCommand.Session(Guid.NewGuid(), Now.Epoch, action));

    public void Start(PlayerId p) => Assert.That(Session(p, ExpeditionAction.Resume).Accepted);

    public EquipmentStatus Machine(EquipmentKind kind) => Now.EquipmentOf(kind);

    public long ObservationOf(EquipmentKind kind) => Machine(kind).LatestObservation?.Sequence ?? 1;

    public BaseOperationCommand Op(BaseOperationAction action, StationId station, OperationChoice choice = OperationChoice.None, long observation = -1) =>
        BaseOperationCommand.Operation(Guid.NewGuid(), Now.Epoch, action, station, choice, observation);

    public CommandResult Do(PlayerId p, BaseOperationAction action, StationId station, OperationChoice choice = OperationChoice.None)
    {
        EquipmentKind? kind = Scenario.EquipmentAt(station);
        long observation = BaseOperationCommand.NeedsObservation(action) && kind.HasValue ? ObservationOf(kind.Value) : BaseOperationCommand.Unstated;
        return Send(p, BaseOperationCommand.Operation(Guid.NewGuid(), Now.Epoch, action, station, choice, observation));
    }

    /// <summary>Starts an operation and ticks until the station is free again.</summary>
    public void Run(PlayerId p, BaseOperationAction action, StationId station, OperationChoice choice = OperationChoice.None, double maxSeconds = 60)
    {
        CommandResult r = Do(p, action, station, choice);
        Assert.That(r.Accepted, $"{action}/{choice} at {station}: {r.Reason}");
        Until(() => !Now.TryGetOperationAt(station, out _), maxSeconds);
    }

    public CommandResult PlaceOrder(PlayerId p) =>
        Send(p, BaseOperationCommand.PlaceOrder(Guid.NewGuid(), Now.Epoch, Scenario.Logistics.Console, BaseOperationsFixtures.StandardManifest));

    public CommandResult ForOrder(PlayerId p, BaseOperationAction action, long order) =>
        Send(p, BaseOperationCommand.ForOrder(Guid.NewGuid(), Now.Epoch, action, Scenario.Logistics.Console, order));

    public void Tick(double seconds, double dt = 0.25)
    {
        for (double left = seconds; left > 1e-12; left -= dt) Authority.Tick(Math.Min(dt, left));
    }

    public void Until(Func<bool> done, double maxSeconds, double dt = 0.25)
    {
        double start = Time;
        while (!done())
        {
            if (Time - start > maxSeconds) Assert.Fail($"Condition not reached within {maxSeconds} s (t = {Time:0.###}).");
            if (Now.Outpost.State != SessionState.Running) Assert.Fail($"Session not running (state {Now.Outpost.State}).");
            Authority.Tick(dt);
        }
    }

    public void Walk(PlayerId p, params BasePosition[] waypoints)
    {
        foreach (BasePosition target in waypoints)
        {
            for (int guard = 0; ; guard++)
            {
                Assert.That(guard, Is.LessThan(3000), $"{p} could not reach {target} (at {Position(p)})");
                BasePosition at = Position(p);
                double dx = target.X - at.X, dy = target.Y - at.Y, length = Math.Sqrt(dx * dx + dy * dy);
                if (length <= 0.02) break;
                double speed = Scenario.Expedition.Outpost.Movement.MaxSpeedMetersPerSecond
                    * (Crew(p).OnSuitSupply ? Scenario.Expedition.SuitedSpeedFactor : 1d);
                Move(p, MoveDirection.FromAxes(dx / length, dy / length));
                double before = length;
                Authority.Tick(Math.Min(0.1, length / speed));
                if (Now.Outpost.State != SessionState.Running)
                {
                    Move(p, MoveDirection.Zero);
                    return;
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

    public void DoTask(PlayerId p, StationId station, TaskTypeId task, double maxSeconds = 60)
    {
        CommandResult r = Send(p, ExpeditionCommand.StartTask(Guid.NewGuid(), Now.Epoch, station, task, Crew(p).ZoneSequence));
        Assert.That(r.Accepted, $"{task} at {station}: {r.Reason}");
        Until(() => Now.Outpost.TryGetStation(station, out StationState s) && s.ActiveTask == null, maxSeconds);
        Now.Outpost.TryGetStation(station, out StationState end);
        Assert.That(end.LastEnd.Reason, Is.EqualTo(TaskEndReason.Completed), $"{task} ended {end.LastEnd.Reason}");
    }

    public CommandResult Cycle(PlayerId p) =>
        Send(p, ExpeditionCommand.CycleAirlock(Guid.NewGuid(), Now.Epoch, ExpeditionFixtures.InnerPanel, Crew(p).ZoneSequence,
            Now.Expedition.Airlock.CycleSequence));

    /// <summary>Whole sortie of one member: suit checklist, egress, regolith sample, ingress, suit back to Worn.</summary>
    public void Sortie(PlayerId p, bool completeScience = true)
    {
        if (!(Now.Expedition.TryGetSuitOf(p, out SuitState worn) && worn.Status == SuitStatus.Worn))
        {
            Walk(p, AtRackA);
            DoTask(p, ExpeditionFixtures.RackA, ExpeditionFixtures.DonA);
        }
        Walk(p, AtCharger);
        DoTask(p, ExpeditionFixtures.Charger, ExpeditionFixtures.ChargeSuit);
        Walk(p, AtConsumables);
        DoTask(p, ExpeditionFixtures.Consumables, ExpeditionFixtures.FillSuit);
        Walk(p, AtSuitCheck);
        DoTask(p, ExpeditionFixtures.SuitCheck, ExpeditionFixtures.CheckSuit);
        Walk(p, BeforeInnerHatch, ChamberA);
        Assert.That(Cycle(p).Accepted);
        Until(() => Now.Expedition.Airlock.State == AirlockState.DepressurizedOuterOpen, 30);
        Walk(p, OutsideHatch, AtRegolith);
        if (completeScience) DoTask(p, ExpeditionFixtures.RegolithSite, ExpeditionFixtures.CollectRegolith);
        else
        {
            Assert.That(Send(p, ExpeditionCommand.StartTask(Guid.NewGuid(), Now.Epoch, ExpeditionFixtures.RegolithSite,
                ExpeditionFixtures.CollectRegolith, Crew(p).ZoneSequence)).Accepted);
            Tick(1);
            Assert.That(Send(p, ExpeditionCommand.CancelTask(Guid.NewGuid(), Now.Epoch, ExpeditionFixtures.RegolithSite)).Accepted);
            Assert.That(Send(p, ExpeditionCommand.DeclareReturn(Guid.NewGuid(), Now.Epoch)).Accepted);
        }
        Walk(p, OutsideHatch, new BasePosition(14.2, ChamberA.Y), ChamberA);
        Assert.That(Cycle(p).Accepted);
        Until(() => Now.Expedition.Airlock.State == AirlockState.PressurizedInnerOpen, 30);
        Walk(p, BeforeInnerHatch);
    }

    public int CountActivities(ActivityKind kind, ActivityStatus status)
    {
        int n = 0;
        foreach (ActivityRecord a in Now.Activities)
            if (a.Kind == kind && a.Status == status) n++;
        return n;
    }

    public DecisionRecord LastDecision => Now.Decisions[Now.Decisions.Count - 1];
}
