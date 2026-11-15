using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Trainer.Application;
using Trainer.Application.Crew;
using Trainer.Application.Development;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.Eva;
using Trainer.Simulation.LifeSupport;
using Trainer.Unity.Networking;
using Trainer.Unity.Networking.Expedition;
using H = Phase03AuthorityHarness;

/// <summary>
/// D3 phase 3 networking end to end IN MEMORY: the REAL expedition authority (D2) with D1/D3 models, the phase 3
/// bridge, cable 'T' 'E', client adapter/controller and host-local port, over D3's in-memory transport. No sockets or
/// processes (see the PlayMode socket tests and the build acceptance for those).
/// </summary>
public sealed class Phase03NetworkLoopbackTests
{
    private sealed class Rig : IDisposable
    {
        public readonly ExpeditionScenario Scenario;
        public readonly ExpeditionAuthority Authority;
        public readonly InMemoryOutpostNetwork Net = new InMemoryOutpostNetwork();
        public readonly ExpeditionCommandBridge Bridge;
        public readonly List<string> HostErrors = new List<string>();
        private readonly List<IDisposable> owned = new List<IDisposable>();

        public Rig(ExpeditionScenario scenario = null)
        {
            Scenario = scenario ?? ExpeditionFixtures.Scenario(false);
            Authority = new ExpeditionAuthority(Scenario, new OutpostBaseSimulation(OutpostSimulationParameters.SyntheticTraining()),
                new EvaSuitSimulation(), CrewRoster.For(Scenario.Outpost));
            Bridge = new ExpeditionCommandBridge(Authority, Net.HostTransport);
            Bridge.ProtocolError += (c, code) => HostErrors.Add(code);
        }

        public ExpeditionConnectionController Client()
        {
            var c = new ExpeditionConnectionController(Net.CreateClient());
            owned.Add(c);
            return c;
        }

        public ExpeditionConnectionController Connected()
        {
            ExpeditionConnectionController c = Client();
            c.Connect();
            Pump();
            Assert.That(c.State, Is.EqualTo(SessionConnectionState.Connected));
            return c;
        }

        public LocalExpeditionSessionPort Local()
        {
            var p = new LocalExpeditionSessionPort(Authority);
            owned.Add(p);
            Assert.That(p.Join().IsJoined);
            return p;
        }

        public void Pump()
        {
            for (int i = 0; i < 3; i++)
            {
                Bridge.FlushSnapshots();
                Net.Pump();
            }
        }

        public void Step(double seconds, double dt = 0.1)
        {
            for (double left = seconds; left > 1e-12; left -= dt)
            {
                Authority.Tick(Math.Min(dt, left));
                Pump();
            }
        }

        public void Dispose()
        {
            foreach (IDisposable d in owned) d.Dispose();
            Bridge.Dispose();
        }
    }

    /// <summary>Drives one participant only through its port (held directions and stamped intents), like a player.</summary>
    private sealed class Driver
    {
        private readonly Rig rig;
        public readonly IExpeditionSessionPort Port;
        private long sequence;
        private readonly Dictionary<Guid, CommandResult> results = new Dictionary<Guid, CommandResult>();
        public readonly List<string> Abandoned = new List<string>();

        public Driver(Rig rig, IExpeditionSessionPort port)
        {
            this.rig = rig;
            Port = port;
            port.CommandCompleted += r => results[r.CommandId] = r;
            port.CommandAbandoned += (c, reason) => Abandoned.Add(reason);
            port.ExpeditionCommandAbandoned += (c, reason) => Abandoned.Add(reason);
        }

        public PlayerId Me => Port.Membership.Player;
        public ExpeditionSessionSnapshot State
        {
            get
            {
                Assert.That(Port.TryGetExpedition(out ExpeditionSessionSnapshot s));
                return s;
            }
        }
        public ExpeditionCrewState Crew => State.Crew.Single(c => c.Player == Me);
        public BasePosition Position => State.Outpost.Crew.Single(c => c.Player == Me).Pose.Position;

        public void Move(MoveDirection direction) => Port.SubmitMovement(new MovementInput(State.Epoch, ++sequence, direction));

        public CommandResult Send(ExpeditionCommand command, bool pump = true)
        {
            Port.Submit(command);
            if (!pump) return null;
            rig.Pump();
            Assert.That(results.TryGetValue(command.Id, out CommandResult r), "answered");
            return r;
        }

        public CommandResult Result(Guid id) => results.TryGetValue(id, out CommandResult r) ? r : null;

        public ExpeditionCommand StartTaskCommand(StationId station, TaskTypeId task) =>
            ExpeditionCommand.StartTask(Guid.NewGuid(), State.Epoch, station, task, Crew.ZoneSequence);

