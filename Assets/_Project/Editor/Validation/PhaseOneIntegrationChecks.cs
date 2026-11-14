using System;
using System.Collections.Generic;
using System.Text;
using Trainer.Application;
using Trainer.Simulation;
using Trainer.Tooling.BuildChecks;
using Trainer.Unity.Configuration;
using Trainer.Unity.Networking;

namespace Trainer.Tooling.Validation
{
    /// <summary>Evidence level of a check. Never report a lower level as a higher one.</summary>
    public static class CheckKinds
    {
        /// <summary>Real MissionSession + PowerSystem in one process, no transport.</summary>
        public const string RealSession = "real-session";
        /// <summary>Real MissionSession + PowerSystem + NetworkCommandBridge + SessionConnectionController over an
        /// in-memory loopback written for these checks. Proves wiring and protocol, NOT a network.</summary>
        public const string Loopback = "loopback";
        /// <summary>Two processes over the approved SDK. Not available until an SDK is approved.</summary>
        public const string RealConnection = "real-connection";
        public const string Design = "design-review";
    }

    /// <summary>
    /// Phase 1 checks against the integrated components (D1 PowerSystem, D2 MissionSession, D3 bridge/adapter),
    /// consumed only through their public contracts. Nothing here re-implements a teammate's component:
    /// the loopback below is a test transport for D3's SDK-neutral seams, like D3's own tests use.
    /// </summary>
    public static class PhaseOneIntegrationChecks
    {
        private sealed class CheckFailed : Exception { public CheckFailed(string message) : base(message) { } }

        private static void Require(bool condition, string message) { if (!condition) throw new CheckFailed(message); }

        private static CheckOutcome Run(string name, string kind, Func<string> body)
        {
            try { return new CheckOutcome(name, kind, CheckStatus.Passed, body()); }
            catch (CheckFailed failure) { return new CheckOutcome(name, kind, CheckStatus.Failed, failure.Message); }
            catch (Exception exception) { return new CheckOutcome(name, kind, CheckStatus.Failed, exception.GetType().Name + ": " + exception.Message); }
        }

        private static MissionSnapshot Current(ISessionPort port)
        {
            MissionSnapshot snapshot;
            Require(port.TryGetSnapshot(out snapshot) && snapshot != null, "port has no snapshot");
            return snapshot;
        }

        private static MissionSession NewSession(PhaseOneFixture fixture)
        {
            return new MissionSession(new PowerSystem(), fixture.Configuration, fixture.Rules, MissionSession.DefaultMaxStepSeconds);
        }

        // ------------------------------------------------------------------ real session, no transport

        /// <summary>Applies one fixture step to the authority; returns the command result (null for Tick).</summary>
        private static CommandResult Apply(MissionSession session, FixtureStep step, List<CommandResult> results)
        {
            if (step.Kind == FixtureStepKind.Tick) { session.Tick(step.Seconds); return null; }
            SessionAction action = step.Kind == FixtureStepKind.Resume ? SessionAction.Resume
                : step.Kind == FixtureStepKind.Pause ? SessionAction.Pause
                : step.Kind == FixtureStepKind.Restart ? SessionAction.Restart : SessionAction.SetScienceEnabled;
            bool? value = step.Kind == FixtureStepKind.Science ? (bool?)step.Science : null;
            int before = results.Count;
            session.Submit(new SessionCommand(Guid.NewGuid(), Current(session).Epoch, action, value));
            Require(results.Count == before + 1, step + ": expected one CommandCompleted");
            return results[results.Count - 1];
        }

