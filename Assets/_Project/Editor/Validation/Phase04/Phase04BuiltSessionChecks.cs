using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text.RegularExpressions;
using Trainer.Application;
using Trainer.Application.BaseOperations;
using Trainer.Application.Crew;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.BaseOperations;
using Trainer.Unity.Configuration;
using Trainer.Unity.Networking;
using Trainer.Unity.Networking.BaseOperations;
using Trainer.Unity.Validation.Phase04;
using UnityEditor;
using SessionState = Trainer.Application.SessionState;

namespace Trainer.Tooling.Validation.Phase04
{
    /// <summary>Opt-in cross-process socket check of the built productive scene. No authority, clock or inventory in the Editor clients.</summary>
    public static class Phase04BuiltSessionChecks
    {
        private const string Evidence = "Logs/phase04-integration/built-session";

        public static void RunFromCommandLine()
        {
            Directory.CreateDirectory(Evidence);
            int exit = 1;
            var checks = new List<string>();
            try
            {
                using (var run = new Run()) run.Verify(checks);
                checks.Add("PASS: productive build authority, two observing Editor socket clients, killed/relaunched built client; not human play.");
                exit = 0;
            }
            catch (Exception e) { checks.Add("FAIL: " + e); UnityEngine.Debug.LogException(e); }
            File.WriteAllLines(Path.Combine(Evidence, "verification.txt"), checks);
            UnityEngine.Debug.Log(string.Join("\n", checks));
            EditorApplication.Exit(exit);
        }

        private sealed class Run : IDisposable
        {
            private readonly List<Process> processes = new List<Process>();
            private readonly List<UnityTransportClient> transports = new List<UnityTransportClient>();
            private readonly List<BaseOperationsConnectionController> clients = new List<BaseOperationsConnectionController>();
            private readonly Stopwatch real = Stopwatch.StartNew();
            private readonly BaseOperationsScenario scenario;
            private readonly string player;
            private const int Port = 7797;

            public Run()
            {
                player = Path.GetFullPath(BuildChecks.Phase04.Phase04BuildChecks.PlayerPath(BuildTarget.StandaloneLinux64));
                if (!File.Exists(player)) throw new InvalidOperationException("Build the Linux candidate first.");
                scenario = AssetDatabase.LoadAssetAtPath<BaseOperationsScenarioDefinition>(BuildChecks.Phase04.Phase04BuildChecks.ScenarioAssetPath).ToScenario();
            }

            private Process Launch(string name, string arguments)
            {
                var p = Process.Start(new ProcessStartInfo(player, arguments + " -logFile \"" + Path.GetFullPath(Path.Combine(Evidence, name + ".log")) + "\"")
                { UseShellExecute = false });
                processes.Add(p);
                return p;
            }

            private BaseOperationsConnectionController Join()
            {
                var t = new UnityTransportClient(new WebSocketClientOptions { Address = "127.0.0.1", Port = Port });
                var c = new BaseOperationsConnectionController(t);
                transports.Add(t); clients.Add(c); c.Connect();
                Until(() => c.State == SessionConnectionState.Connected, "socket join");
                return c;
            }

            private void Pump()
            {
                foreach (var t in transports) { t.Update(); t.Flush(); }
                Thread.Sleep(5);
            }

            private void Until(Func<bool> done, string detail, double timeout = 15)
            {
                double deadline = real.Elapsed.TotalSeconds + timeout;
                while (!done())
                {
                    if (real.Elapsed.TotalSeconds > deadline) throw new InvalidOperationException("Timeout: " + detail);
                    Pump();
                }
            }

            private static BaseOperationsSnapshot State(BaseOperationsConnectionController c)
            {
                if (!c.Session.TryGetBaseOperations(out var state)) throw new InvalidOperationException("No remote snapshot.");
                return state;
            }

