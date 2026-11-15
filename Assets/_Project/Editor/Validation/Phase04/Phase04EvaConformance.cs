using System;
using System.Collections.Generic;
using System.Linq;
using Trainer.Application;
using Trainer.Application.BaseOperations;
using Trainer.Application.Development;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.Eva;
using static Trainer.Tooling.Validation.Phase04.Phase04Checks;

namespace Trainer.Tooling.Validation.Phase04
{
    /// <summary>Two real-authority sorties on the labelled phase 3/4 fixture plan. Held movement and validated commands only.</summary>
    public static class Phase04EvaConformance
    {
        public static string Run(Phase04ImplementationDiscovery discovery)
        {
            var report = new List<string>();
            foreach (bool science in new[] { true, false })
            {
                var scenario = BaseOperationsFixtures.Scenario(null);
                using (var session = Phase04Session.Create(discovery, scenario, Phase04Kinds.AuthorityInMemory))
                {
                    var actor = session.Join(); session.Join(); session.Resume(actor);
                    Walk(session, actor, new BasePosition(1.5, 8.3));
                    Task(session, actor, ExpeditionFixtures.RackA, ExpeditionFixtures.DonA);
                    Walk(session, actor, new BasePosition(5.5, 8.3));
                    Task(session, actor, ExpeditionFixtures.Charger, ExpeditionFixtures.ChargeSuit);
                    Walk(session, actor, new BasePosition(7.5, 8.3));
                    Task(session, actor, ExpeditionFixtures.Consumables, ExpeditionFixtures.FillSuit);
                    Walk(session, actor, new BasePosition(10, 7.2));
                    Task(session, actor, ExpeditionFixtures.SuitCheck, ExpeditionFixtures.CheckSuit);
                    Walk(session, actor, new BasePosition(11.2, 5), new BasePosition(12.9, 4.4));
                    Cycle(session, actor, AirlockState.DepressurizedOuterOpen);
                    Walk(session, actor, new BasePosition(15.8, 5), new BasePosition(21, 5));
                    session.State.Expedition.TryGetCrew(actor.Player, out var outside);
                    Require(outside.OnSuitSupply && outside.Zone == CrewZone.Exterior, "real sealed member on exterior supply");
                    if (science) Task(session, actor, ExpeditionFixtures.RegolithSite, ExpeditionFixtures.CollectRegolith);
                    else
                    {
                        Require(actor.Send(ExpeditionCommand.StartTask(Guid.NewGuid(), session.State.Epoch, ExpeditionFixtures.RegolithSite,
                            ExpeditionFixtures.CollectRegolith, actor.ZoneSequence(session.State))) == OutpostReasons.Accepted, "start science to cancel");
                        session.Advance(1);
                        Require(actor.Send(ExpeditionCommand.CancelTask(Guid.NewGuid(), session.State.Epoch, ExpeditionFixtures.RegolithSite)) == OutpostReasons.Accepted,
                            "cancel unfinished science");
                        Require(actor.Send(ExpeditionCommand.DeclareReturn(Guid.NewGuid(), session.State.Epoch)) == OutpostReasons.Accepted, "declare early return");
                    }
                    Walk(session, actor, new BasePosition(15.8, 5), new BasePosition(14.2, 4.4), new BasePosition(12.9, 4.4));
                    Cycle(session, actor, AirlockState.PressurizedInnerOpen);
                    Walk(session, actor, new BasePosition(11.2, 5));
                    var state = session.State;
                    Require(state.Outpost.State == SessionState.Running && state.Expedition.Phase == ExpeditionPhase.Preparing && state.Epoch == 0,
                        "return keeps the same continuous session and restores Preparing");
                    Require(state.Session.ExpeditionsFinished == 1, "exactly one closed sortie");
                    Require(state.Activities.Count(a => a.Kind == ActivityKind.ScienceAssignment && a.Status == ActivityStatus.Completed) == (science ? 1 : 0),
                        "unfinished science is not credited");
                    Require(state.Activities.Single(a => a.Kind == ActivityKind.Expedition).ResultKey == (science ? "science-returned" : "returned-without-science"),
                        "science and early return have distinct results");
                    if (session.Instance.Eva == null) throw new Phase04CheckPending("no inspectable EVA model from authority construction");
                    var eva = session.Instance.Eva;
                    Require(eva.Ledger.OxygenConsumedKilograms > 0 && eva.Ledger.FeedwaterConsumedKilograms > 0 && eva.Ledger.MakeupGasUsedKilograms > 0,
                        "sortie really consumes suit services and airlock gas");
                    Near(state.SimulatedTimeSeconds, eva.Snapshot.SimulatedTimeSeconds, 1e-8, "EVA and session clock");
                    report.Add($"{(science ? "science completed" : "science cancelled, early return")}: {state.SimulatedTimeSeconds:0.###} s, " +
                        $"suit O2 {eva.Ledger.OxygenConsumedKilograms:0.######} kg, feedwater {eva.Ledger.FeedwaterConsumedKilograms:0.######} kg; Running/Preparing, epoch 0");
                }
            }
            return string.Join("; ", report);
        }

        private static void Task(Phase04Session s, AuthorityActor actor, StationId station, TaskTypeId task)
        {
            Require(actor.Send(ExpeditionCommand.StartTask(Guid.NewGuid(), s.State.Epoch, station, task, actor.ZoneSequence(s.State))) == OutpostReasons.Accepted,
                "start " + task);
            Require(s.AdvanceUntil(state => state.Outpost.TryGetStation(station, out var value) && value.ActiveTask == null, 60), "finish " + task);
            s.State.Outpost.TryGetStation(station, out var end);
            Require(end.LastEnd.Reason == TaskEndReason.Completed, "completed task " + task);
        }

        private static void Cycle(Phase04Session s, AuthorityActor actor, AirlockState target)
        {
            Require(actor.Send(ExpeditionCommand.CycleAirlock(Guid.NewGuid(), s.State.Epoch, ExpeditionFixtures.InnerPanel,
                actor.ZoneSequence(s.State), s.State.Expedition.Airlock.CycleSequence)) == OutpostReasons.Accepted, "airlock cycle");
            Require(s.AdvanceUntil(state => state.Expedition.Airlock.State == target, 30), "airlock reaches " + target);
        }

        private static void Walk(Phase04Session s, AuthorityActor actor, params BasePosition[] waypoints)
        {
            foreach (var target in waypoints)
            {
                for (int guard = 0; ; guard++)
                {
                    Require(guard < 3000, "bounded walk to " + target);
                    s.State.Outpost.TryGetCrew(actor.Player, out var me);
                    double dx = target.X - me.Pose.Position.X, dy = target.Y - me.Pose.Position.Y;
                    double distance = Math.Sqrt(dx * dx + dy * dy);
                    if (distance < .02) break;
                    s.State.Expedition.TryGetCrew(actor.Player, out var crew);
                    double speed = s.Scenario.Expedition.Outpost.Movement.MaxSpeedMetersPerSecond * (crew.OnSuitSupply ? s.Scenario.Expedition.SuitedSpeedFactor : 1);
                    actor.Move(dx / distance, dy / distance);
                    s.Instance.Advance(Math.Min(.1, distance / speed));
                }
                actor.Move(0, 0);
            }
        }
    }
}