        public static List<CheckOutcome> RunRealSessionChecks()
        {
            var outcomes = new List<CheckOutcome>();
            foreach (PhaseOneFixture fixture in PhaseOneFixtures.All())
            {
                PhaseOneFixture f = fixture;
                outcomes.Add(Run("Fixture_" + f.Name, CheckKinds.RealSession, () =>
                {
                    RequireNoConfigErrors(f.Configuration);
                    MissionSession session = NewSession(f);
                    var results = new List<CommandResult>();
                    var published = new List<MissionSnapshot>();
                    session.CommandCompleted += results.Add;
                    session.SnapshotChanged += published.Add;
                    MissionSnapshot previous = Current(session);
                    string mismatch = f.Expected[0].Compare(previous);
                    Require(mismatch == null, "initial: " + mismatch);
                    int warnings = 0;
                    for (int i = 0; i < f.Steps.Length; i++)
                    {
                        CommandResult result = Apply(session, f.Steps[i], results);
                        if (result != null) Require(result.Accepted, f.Steps[i] + " rejected: " + result.Reason);
                        MissionSnapshot next = Current(session);
                        mismatch = f.Expected[i + 1].Compare(next);
                        Require(mismatch == null, "after " + f.Steps[i] + ": " + mismatch);
                        if (!ReferenceEquals(next, previous))
                        {
                            ValidationReport report = PhaseOneScenarioValidator.ValidateMissionTransition(previous, next, f.Configuration);
                            Require(!report.HasErrors, "validator after " + f.Steps[i] + ": " + report);
                            warnings += report.Issues.Count;
                        }
                        previous = next;
                    }
                    Require(session.Outcome == f.FinalOutcome, "outcome " + session.Outcome + " != " + f.FinalOutcome);
                    return f.Purpose + "; " + f.Steps.Length + " steps match the reference; " + published.Count
                        + " snapshots; validator warnings " + warnings;
                }));
            }

            PhaseOneFixture s1 = PhaseOneFixtures.BalanceObjectiveRestart();

            outcomes.Add(Run("RepeatedCommandAppliedOnce", CheckKinds.RealSession, () =>
            {
                MissionSession session = NewSession(s1);
                var results = new List<CommandResult>();
                int published = 0;
                session.CommandCompleted += results.Add;
                session.SnapshotChanged += s => published++;
                var command = new SessionCommand(Guid.NewGuid(), 0, SessionAction.SetScienceEnabled, true);
                session.Submit(command);
                MissionSnapshot after = Current(session);
                session.Submit(new SessionCommand(command.Id, 0, SessionAction.SetScienceEnabled, true));
                Require(results.Count == 2 && published == 1, "replay published or completed wrongly");
                Require(!PhaseOneScenarioValidator.ValidateReplay(results[0], results[1]).HasErrors, "replay result differs");
                Require(ReferenceEquals(Current(session), after) && after.Revision == 1, "replay changed state");
                session.Submit(new SessionCommand(command.Id, 0, SessionAction.Pause));
                Require(results[2].Reason == "CommandIdConflict" && ReferenceEquals(Current(session), after), "conflict not rejected");
                return "same result, one snapshot; conflicting content rejected";
            }));

            outcomes.Add(Run("StaleEpochAndRestartReplay", CheckKinds.RealSession, () =>
            {
                MissionSession session = NewSession(s1);
                var results = new List<CommandResult>();
                session.CommandCompleted += results.Add;
                session.Submit(new SessionCommand(Guid.NewGuid(), 0, SessionAction.Resume));
                session.Tick(10);
                var restart = new SessionCommand(Guid.NewGuid(), 0, SessionAction.Restart);
                session.Submit(restart);
                MissionSnapshot fresh = Current(session);
                Require(fresh.Epoch == 1 && fresh.Revision == 0, "restart did not open epoch 1 revision 0");
                session.Submit(new SessionCommand(Guid.NewGuid(), 0, SessionAction.Resume));
                Require(results[results.Count - 1].Reason == "EpochMismatch", "old epoch accepted");
                session.Submit(new SessionCommand(Guid.NewGuid(), 2, SessionAction.Resume));
                Require(results[results.Count - 1].Reason == "EpochMismatch", "future epoch accepted");
                session.Submit(restart);
                Require(results[results.Count - 1].Accepted && results[results.Count - 1].Epoch == 0, "restart replay result changed");
                Require(ReferenceEquals(Current(session), fresh), "stale/replayed command changed state");
                ValidationReport report = PhaseOneScenarioValidator.ValidateMissionTransition(
                    new MissionSnapshot(0, 2, SessionState.Running, fresh.Power, 0, false), fresh, s1.Configuration);
                Require(!report.Contains("RestartNotClean"), report.ToString());
                return "old/future epochs rejected; replayed Restart not reapplied; reset clean";
            }));

            outcomes.Add(Run("PauseFreezesClockAndEnergy", CheckKinds.RealSession, () =>
            {
                MissionSession session = NewSession(s1);
                int published = 0;
                session.SnapshotChanged += s => published++;
                session.Submit(new SessionCommand(Guid.NewGuid(), 0, SessionAction.Resume));
                session.Submit(new SessionCommand(Guid.NewGuid(), 0, SessionAction.SetScienceEnabled, true));
                session.Tick(5);
                session.Submit(new SessionCommand(Guid.NewGuid(), 0, SessionAction.Pause));
                MissionSnapshot paused = Current(session);
                int before = published;
                session.Tick(100);
                Require(published == before && ReferenceEquals(Current(session), paused), "Tick while paused changed state");
                session.Submit(new SessionCommand(Guid.NewGuid(), 0, SessionAction.SetScienceEnabled, false));
                MissionSnapshot toggled = Current(session);
                Require(toggled.Power.SimulatedTimeSeconds == paused.Power.SimulatedTimeSeconds
                    && toggled.Power.EnergyJoules == paused.Power.EnergyJoules, "science toggle while paused moved the clock");
                ValidationReport report = PhaseOneScenarioValidator.ValidateMissionTransition(paused, toggled, s1.Configuration);
                return "100 s Tick while paused: no snapshot, no time/energy change"
                    + (report.Contains("ScienceRequestedWhileDisabled")
                        ? "; NOTE: after disabling science the confirmed Power still shows science demand until the next Tick" : "");
            }));

            outcomes.Add(Run("FullRealStreamPassesValidator", CheckKinds.RealSession, () =>
            {
                MissionSession session = NewSession(PhaseOneFixtures.DepletionEssentialLost());
                var stream = new List<MissionSnapshot> { Current(session) };
                session.SnapshotChanged += stream.Add;
                PowerConfiguration config = PhaseOneFixtures.DepletionEssentialLost().Configuration;
                session.Submit(new SessionCommand(Guid.NewGuid(), 0, SessionAction.Resume));
                for (int i = 0; i < 6; i++) session.Tick(1);
                session.Submit(new SessionCommand(Guid.NewGuid(), 0, SessionAction.SetScienceEnabled, true));
                for (int i = 0; i < 40 && Current(session).State == SessionState.Running; i++) session.Tick(0.7);
                long epoch = Current(session).Epoch;
                session.Submit(new SessionCommand(Guid.NewGuid(), epoch, SessionAction.Restart));
                int warnings = 0;
                for (int i = 1; i < stream.Count; i++)
                {
                    ValidationReport report = PhaseOneScenarioValidator.ValidateMissionTransition(stream[i - 1], stream[i], config);
                    Require(!report.HasErrors, "snapshot " + i + ": " + report);
                    warnings += report.Issues.Count;
                }
                return stream.Count + " snapshots (irregular 0.7 s ticks, depletion, restart); 0 errors, " + warnings + " warnings";
            }));

            return outcomes;
        }