            private void Session(BaseOperationsConnectionController c, ExpeditionAction action)
            {
                var command = ExpeditionCommand.Session(Guid.NewGuid(), State(c).Epoch, action);
                CommandResult result = null;
                Action<CommandResult> handler = r => { if (r.CommandId == command.Id) result = r; };
                c.Session.CommandCompleted += handler;
                try { c.Session.Submit(command); Until(() => result != null, action + " answered"); Require(result.Accepted, action + ": " + result.Reason); }
                finally { c.Session.CommandCompleted -= handler; }
            }

            private void WalkTo(BaseOperationsConnectionController c, StationId station)
            {
                var navigator = new Phase04Navigator(scenario.Expedition);
                var input = new CrewInputSender(c.Session);
                var placement = scenario.Layout.Stations.Single(s => s.Id == station);
                State(c).Outpost.TryGetCrew(c.Session.Membership.Player, out var initial);
                var path = navigator.PlanToStation(initial.Pose.Position, station) ?? throw new InvalidOperationException("No station route.");
                int index = 0;
                Until(() =>
                {
                    State(c).Outpost.TryGetCrew(c.Session.Membership.Player, out var crew);
                    var p = crew.Pose.Position;
                    if (placement.IsWithinReach(p)) return true;
                    while (index < path.Count - 1 && p.DistanceTo(path[index]) < .2) index++;
                    double dx = path[index].X - p.X, dy = path[index].Y - p.Y;
                    double distance = Math.Sqrt(dx * dx + dy * dy);
                    input.Update(dx / distance, dy / distance);
                    return false;
                }, "walk to " + station, 30);
                input.Update(0, 0);
            }