        public ExpeditionCommand CycleCommand(StationId panel) =>
            ExpeditionCommand.CycleAirlock(Guid.NewGuid(), State.Epoch, panel, Crew.ZoneSequence, State.Airlock.CycleSequence);

        public void Walk(params BasePosition[] targets)
        {
            foreach (BasePosition target in targets)
                for (int guard = 0; ; guard++)
                {
                    Assert.That(guard, Is.LessThan(3000), $"{Me} stuck at {Position} going to {target}");
                    double dx = target.X - Position.X, dy = target.Y - Position.Y, d = Math.Sqrt(dx * dx + dy * dy);
                    if (d <= 0.02) { Move(MoveDirection.Zero); rig.Pump(); break; }
                    double speed = rig.Scenario.Outpost.Movement.MaxSpeedMetersPerSecond * (Crew.OnSuitSupply ? rig.Scenario.SuitedSpeedFactor : 1);
                    Move(MoveDirection.FromAxes(dx / d, dy / d));
                    rig.Pump();
                    rig.Step(Math.Min(0.1, d / speed), 0.1);
                }
        }

        public void DoTask(StationId station, TaskTypeId task)
        {
            CommandResult r = Send(StartTaskCommand(station, task));
            Assert.That(r.Accepted, $"{task}: {r.Reason}");
            for (int i = 0; i < 2000 && State.Outpost.Stations.Single(s => s.Station == station).ActiveTask != null; i++) rig.Step(0.1);
            Assert.That(State.Outpost.Stations.Single(s => s.Station == station).LastEnd.Reason, Is.EqualTo(TaskEndReason.Completed));
        }

        public void PrepareSuit(bool rackA)
        {
            Walk(rackA ? H.AtRackA : H.AtRackB);
            DoTask(rackA ? ExpeditionFixtures.RackA : ExpeditionFixtures.RackB, rackA ? ExpeditionFixtures.DonA : ExpeditionFixtures.DonB);
            Walk(H.AtCharger);
            DoTask(ExpeditionFixtures.Charger, ExpeditionFixtures.ChargeSuit);
            Walk(H.AtConsumables);
            DoTask(ExpeditionFixtures.Consumables, ExpeditionFixtures.FillSuit);
            Walk(H.AtSuitCheck);
            DoTask(ExpeditionFixtures.SuitCheck, ExpeditionFixtures.CheckSuit);
        }
    }

    private static CommandResult Resume(Driver d) =>
        d.Send(ExpeditionCommand.Session(Guid.NewGuid(), d.State.Epoch, ExpeditionAction.Resume));

    // ---- deep comparison of published state (exact doubles: the cable is binary) ----

    private static string Fingerprint(object value)
    {
        var sb = new StringBuilder();
        Dump(value, sb, 0);
        return sb.ToString();
    }