        private static void RequireNoConfigErrors(PowerConfiguration configuration)
        {
            ValidationReport report = PhaseOneScenarioValidator.ValidateConfiguration(configuration);
            Require(!report.HasErrors, "fixture configuration: " + report);
        }

        // ------------------------------------------------------------------ design review of the scenario

        /// <summary>Checks whether a scenario gives a testable decision: does keeping science on always win?
        /// Reports Info (never Failed): product values are the team's decision, not D5's.</summary>
        public static CheckOutcome ReviewScenarioDecision(string label, PowerConfiguration configuration, MissionRules rules)
        {
            return Run("DecisionReview_" + label, CheckKinds.Design, () =>
            {
                RequireNoConfigErrors(configuration);
                MissionOutcome always = Simulate(configuration, rules, true, out double alwaysEnergy, out double alwaysTime);
                MissionOutcome never = Simulate(configuration, rules, false, out double neverEnergy, out double neverTime);
                bool essentialSafe = configuration.GenerationWatts >= configuration.EssentialConsumptionWatts;
                var text = new StringBuilder();
                text.Append("science always on -> ").Append(always).Append(" at ").Append(alwaysTime.ToString("0.##"))
                    .Append(" s, ").Append(alwaysEnergy.ToString("0.##")).Append(" J left; never -> ").Append(never)
                    .Append(" at ").Append(neverTime.ToString("0.##")).Append(" s. ");
                if (always == MissionOutcome.ObjectiveAchieved && essentialSafe)
                    text.Append("NO TRADE-OFF: generation >= essential, so essential service can never be lost and keeping science on always wins.");
                else if (always == MissionOutcome.ObjectiveAchieved)
                    text.Append("Keeping science on wins; check that a worse strategy exists for the decision to matter.");
                else
                    text.Append("Keeping science on does not win: the player must manage science.");
                if (never == MissionOutcome.TimeExpired)
                    text.Append(" Without the objective the session ends Completed (TimeExpired): clients cannot tell it from success.");
                return text.ToString();
            });
        }