            public void Verify(List<string> checks)
            {
                Launch("authority", "-batchmode -nographics -trainer-role authority -trainer-bind 127.0.0.1 -trainer-port " + Port + " -trainer-timescale 4");
                Until(() => File.Exists(Path.Combine(Evidence, "authority.log")) && File.ReadAllText(Path.Combine(Evidence, "authority.log")).Contains("Listening on"), "authority listens", 30);
                var a = Join(); var b = Join();
                Session(a, ExpeditionAction.Resume);
                WalkTo(a, scenario.Logistics.Console);
                using (var intents = new BaseOperationsInteractionClient(a.Session, scenario))
                {
                    string answer = null; intents.Answered += value => answer = value;
                    Require(intents.RequestPlaceOrder(scenario.Logistics.Console, scenario.Manifests[0].Key), "order sent");
                    Until(() => answer != null, "order answered"); Require(answer == OutpostReasons.Accepted, "order " + answer);
                    Until(() => State(a).Orders.Count == 1, "order created");
                }
                long order = State(a).Orders[0].Sequence;
                string clientArguments = "-batchmode -nographics -trainer-role client -trainer-server 127.0.0.1 -trainer-port " + Port
                    + " -trainer-connect -trainer-autopilot continuity -trainer-autopilot-role water -trainer-autopilot-minutes 10";
                Process pilot = Launch("water-killed", clientArguments);
                Until(() => State(a).Operations.Any(o => o.Action == BaseOperationAction.Intervene && o.Choice == OperationChoice.ReplaceProcessorFilter), "real repair starts", 150);
                Session(a, ExpeditionAction.Pause);
                Until(() => State(a).Outpost.State == SessionState.Paused && State(b).Revision == State(a).Revision, "pause seen by both clients");
                BaseOperationsSnapshot during = State(a);
                var operation = during.Operations.Single(o => o.Action == BaseOperationAction.Intervene);
                PlayerId gone = operation.Performer;
                string spare = operation.ReservedSpare;
                int freeDuring = during.FreeSpares(spare);
                double t = during.SimulatedTimeSeconds;
                checks.Add($"repair active before process kill: sim={t.ToString("R", CultureInfo.InvariantCulture)}s performer={gone}, work={operation.WorkSeconds}/{operation.DurationSeconds}, free spares={freeDuring}");
                pilot.Kill(); Require(pilot.WaitForExit(10000), "built client killed");
                Until(() => !State(a).Outpost.TryGetCrew(gone, out _) && !State(a).Operations.Any(o => o.Performer == gone), "dead member and operation released");
                Require(State(a).FreeSpares(spare) == freeDuring + 1, "spare reservation released, not consumed");
                Require(State(a).Orders.Any(o => o.Sequence == order), "order conserved after process death");
                checks.Add("PASS process death during repair: no phantom member/operation, one spare released, order retained.");

                Process replacement = Launch("water-rejoined", clientArguments);
                Until(() => State(a).Outpost.Crew.Any(c => c.Player != a.Session.Membership.Player && c.Player != b.Session.Membership.Player), "new built-client identity");
                Until(() => File.Exists(Path.Combine(Evidence, "water-rejoined.log")) && File.ReadAllText(Path.Combine(Evidence, "water-rejoined.log")).Contains("[Autopilot] status"), "replacement observes state");
                string joinedLog = File.ReadAllText(Path.Combine(Evidence, "water-rejoined.log"));
                Match joined = Regex.Match(joinedLog, @"\[Autopilot\] driver=0\.0s sim=([0-9.]+)s rev=\d+ Water joined");
                Require(joinedLog.Contains("session Paused") && joined.Success
                    && Math.Abs(double.Parse(joined.Groups[1].Value, CultureInfo.InvariantCulture) - t) < .11,
                    "replacement has current paused state, not initial Ready/time zero (summary sim= is elapsed since join)");
                Require(State(a).Orders.Any(o => o.Sequence == order) && State(a).FreeSpares(spare) == freeDuring + 1, "rejoin duplicates neither order nor spare");
                checks.Add("PASS relaunched built client: fresh identity, current paused clock, conserved order and spare. See water-rejoined.log.");
                replacement.Kill(); Require(replacement.WaitForExit(10000), "replacement stopped before independent restart check");
                Until(() => State(a).Outpost.Crew.Count == 2, "only the two observing socket clients remain");
                b.Disconnect();
                Until(() => !State(a).Outpost.TryGetCrew(b.Session.Membership.Player, out _), "observer disconnects");
                b.Connect(); Until(() => b.State == SessionConnectionState.Connected, "observer rejoins");
                Until(() => State(b).Revision == State(a).Revision, "rejoined observer at exact current revision");
                Require(State(b).SimulatedTimeSeconds == t && State(b).Orders.Any(o => o.Sequence == order), "full current snapshot after fresh join");
                long epoch = State(a).Epoch;
                Session(a, ExpeditionAction.Restart);
                Until(() => State(a).Epoch == epoch + 1 && State(b).Epoch == epoch + 1, "new epoch on both socket clients");
                Until(() => State(a).Revision == State(b).Revision, "restart exact revision");
                foreach (var client in new[] { a, b })
                {
                    var s = State(client);
                    Require(s.SimulatedTimeSeconds == 0 && s.Outpost.State == SessionState.Ready, "restart clock and state");
                    Require(s.Orders.Count == 0 && s.Operations.Count == 0, "restart clears activities");
                    Require(s.Outpost.Resources.LifeSupport.Water.PotableKilograms == scenario.Expedition.Outpost.Simulation.LifeSupport.InitialWater.PotableKilograms, "exact initial water, no duplication");
                    Require(s.Outpost.Resources.LifeSupport.Food.StoredKilograms == scenario.Expedition.Outpost.Simulation.LifeSupport.InitialFood.StoredKilograms, "exact initial food");
                    foreach (var stock in scenario.Equipment.InitialSpares) Require(s.FreeSpares(stock.Key) == stock.Count, "exact initial spare " + stock.Key);
                }
                checks.Add($"PASS restart: epoch {epoch} -> {State(a).Epoch}, both remote clients at revision {State(a).Revision}; initial stocks once. Real {real.Elapsed.TotalSeconds:0.0}s; authority time scale 4.");
            }

            private static void Require(bool value, string detail) { if (!value) throw new InvalidOperationException(detail); }
            public void Dispose()
            {
                foreach (var c in clients) c.Dispose();
                foreach (var t in transports) t.Dispose();
                foreach (var p in processes) { if (!p.HasExited) { p.Kill(); p.WaitForExit(10000); } p.Dispose(); }
            }
        }
    }
}