    private static void Dump(object value, StringBuilder sb, int depth)
    {
        if (depth > 12) { sb.Append("…"); return; }
        if (value == null) { sb.Append("null"); return; }
        Type t = value.GetType();
        if (t.IsPrimitive || t.IsEnum || value is string || value is decimal || value is Guid)
        {
            sb.Append(value is double d ? d.ToString("R") : value.ToString());
            return;
        }
        if (value is IEnumerable list)
        {
            sb.Append('[');
            foreach (object item in list) { Dump(item, sb, depth + 1); sb.Append(','); }
            sb.Append(']');
            return;
        }
        sb.Append(t.Name).Append('{');
        foreach (PropertyInfo p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance).OrderBy(p => p.Name))
        {
            if (p.GetIndexParameters().Length > 0) continue;
            sb.Append(p.Name).Append('=');
            Dump(p.GetValue(value), sb, depth + 1);
            sb.Append(';');
        }
        sb.Append('}');
    }

    [Test]
    public void RemoteSnapshotsCarryTheWholePhase3StateExactly()
    {
        using (var rig = new Rig(H.With(ExpeditionFixtures.Scenario(false), episode: ExpeditionFixtures.Episode())))
        {
            var ana = new Driver(rig, rig.Local());
            var leo = new Driver(rig, rig.Connected().Session);
            Assert.That(Resume(ana).Accepted);
            ana.PrepareSuit(true);
            ana.Walk(H.BeforeInnerHatch, H.ChamberA);
            Assert.That(ana.Send(ana.CycleCommand(ExpeditionFixtures.InnerPanel)).Accepted);
            for (int i = 0; i < 400 && !ana.State.Instruction.InForce; i++) rig.Step(0.1); // warning at 45 s, during the EVA
            Assert.That(leo.State.Instruction.InForce);
            Assert.That(leo.State.Revision, Is.EqualTo(ana.State.Revision));
            Assert.That(Fingerprint(leo.State), Is.EqualTo(Fingerprint(ana.State)),
                "the remote client holds exactly the authority's state: suits, margins, airlock, advisories, receptions");
            Assert.That(leo.State.Crew.Single(c => c.Player == ana.Me).Margin.Status, Is.Not.EqualTo(MarginStatus.NotApplicable));
        }
    }

    [Test]
    public void ALateJoinerReceivesTheFullStateBeforeItsMembership()
    {
        using (var rig = new Rig(H.With(ExpeditionFixtures.Scenario(false), episode: ExpeditionFixtures.Episode())))
        {
            var ana = new Driver(rig, rig.Local());
            Assert.That(Resume(ana).Accepted);
            rig.Step(50);
            ExpeditionConnectionController late = rig.Client();
            bool hadStateWhenJoined = false;
            late.Session.MembershipChanged += m =>
            {
                if (m.IsJoined) hadStateWhenJoined = late.Session.TryGetExpedition(out ExpeditionSessionSnapshot s) && s.TryGetCrew(m.Player, out _);
            };
            late.Connect();
            rig.Pump();
            Assert.That(hadStateWhenJoined, "Joined only with a snapshot that contains the newcomer");
            var leo = new Driver(rig, late.Session);
            Assert.That(leo.State.TryGetReception(ExpeditionFixtures.WarningId, leo.Me, out AdvisoryReception r), "current advisories reach the newcomer");
            Assert.That(r.ReceivedAtSeconds, Is.EqualTo(leo.State.SimulatedTimeSeconds).Within(1e-9));
        }
    }

    [Test]
    public void TwoSimultaneousAirlockRequestsFromTwoClientsRunOneCycle()
    {
        using (var rig = new Rig())
        {
            var ana = new Driver(rig, rig.Connected().Session);
            var leo = new Driver(rig, rig.Connected().Session);
            Assert.That(Resume(ana).Accepted);
            ana.Walk(H.BeforeInnerHatch);
            leo.Walk(new BasePosition(11.4, 5.6)); // also within reach of the inner panel, clear of the doorway
            ExpeditionCommand a = ana.CycleCommand(ExpeditionFixtures.InnerPanel), b = leo.CycleCommand(ExpeditionFixtures.InnerPanel);
            ana.Send(a, pump: false);
            leo.Send(b, pump: false);
            rig.Pump();
            CommandResult ra = ana.Result(a.Id), rb = leo.Result(b.Id);
            Assert.That(ra.Accepted ^ rb.Accepted, "exactly one is accepted");
            Assert.That((ra.Accepted ? rb : ra).Reason, Is.EqualTo(ExpeditionReasons.StaleAirlockState));
            Assert.That(ana.State.Airlock.CycleSequence, Is.EqualTo(1));
            Assert.That(leo.State.Airlock.CycleSequence, Is.EqualTo(1));
        }
    }

    [Test]
    public void AnIntentInFlightDuringAZoneChangeIsRefusedAsStale()
    {
        using (var rig = new Rig())
        {
            var ana = new Driver(rig, rig.Connected().Session);
            Assert.That(Resume(ana).Accepted);
            ana.Walk(H.BeforeInnerHatch, new BasePosition(12.0, 5.0));
            ana.Move(MoveDirection.FromAxes(1, 0));
            rig.Pump(); // the held direction reaches the authority
            ExpeditionCommand cycle = ana.CycleCommand(ExpeditionFixtures.InnerPanel); // stamped with the Interior zone
            ana.Send(cycle, pump: false);
            for (int i = 0; i < 10; i++) rig.Authority.Tick(0.1); // she steps into the chamber before the intent arrives
            rig.Pump();
            Assert.That(ana.Crew.Zone, Is.EqualTo(CrewZone.Airlock));
            Assert.That(ana.Result(cycle.Id).Reason, Is.EqualTo(ExpeditionReasons.StaleZone));
            Assert.That(ana.State.Airlock.CycleSequence, Is.EqualTo(0));
        }
    }

    [Test]
    public void DisconnectingOutsideLeavesNoGhostAndTheOtherClientSeesTheIncident()
    {
        using (var rig = new Rig())
        {
            ExpeditionConnectionController anaLink = rig.Connected();
            var ana = new Driver(rig, anaLink.Session);
            var leo = new Driver(rig, rig.Connected().Session);
            Assert.That(Resume(ana).Accepted);
            ana.PrepareSuit(true);
            ana.Walk(H.BeforeInnerHatch, H.ChamberA);
            Assert.That(ana.Send(ana.CycleCommand(ExpeditionFixtures.InnerPanel)).Accepted);
            for (int i = 0; i < 200 && !ana.State.Airlock.OuterHatchOpen; i++) rig.Step(0.1);
            ana.Walk(H.OutsideHatch, H.AtRegolith);
            PlayerId gone = ana.Me;
            ana.Send(ana.StartTaskCommand(ExpeditionFixtures.RegolithSite, ExpeditionFixtures.CollectRegolith), pump: false);
            anaLink.Disconnect(); // pending intent abandoned: result unknown
            rig.Pump();
            Assert.That(ana.Abandoned, Is.EqualTo(new[] { OutpostReasons.LocalDisconnect }));
            rig.Step(1);
            ExpeditionSessionSnapshot seen = leo.State;
            Assert.That(seen.TryGetCrew(gone, out _), Is.False, "no ghost on the other client");
            Assert.That(seen.Suits[0].Status, Is.EqualTo(SuitStatus.Abandoned));
            Assert.That(seen.Incidents.Single().Kind, Is.EqualTo(IncidentKind.DisconnectedWhileSealed));
            Assert.That(seen.Outpost.Stations.All(s => s.ActiveTask == null), "no phantom reservation");
            SuitLevels frozen = seen.Suits[0].Reading.Levels;
            rig.Step(5);
            Assert.That(leo.State.Suits[0].Reading.Levels, Is.EqualTo(frozen), "nobody keeps consuming in a divergent copy");

            // Reconnection: a new identity, no duplicate.
            anaLink.Connect();
            rig.Pump();
            Assert.That(anaLink.Session.Membership.Player, Is.Not.EqualTo(gone));
            Assert.That(leo.State.Crew.Count, Is.EqualTo(2));
        }
    }

    [Test]
    public void PauseAndRestartThroughPhase2CommandsAreCleanOverTheNetwork()
    {
        using (var rig = new Rig())
        {
            var ana = new Driver(rig, rig.Connected().Session);
            var leo = new Driver(rig, rig.Connected().Session);
            CommandResult result = null;
            ana.Port.CommandCompleted += r => result = r;
            ana.Port.Submit(new OutpostCommand(Guid.NewGuid(), 0, OutpostAction.Resume)); // phase 2 controls through the phase 3 port
            rig.Pump();
            Assert.That(result.Accepted);
            rig.Step(3);
            ana.Port.Submit(new OutpostCommand(Guid.NewGuid(), 0, OutpostAction.Pause));
            rig.Pump();
            double paused = leo.State.SimulatedTimeSeconds;
            rig.Step(3);
            Assert.That(leo.State.SimulatedTimeSeconds, Is.EqualTo(paused), "no client clock: time stops everywhere");
            var old = OutpostCommand.StartTask(Guid.NewGuid(), 0, ExpeditionFixtures.RackA, ExpeditionFixtures.DonA);
            ana.Port.Submit(new OutpostCommand(Guid.NewGuid(), 0, OutpostAction.Restart));
            rig.Pump();
            Assert.That(leo.State.Epoch, Is.EqualTo(1));
            Assert.That(leo.State.Revision, Is.EqualTo(0), "Restart starts the new epoch at revision 0");
            ana.Port.Submit(old);
            rig.Pump();
            Assert.That(result.Reason, Is.EqualTo(OutpostReasons.EpochMismatch), "an intent of the old epoch is refused");
            Assert.That(leo.State.Outpost.State, Is.EqualTo(SessionState.Ready));
        }
    }

    [Test]
    public void ForeignOrMalformedCableMessagesAreRejectedOnBothSides()
    {
        using (var rig = new Rig())
        {
            ExpeditionConnectionController link = rig.Connected();
            ulong connection = rig.Net.HostTransport.Links.Keys.Single();
            rig.Net.HostTransport.Inject(connection, new byte[] { 0x54, 0x4F, 1, 1 }); // a phase 2 Join
            rig.Net.HostTransport.Inject(connection, new byte[] { 0x54, 0x45, 1, 2, 1, 2 }); // truncated phase 3 Submit
            rig.Net.HostTransport.Inject(connection, new byte[] { 0x54, 0x45, 1, 11 }); // a client cannot send snapshots
            rig.Pump();
            Assert.That(rig.HostErrors, Is.EqualTo(new[] { SessionNetworkCodes.BadMagic, SessionNetworkCodes.MalformedPayload,
                SessionNetworkCodes.UnexpectedMessageKind }));
            var clientErrors = new List<string>();
            link.Session.ProtocolError += clientErrors.Add;
            rig.Net.HostTransport.SendToClient(connection, new byte[] { 0x54, 0x4F, 1, 11 }); // phase 2 snapshot header
            rig.Net.HostTransport.SendToClient(connection, new byte[] { 0x54, 0x45, 1, 11, 0 }); // truncated phase 3 snapshot
            rig.Net.Pump();
            Assert.That(clientErrors, Is.EqualTo(new[] { SessionNetworkCodes.BadMagic, SessionNetworkCodes.MalformedPayload }));
            Assert.That(link.State, Is.EqualTo(SessionConnectionState.Connected), "dropped messages never invent state");
        }
    }
}