        private static MissionOutcome Simulate(PowerConfiguration configuration, MissionRules rules, bool science,
            out double energy, out double time)
        {
            var session = new MissionSession(new PowerSystem(), configuration, rules, MissionSession.DefaultMaxStepSeconds);
            if (science) session.Submit(new SessionCommand(Guid.NewGuid(), 0, SessionAction.SetScienceEnabled, true));
            session.Submit(new SessionCommand(Guid.NewGuid(), 0, SessionAction.Resume));
            double guard = rules.MissionDurationSeconds + 10;
            while (Current(session).State == SessionState.Running && guard > 0) { session.Tick(5); guard -= 5; }
            MissionSnapshot last = Current(session);
            energy = last.Power.EnergyJoules;
            time = last.Power.SimulatedTimeSeconds;
            return session.Outcome;
        }

        public static List<CheckOutcome> RunDesignReview()
        {
            var outcomes = new List<CheckOutcome>();
            ScenarioDefinition defaults = null;
            try
            {
                defaults = UnityEngine.ScriptableObject.CreateInstance<ScenarioDefinition>();
                string problem = defaults.Validate();
                if (problem != null)
                    outcomes.Add(new CheckOutcome("DecisionReview_ScenarioDefinitionDefaults", CheckKinds.Design, CheckStatus.Failed, problem));
                else
                    outcomes.Add(ReviewScenarioDecision("ScenarioDefinitionDefaults", defaults.ToPowerConfiguration(), defaults.ToMissionRules()));
            }
            finally
            {
                if (defaults != null) UnityEngine.Object.DestroyImmediate(defaults);
            }
            return outcomes;
        }

        // ------------------------------------------------------------------ two clients over loopback

        private sealed class ClientProbe
        {
            public readonly SessionConnectionController Controller;
            public readonly List<MissionSnapshot> Snapshots = new List<MissionSnapshot>();
            public readonly List<CommandResult> Results = new List<CommandResult>();
            public readonly List<string> Abandoned = new List<string>();

            public ClientProbe(SessionConnectionController controller)
            {
                Controller = controller;
                controller.Session.SnapshotChanged += Snapshots.Add;
                controller.Session.CommandCompleted += Results.Add;
                controller.Session.CommandAbandoned += (command, reason) => Abandoned.Add(reason);
            }

            public NetworkSessionAdapter Port { get { return Controller.Session; } }

            public SessionCommand Submit(SessionAction action, bool? value = null)
            {
                var command = new SessionCommand(Guid.NewGuid(), Current(Port).Epoch, action, value);
                Port.Submit(command);
                return command;
            }

            public CommandResult ResultFor(SessionCommand command)
            {
                CommandResult found = null;
                foreach (CommandResult result in Results)
                    if (result.CommandId == command.Id)
                    {
                        Require(found == null, "more than one result for " + command.Id);
                        found = result;
                    }
                return found;
            }
        }

        private sealed class LoopbackRig : IDisposable
        {
            public readonly LoopbackNetwork Network = new LoopbackNetwork();
            public readonly MissionSession Authority;
            public readonly NetworkCommandBridge Bridge;
            public readonly List<ClientProbe> Clients = new List<ClientProbe>();
            public readonly PowerConfiguration Configuration;

            public LoopbackRig(PhaseOneFixture fixture, int clients)
            {
                Configuration = fixture.Configuration;
                Authority = NewSession(fixture);
                Bridge = new NetworkCommandBridge(Authority, Network.Host);
                for (int i = 0; i < clients; i++) AddClient();
                Network.Pump();
            }

