using System;
using System.Collections.Generic;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.BaseOperations;
using Trainer.Application.Crew;
using Trainer.Application.Development;
using Trainer.Application.Expedition;
using Trainer.Application.Incidents;
using Trainer.Application.Outpost;
using Trainer.Simulation;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.Eva;
using Trainer.Simulation.LifeSupport;

namespace Trainer.Tests.Phase05.Application
{
    /// <summary>
    /// Test driver of the REAL phase 5 stack: D2 <see cref="LivingBaseAuthority"/> over the phase 4 authority + D1 models + D3
    /// roster, on the labelled synthetic fixture scenario. It acts like players (held directions, commands stamped with what the
    /// snapshot showed) and drives the single clock. It decides nothing itself.
    /// </summary>
    public sealed class Phase05LivingBaseHarness
    {
        public readonly LivingBaseScenario Scenario;
        public readonly OutpostBaseSimulation Base;
        public readonly BaseEquipmentSimulation Equipment;
        public readonly LivingBaseAuthority Authority;
        public readonly List<LivingBaseSnapshot> Published = new List<LivingBaseSnapshot>();
        public readonly List<(PlayerId Sender, CommandResult Result)> Results = new List<(PlayerId, CommandResult)>();
        private readonly Dictionary<PlayerId, long> sequences = new Dictionary<PlayerId, long>();

        public static readonly BasePosition AtPower = new BasePosition(8.0, 5.4);
        public static readonly BasePosition AtWater = new BasePosition(10, 2.3);
        public static readonly BasePosition AtRackA = new BasePosition(1.5, 8.3);
        public static readonly BasePosition AtCharger = new BasePosition(5.5, 8.3);
        public static readonly BasePosition AtConsumables = new BasePosition(7.5, 8.3);
        public static readonly BasePosition AtSuitCheck = new BasePosition(10, 7.2);
        public static readonly BasePosition BeforeInnerHatch = new BasePosition(11.2, 5.0);
        public static readonly BasePosition ChamberA = new BasePosition(12.9, 4.4);
        public static readonly BasePosition OutsideHatch = new BasePosition(15.8, 5.0);
        public static readonly BasePosition AtRegolith = new BasePosition(21, 5);
        public static readonly BasePosition AtShelterPanel = new BasePosition(1.5, 1.6);

        public Phase05LivingBaseHarness(LivingBaseScenario scenario = null, LivingBaseSetup setup = null, string problemKey = null)
        {
            Scenario = scenario ?? LivingBaseFixtures.Scenario();
            Base = new OutpostBaseSimulation(OutpostSimulationParameters.SyntheticTraining());
            Equipment = new BaseEquipmentSimulation();
            Authority = new LivingBaseAuthority(Scenario, setup ?? LivingBaseFixtures.SyntheticSetup(), Base, Equipment, new EvaSuitSimulation(),
                CrewRoster.For(Scenario.BaseOperations.Expedition.Outpost), problemKey);
            Authority.SnapshotChanged += s => Published.Add(s);
            Authority.CommandCompleted += (sender, result) => Results.Add((sender, result));
        }

        /// <summary>The fixture scenario with a replaced catalog, director rules, templates or upgrade (others unchanged).</summary>
        public static LivingBaseScenario Variant(IncidentDefinition[] catalog = null, DirectorRules director = null,
            AssignmentTemplate[] templates = null, AssignmentRules board = null, UpgradeDefinition upgrade = null,
            SpaceWeatherEpisodeDefinition historical = null)
        {
            LivingBaseScenario f = LivingBaseFixtures.Scenario();
            return new LivingBaseScenario(f.BaseOperations, f.PowerStation, catalog ?? LivingBaseFixtures.Catalog(), director ?? f.Director,
                f.PowerOperations, templates ?? new List<AssignmentTemplate>(f.Templates).ToArray(), board ?? f.Assignments, upgrade ?? f.Upgrade,
                historical, f.SyntheticEpisodes, f.Provenance);
        }

        /// <summary>Director that proposes at every evaluation (pressure 1), short grace and rest: deterministic first pick.</summary>
        public static DirectorRules Eager(double grace = 5, double rest = 30, double interval = 10, int maxSimultaneous = 1) =>
            new DirectorRules(interval, grace, rest, maxSimultaneous, 1.0, 0.25, 5);

        public static IncidentDefinition Only(string key)
        {
            foreach (IncidentDefinition d in LivingBaseFixtures.Catalog()) if (d.Key == key) return d;
            throw new ArgumentException(key);
        }

        public LivingBaseSnapshot Now
        {
            get
            {
                Assert.That(Authority.TryGetSnapshot(out LivingBaseSnapshot s), Is.True);
                return s;
            }
        }

        public BaseOperationsSnapshot Phase4 => Now.BaseOperations;
        public double Time => Now.SimulatedTimeSeconds;

        public PlayerId Join()
        {
            OutpostMembership m = Authority.Join();
            Assert.That(m.IsJoined, m.Reason);
            return m.Player;
        }