            public ClientProbe AddClient()
            {
                var probe = new ClientProbe(new SessionConnectionController(Network.CreateClient()));
                Clients.Add(probe);
                probe.Controller.Connect();
                return probe;
            }

            /// <summary>Every connected client holds exactly the authority's confirmed snapshot.</summary>
            public void RequireConverged(string when)
            {
                MissionSnapshot truth = Current(Authority);
                for (int i = 0; i < Clients.Count; i++)
                {
                    ClientProbe client = Clients[i];
                    if (client.Controller.State != SessionConnectionState.Connected) continue;
                    MissionSnapshot seen = Current(client.Port);
                    Require(SameState(truth, seen), when + ": client " + i + " sees e" + seen.Epoch + " r" + seen.Revision + " "
                        + seen.State + " " + seen.Power.EnergyJoules + " J, authority e" + truth.Epoch + " r" + truth.Revision
                        + " " + truth.State + " " + truth.Power.EnergyJoules + " J");
                }
            }

            public void Dispose()
            {
                foreach (ClientProbe client in Clients) client.Controller.Dispose();
                Network.Pump();
                Bridge.Dispose();
            }
        }

        private static bool SameState(MissionSnapshot a, MissionSnapshot b)
        {
            PowerSnapshot p = a.Power, q = b.Power;
            return a.Epoch == b.Epoch && a.Revision == b.Revision && a.State == b.State
                && a.ScienceEnabled == b.ScienceEnabled && a.ScienceWorkSeconds.Equals(b.ScienceWorkSeconds)
                && p.SimulatedTimeSeconds.Equals(q.SimulatedTimeSeconds) && p.EnergyJoules.Equals(q.EnergyJoules)
                && p.CapacityJoules.Equals(q.CapacityJoules) && p.GenerationWatts.Equals(q.GenerationWatts)
                && p.EssentialRequestedWatts.Equals(q.EssentialRequestedWatts) && p.EssentialServedWatts.Equals(q.EssentialServedWatts)
                && p.ScienceRequestedWatts.Equals(q.ScienceRequestedWatts) && p.ScienceServedWatts.Equals(q.ScienceServedWatts);
        }

        public static List<CheckOutcome> RunLoopbackChecks()
        {
            var outcomes = new List<CheckOutcome>();
            PhaseOneFixture s1 = PhaseOneFixtures.BalanceObjectiveRestart();

            outcomes.Add(Run("TwoClientsSyncToOneAuthority", CheckKinds.Loopback, () =>
            {
                using (var rig = new LoopbackRig(s1, 2))
                {
                    foreach (ClientProbe client in rig.Clients)
                        Require(client.Controller.State == SessionConnectionState.Connected, "client state " + client.Controller.State);
                    Require(rig.Bridge.ConnectedClientCount == 2, "bridge sees " + rig.Bridge.ConnectedClientCount + " clients");
                    rig.RequireConverged("after join");
                    return "both clients Connected with the authority's initial snapshot";
                }
            }));

            outcomes.Add(Run("ActionFromOneClientHasConsequencesForBoth", CheckKinds.Loopback, () =>
            {
                using (var rig = new LoopbackRig(s1, 2))
                {
                    ClientProbe a = rig.Clients[0], b = rig.Clients[1];
                    SessionCommand resume = a.Submit(SessionAction.Resume);
                    Require(a.ResultFor(resume) == null, "result arrived before the network delivered it (not asynchronous)");
                    rig.Network.Pump();
                    Require(a.ResultFor(resume) != null && a.ResultFor(resume).Accepted, "A: Resume not accepted");
                    Require(b.ResultFor(resume) == null, "B received A's result (results must go only to the sender)");
                    rig.Authority.Tick(60);
                    SessionCommand science = b.Submit(SessionAction.SetScienceEnabled, true);
                    rig.Network.Pump();
                    Require(b.ResultFor(science) != null && b.ResultFor(science).Accepted, "B: science not accepted");
                    rig.Authority.Tick(30);
                    rig.Network.Pump();
                    rig.RequireConverged("after B enabled science");
                    string mismatch = s1.Expected[4].Compare(Current(a.Port));
                    Require(mismatch == null, "A does not see the reference state after 60 s + 30 s with science: " + mismatch);
                    return "A resumed, B enabled science; both see 90 s, 16500 J, 30 s of work (reference)";
                }
            }));

            outcomes.Add(Run("DuplicateAndForeignIdOverNetwork", CheckKinds.Loopback, () =>
            {
                using (var rig = new LoopbackRig(s1, 2))
                {
                    ClientProbe a = rig.Clients[0], b = rig.Clients[1];
                    SessionCommand science = a.Submit(SessionAction.SetScienceEnabled, true);
                    rig.Network.Pump();
                    long revision = Current(rig.Authority).Revision;
                    a.Port.Submit(new SessionCommand(science.Id, science.Epoch, science.Action, science.RequestedScienceEnabled));
                    rig.Network.Pump();
                    Require(Current(rig.Authority).Revision == revision, "duplicate from the same client was reapplied");
                    b.Port.Submit(new SessionCommand(science.Id, science.Epoch, SessionAction.Pause));
                    rig.Network.Pump();
                    CommandResult foreign = b.ResultFor(science);
                    Require(foreign != null && !foreign.Accepted && foreign.Reason == "CommandIdConflict", "foreign ID not rejected");
                    Require(Current(rig.Authority).Revision == revision, "foreign ID changed state");
                    rig.RequireConverged("after duplicates");
                    return "same-client replay not reapplied; other client's reuse rejected (CommandIdConflict)";
                }
            }));

            outcomes.Add(Run("RestartFromOneClientInvalidatesOldEpochForOther", CheckKinds.Loopback, () =>
            {
                using (var rig = new LoopbackRig(s1, 2))
                {
                    ClientProbe a = rig.Clients[0], b = rig.Clients[1];
                    a.Submit(SessionAction.Resume);
                    rig.Network.Pump();
                    rig.Authority.Tick(20);
                    rig.Network.Pump();
                    long oldEpoch = Current(b.Port).Epoch;
                    a.Submit(SessionAction.Restart);
                    rig.Network.Pump();
                    var stale = new SessionCommand(Guid.NewGuid(), oldEpoch, SessionAction.Resume);
                    b.Port.Submit(stale);
                    rig.Network.Pump();
                    Require(b.ResultFor(stale) != null && b.ResultFor(stale).Reason == "EpochMismatch", "old epoch accepted");
                    MissionSnapshot now = Current(rig.Authority);
                    Require(now.Epoch == oldEpoch + 1 && now.Revision == 0 && now.State == SessionState.Ready, "authority not restarted cleanly");
                    rig.RequireConverged("after restart");
                    string mismatch = s1.Expected[s1.Expected.Length - 1].Compare(Current(b.Port));
                    Require(mismatch == null, "B after restart: " + mismatch);
                    return "both at epoch " + now.Epoch + " revision 0 with initial energy; stale command rejected";
                }
            }));

            outcomes.Add(Run("SharedPauseStopsClockForBoth", CheckKinds.Loopback, () =>
            {
                using (var rig = new LoopbackRig(s1, 2))
                {
                    ClientProbe a = rig.Clients[0], b = rig.Clients[1];
                    a.Submit(SessionAction.Resume);
                    rig.Network.Pump();
                    rig.Authority.Tick(15);
                    b.Submit(SessionAction.Pause);
                    rig.Network.Pump();
                    MissionSnapshot paused = Current(a.Port);
                    int seen = a.Snapshots.Count;
                    rig.Authority.Tick(120);
                    rig.Network.Pump();
                    Require(a.Snapshots.Count == seen && SameState(Current(a.Port), paused), "clock advanced while paused");
                    Require(paused.State == SessionState.Paused && paused.Power.SimulatedTimeSeconds == 15, "pause not at 15 s");
                    rig.RequireConverged("while paused");
                    return "B paused at 15 s; 120 s of authority ticks changed nothing on either client";
                }
            }));

            outcomes.Add(Run("ClientStreamsPassValidator", CheckKinds.Loopback, () =>
            {
                using (var rig = new LoopbackRig(PhaseOneFixtures.DepletionEssentialLost(), 2))
                {
                    ClientProbe a = rig.Clients[0], b = rig.Clients[1];
                    MissionSnapshot first = Current(a.Port);
                    a.Submit(SessionAction.SetScienceEnabled, true);
                    rig.Network.Pump();
                    b.Submit(SessionAction.Resume);
                    rig.Network.Pump();
                    for (int i = 0; i < 30 && Current(rig.Authority).State == SessionState.Running; i++)
                    {
                        rig.Authority.Tick(1);
                        rig.Network.Pump();
                    }
                    Require(Current(b.Port).State == SessionState.Interrupted, "B did not see the essential loss");
                    var stream = new List<MissionSnapshot> { first };
                    stream.AddRange(b.Snapshots);
                    int warnings = 0;
                    for (int i = 1; i < stream.Count; i++)
                    {
                        if (ReferenceEquals(stream[i], stream[i - 1])) continue;
                        ValidationReport report = PhaseOneScenarioValidator.ValidateMissionTransition(stream[i - 1], stream[i], rig.Configuration);
                        Require(!report.HasErrors, "client stream " + i + ": " + report);
                        warnings += report.Issues.Count;
                    }
                    rig.RequireConverged("after interruption");
                    return stream.Count + " snapshots on B; Interrupted at " + Current(b.Port).Power.SimulatedTimeSeconds
                        + " s like the reference (20 s); 0 errors, " + warnings + " warnings";
                }
            }));

            outcomes.Add(Run("LateJoinerGetsCurrentState", CheckKinds.Loopback, () =>
            {
                using (var rig = new LoopbackRig(s1, 1))
                {
                    rig.Clients[0].Submit(SessionAction.Resume);
                    rig.Network.Pump();
                    rig.Authority.Tick(42);
                    rig.Network.Pump();
                    ClientProbe late = rig.AddClient();
                    rig.Network.Pump();
                    Require(late.Controller.State == SessionConnectionState.Connected, "late client " + late.Controller.State);
                    rig.RequireConverged("after late join");
                    return "client joining at 42 s receives the confirmed state";
                }
            }));

            outcomes.Add(Run("HostLossLeavesResultUnknown", CheckKinds.Loopback, () =>
            {
                using (var rig = new LoopbackRig(s1, 2))
                {
                    ClientProbe a = rig.Clients[0], b = rig.Clients[1];
                    MissionSnapshot before = Current(a.Port);
                    a.Submit(SessionAction.Resume);          // in flight when the host disappears
                    rig.Network.Host.DropAll("HostLost");
                    rig.Network.Pump();
                    Require(a.Controller.State == SessionConnectionState.ConnectionLost
                        && b.Controller.State == SessionConnectionState.ConnectionLost, "clients did not detect host loss");
                    Require(a.Controller.LastDisconnectReason == "HostLost", "reason " + a.Controller.LastDisconnectReason);
                    Require(a.Results.Count == 0 && a.Abandoned.Count == 1
                        && a.Abandoned[0] == SessionNetworkCodes.ConnectionLost, "in-flight command not abandoned as unknown");
                    Require(Current(rig.Authority).State == before.State, "message delivered after the link closed");
                    return "both ConnectionLost (HostLost); pending command abandoned without an invented result";
                }
            }));

            outcomes.Add(new CheckOutcome("TwoProcessesOverApprovedSdk", CheckKinds.RealConnection, CheckStatus.Pending,
                "No networking SDK approved: ISessionHostTransport/ISessionClientTransport have no binding. "
                + "Loopback results above prove wiring only, not a network."));
            return outcomes;
        }
    }