        public PlayerId JoinAndStart()
        {
            PlayerId p = Join();
            Assert.That(Session(p, ExpeditionAction.Resume).Accepted);
            return p;
        }

        public ExpeditionCrewState Crew(PlayerId p)
        {
            Assert.That(Phase4.Expedition.TryGetCrew(p, out ExpeditionCrewState c), p + " present");
            return c;
        }

        public BasePosition Position(PlayerId p)
        {
            Assert.That(Phase4.Outpost.TryGetCrew(p, out CrewMemberState c), p + " present");
            return c.Pose.Position;
        }

        public CommandResult Last(Guid id)
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

        public CommandResult Send(PlayerId p, IncidentCommand command)
        {
            Authority.Submit(p, command);
            return Last(command.Id);
        }

        public CommandResult Session(PlayerId p, ExpeditionAction action) =>
            Send(p, ExpeditionCommand.Session(Guid.NewGuid(), Now.Epoch, action));

        public IncidentCommand Power(PlayerId p, IncidentAction action, long observation = 0) =>
            new IncidentCommand(Guid.NewGuid(), Now.Epoch, action, LivingBaseFixtures.PowerPanel, 0,
                IncidentCommand.NeedsObservation(action) ? (observation > 0 ? observation : Now.Power.Observation?.Sequence ?? 1) : 0,
                Crew(p).ZoneSequence);

        public CommandResult DoPower(PlayerId p, IncidentAction action) => Send(p, Power(p, action));

        public IncidentCommand AtStation(PlayerId p, IncidentAction action, StationId station) =>
            new IncidentCommand(Guid.NewGuid(), Now.Epoch, action, station, 0, 0, Crew(p).ZoneSequence);

        public CommandResult Assignment(PlayerId p, IncidentAction action, long sequence) =>
            Send(p, new IncidentCommand(Guid.NewGuid(), Now.Epoch, action, StationId.None, sequence, 0, 0));

        /// <summary>Runs a timed phase 5 operation to its end (the station is free again).</summary>
        public void RunPower(PlayerId p, IncidentAction action, double maxSeconds = 60)
        {
            CommandResult r = DoPower(p, action);
            Assert.That(r.Accepted, $"{action}: {r.Reason}");
            Until(() => Now.Operations.Count == 0, maxSeconds);
        }

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
                if (Phase4.Outpost.State != SessionState.Running) Assert.Fail($"Session not running (state {Phase4.Outpost.State}).");
                Authority.Tick(dt);
            }
        }

        public IncidentRecord OpenIncident(IncidentType type)
        {
            foreach (IncidentRecord i in Now.Incidents) if (i.IsOpen && i.Kind == type) return i;
            return null;
        }

        public IncidentRecord LatestIncident(IncidentType type)
        {
            IncidentRecord found = null;
            foreach (IncidentRecord i in Now.Incidents) if (i.Kind == type) found = i;
            return found;
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
                    var movement = Scenario.BaseOperations.Expedition.Outpost.Movement;
                    double speed = movement.MaxSpeedMetersPerSecond * (Crew(p).OnSuitSupply ? Scenario.BaseOperations.Expedition.SuitedSpeedFactor : 1d);
                    Move(p, MoveDirection.FromAxes(dx / length, dy / length));
                    double before = length;
                    Authority.Tick(Math.Min(0.1, length / speed));
                    if (Phase4.Outpost.State != SessionState.Running)
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
            Until(() => Phase4.Outpost.TryGetStation(station, out StationState s) && s.ActiveTask == null, maxSeconds);
            Phase4.Outpost.TryGetStation(station, out StationState end);
            Assert.That(end.LastEnd.Reason, Is.EqualTo(TaskEndReason.Completed), $"{task} ended {end.LastEnd.Reason}");
        }

        public CommandResult Cycle(PlayerId p) =>
            Send(p, ExpeditionCommand.CycleAirlock(Guid.NewGuid(), Now.Epoch, ExpeditionFixtures.InnerPanel, Crew(p).ZoneSequence,
                Phase4.Expedition.Airlock.CycleSequence));

        /// <summary>Whole sortie of one member (as phase 4): suit checklist, egress, regolith sample, ingress.</summary>
        public void Sortie(PlayerId p)
        {
            if (!(Phase4.Expedition.TryGetSuitOf(p, out SuitState worn) && worn.Status == SuitStatus.Worn))
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
            Until(() => Phase4.Expedition.Airlock.State == AirlockState.DepressurizedOuterOpen, 30);
            Walk(p, OutsideHatch, AtRegolith);
            DoTask(p, ExpeditionFixtures.RegolithSite, ExpeditionFixtures.CollectRegolith);
            Walk(p, OutsideHatch, new BasePosition(14.2, ChamberA.Y), ChamberA);
            Assert.That(Cycle(p).Accepted);
            Until(() => Phase4.Expedition.Airlock.State == AirlockState.PressurizedInnerOpen, 30);
            Walk(p, BeforeInnerHatch);
        }
    }
}