    // ---------------------------------------------------------------------- in-memory loopback transport

    /// <summary>Deterministic in-memory delivery for D3's SDK-neutral seams. Messages are queued and only
    /// delivered by Pump(), so results are asynchronous like a real transport. NOT a networking binding.</summary>
    public sealed class LoopbackNetwork
    {
        private readonly Queue<Action> queue = new Queue<Action>();
        private ulong lastConnectionId;

        public LoopbackHostTransport Host { get; }

        public LoopbackNetwork() { Host = new LoopbackHostTransport(this); }

        public LoopbackClientTransport CreateClient() { return new LoopbackClientTransport(this); }

        internal void Post(Action action) { queue.Enqueue(action); }

        internal ulong NextConnectionId() { return ++lastConnectionId; }

        /// <summary>Delivers queued messages, including any they produce, until none remain.</summary>
        public int Pump(int limit = 100000)
        {
            int delivered = 0;
            while (queue.Count > 0)
            {
                if (++delivered > limit) throw new InvalidOperationException("Loopback did not settle.");
                queue.Dequeue()();
            }
            return delivered;
        }
    }

    public sealed class LoopbackHostTransport : ISessionHostTransport
    {
        private readonly LoopbackNetwork network;
        private readonly Dictionary<ulong, LoopbackClientTransport> clients = new Dictionary<ulong, LoopbackClientTransport>();

        public event Action<ulong> ClientConnected;
        public event Action<ulong> ClientDisconnected;
        public event Action<ulong, byte[]> MessageReceived;

        internal LoopbackHostTransport(LoopbackNetwork network) { this.network = network; }

        public int ClientCount { get { return clients.Count; } }

        public void SendToClient(ulong connectionId, byte[] payload)
        {
            LoopbackClientTransport client;
            if (clients.TryGetValue(connectionId, out client))
                network.Post(() => client.DeliverFromHost(connectionId, payload));
        }

        public void DisconnectClient(ulong connectionId, string reason)
        {
            LoopbackClientTransport client;
            if (!clients.TryGetValue(connectionId, out client)) return;
            clients.Remove(connectionId);
            network.Post(() => client.LinkLost(connectionId, reason));
            network.Post(() => { Action<ulong> handler = ClientDisconnected; if (handler != null) handler(connectionId); });
        }

        /// <summary>Simulates losing the host: every client link drops with <paramref name="reason"/>.</summary>
        public void DropAll(string reason)
        {
            foreach (ulong id in new List<ulong>(clients.Keys)) DisconnectClient(id, reason);
        }

        internal void Accept(ulong connectionId, LoopbackClientTransport client)
        {
            clients[connectionId] = client;
            Action<ulong> handler = ClientConnected;
            if (handler != null) handler(connectionId);
        }

        internal void Leave(ulong connectionId)
        {
            if (!clients.Remove(connectionId)) return;
            Action<ulong> handler = ClientDisconnected;
            if (handler != null) handler(connectionId);
        }

        internal void Receive(ulong connectionId, byte[] payload)
        {
            if (!clients.ContainsKey(connectionId)) return;
            Action<ulong, byte[]> handler = MessageReceived;
            if (handler != null) handler(connectionId, payload);
        }
    }

    public sealed class LoopbackClientTransport : ISessionClientTransport
    {
        private readonly LoopbackNetwork network;
        private long link;
        private ulong connectionId;
        private bool open;

        public event Action<long> Connected;
        public event Action<long, string> Disconnected;
        public event Action<long, byte[]> MessageReceived;

        internal LoopbackClientTransport(LoopbackNetwork network) { this.network = network; }

        public void Connect(long attempt)
        {
            link = attempt;
            open = false;
            network.Post(() =>
            {
                if (link != attempt || open) return; // cancelled or superseded
                connectionId = network.NextConnectionId();
                open = true;
                network.Host.Accept(connectionId, this);
                Action<long> handler = Connected;
                if (handler != null) handler(attempt);
            });
        }

        public void Disconnect(long attempt)
        {
            if (attempt != link) return;
            link = 0;
            if (!open) return;
            open = false;
            ulong id = connectionId;
            network.Post(() => network.Host.Leave(id));
        }

        public void SendToHost(long attempt, byte[] payload)
        {
            if (!open || attempt != link) return;
            ulong id = connectionId;
            network.Post(() => network.Host.Receive(id, payload));
        }

        internal void DeliverFromHost(ulong id, byte[] payload)
        {
            if (!open || id != connectionId) return;
            Action<long, byte[]> handler = MessageReceived;
            if (handler != null) handler(link, payload);
        }

        internal void LinkLost(ulong id, string reason)
        {
            if (!open || id != connectionId) return;
            open = false;
            long attempt = link;
            Action<long, string> handler = Disconnected;
            if (handler != null) handler(attempt, reason);
        }
    }
}
