using System;
using System.Collections.Generic;
using System.Linq;
using Trainer.Application;
using Trainer.Application.Outpost;
using static Trainer.Tooling.Validation.Phase02.Phase02Checks;

namespace Trainer.Tooling.Validation.Phase02
{
    /// <summary>
    /// What the two-participant script needs from the integrated build. Filled at integration with the REAL
    /// ports (D3's host-local and network client adapters over D2's authority); never with the development fake.
    /// </summary>
    public sealed class TwoPortPlan
    {
        public string Label { get; set; } = "two ports";
        public OutpostScenario Scenario { get; set; }
        public IOutpostSessionPort Host { get; set; }
        public IOutpostSessionPort Client { get; set; }
        /// <summary>Advances simulated time and pumps the transport by the given seconds.</summary>
        public Action<double> Pump { get; set; }
        /// <summary>Closes the client's link (real disconnect). Null: the disconnect step is Pending.</summary>
        public Action DisconnectClient { get; set; }
        public StationId HostStation { get; set; }
        public TaskTypeId HostTask { get; set; }
        public StationId ClientStation { get; set; }
        public TaskTypeId ClientTask { get; set; }
        /// <summary>Optional straight-line waypoints to reach each station from the spawn (scenario-specific).</summary>
        public IReadOnlyList<BasePosition> HostRoute { get; set; } = Array.Empty<BasePosition>();
        public IReadOnlyList<BasePosition> ClientRoute { get; set; } = Array.Empty<BasePosition>();
        public double MaxPumpSeconds { get; set; } = 120;
    }

    /// <summary>
    /// The phase 2 acceptance flow over two participants: both join and see each other, one moves and the
    /// other sees it, they work on DIFFERENT stations at the same time, a contended station is granted to only
    /// one, tasks complete, both see the same state and outcome, a restart reaches both, and a disconnect leaves
    /// no ghost. In the Editor it exercises the real adapters in one process; the two-process build run is a
    /// separate, manual piece of evidence (see the D5 handoff).
    /// </summary>
    public static class TwoPortSessionScript
    {
        private const string Kind = Phase02CheckKinds.TwoPortSession;
        private const double Tick = 0.05;

        public static List<Phase02Check> Run(TwoPortPlan plan)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            string p = plan.Label + ": ";
            if (plan.Host == null || plan.Client == null || plan.Pump == null || plan.Scenario == null)
                return new List<Phase02Check>
                {
                    new Phase02Check(p + "two-participant session", Kind, Phase02CheckStatus.Pending,
                        "Real host/client ports and pump not provided (requires D2 + D3 integration).")
                };

            var results = new List<(string Who, CommandResult Result)>();
            var abandoned = new List<string>();
            var stream = new OutpostSnapshotStreamValidator(plan.Scenario);
            plan.Host.CommandCompleted += r => results.Add(("host", r));
            plan.Client.CommandCompleted += r => results.Add(("client", r));
            plan.Host.CommandAbandoned += (c, reason) => abandoned.Add("host " + c.Action + " " + reason);
            plan.Client.CommandAbandoned += (c, reason) => abandoned.Add("client " + c.Action + " " + reason);
            plan.Host.SnapshotChanged += s => stream.Observe(s);
            var seq = new Dictionary<IOutpostSessionPort, long>();

            OutpostSessionSnapshot Snap(IOutpostSessionPort port)
            {
                Require(port.TryGetSnapshot(out OutpostSessionSnapshot s), "No snapshot on a port.");
                return s;
            }
            bool PumpUntil(Func<bool> done)
            {
                for (double t = 0; t < plan.MaxPumpSeconds; t += Tick)
                {
                    if (done()) return true;
                    plan.Pump(Tick);
                }
                return done();
            }
            CommandResult Send(IOutpostSessionPort port, string who, OutpostCommand command)
            {
                port.Submit(command);
                Require(PumpUntil(() => results.Any(r => r.Result.CommandId == command.Id) || abandoned.Count > 0),
                    who + " " + command.Action + " got no result.");
                Require(abandoned.Count == 0, "Abandoned: " + string.Join(", ", abandoned));
                var mine = results.Where(r => r.Result.CommandId == command.Id).ToList();
                Require(mine.Count == 1 && mine[0].Who == who, "Result for " + who + " " + command.Action + " routed to "
                    + string.Join("/", mine.Select(m => m.Who)) + ".");
                return mine[0].Result;
            }
            void Move(IOutpostSessionPort port, double x, double y)
            {
                seq.TryGetValue(port, out long n);
                seq[port] = ++n;
                port.SubmitMovement(new MovementInput(Snap(port).Epoch, n, MoveDirection.FromAxes(x, y)));
            }
            void Walk(IOutpostSessionPort port, IEnumerable<BasePosition> route)
            {
                PlayerId me = port.Membership.Player;
                foreach (BasePosition target in route)
                {
                    bool arrived = PumpUntil(() =>
                    {
                        Snap(port).TryGetCrew(me, out CrewMemberState m);
                        double dx = target.X - m.Pose.Position.X, dy = target.Y - m.Pose.Position.Y, d = Math.Sqrt(dx * dx + dy * dy);
                        if (d < 0.05) return true;
                        double scale = Math.Min(1, d / (plan.Scenario.Movement.MaxSpeedMetersPerSecond * Tick));
                        Move(port, dx / d * scale, dy / d * scale);
                        return false;
                    });
                    Move(port, 0, 0);
                    Require(arrived, me + " could not reach waypoint " + target + ".");
                }
            }

            var checks = new List<Phase02Check>();
            checks.Add(Check(p + "both join and see two astronauts", Kind, () =>
            {
                Require(PumpUntil(() => plan.Host.Membership.IsJoined && plan.Client.Membership.IsJoined), "Both ports must reach Joined.");
                Require(plan.Host.Membership.Player != plan.Client.Membership.Player, "Distinct PlayerIds expected.");
                Require(PumpUntil(() => Snap(plan.Host).Crew.Count == 2 && Snap(plan.Client).Crew.Count == 2), "Each port must see two crew members.");
                return "host " + plan.Host.Membership.Player + ", client " + plan.Client.Membership.Player;
            }));
            checks.Add(Check(p + "resume; client movement is visible on the host", Kind, () =>
            {
                Require(Send(plan.Host, "host", new OutpostCommand(Guid.NewGuid(), Snap(plan.Host).Epoch, OutpostAction.Resume)).Accepted, "Resume.");
                PlayerId c = plan.Client.Membership.Player;
                Snap(plan.Host).TryGetCrew(c, out CrewMemberState start);
                if (plan.ClientRoute.Count > 0) Walk(plan.Client, plan.ClientRoute);
                else
                {
                    // No route: a short nudge in any admissible direction proves the shared state.
                    Move(plan.Client, 0, 1);
                    for (int i = 0; i < 4; i++) plan.Pump(Tick);
                    Move(plan.Client, 0, 0);
                }
                Require(PumpUntil(() => Snap(plan.Host).TryGetCrew(c, out var m) && !m.Pose.Position.Equals(start.Pose.Position)),
                    "Host never saw the client move.");
                if (plan.ClientRoute.Count == 0) Walk(plan.Client, new[] { start.Pose.Position });
                Require(PumpUntil(() => Snap(plan.Host).Revision == Snap(plan.Client).Revision && Snap(plan.Host).Epoch == Snap(plan.Client).Epoch),
                    "Ports did not converge on the same revision.");
                Snap(plan.Host).TryGetCrew(c, out var onHost);
                Snap(plan.Client).TryGetCrew(c, out var onClient);
                Require(onHost.Pose.Position.Equals(onClient.Pose.Position), "Host and client disagree on the client's position.");
            }));
            checks.Add(Check(p + "different tasks at the same time; contended station granted once", Kind, () =>
            {
                Walk(plan.Host, plan.HostRoute);
                long epoch = Snap(plan.Host).Epoch;
                CommandResult h = Send(plan.Host, "host", OutpostCommand.StartTask(Guid.NewGuid(), epoch, plan.HostStation, plan.HostTask));
                Require(h.Accepted, "Host StartTask: " + h.Reason + ".");
                CommandResult contended = Send(plan.Client, "client", OutpostCommand.StartTask(Guid.NewGuid(), epoch, plan.HostStation, plan.HostTask));
                Require(contended.Reason == OutpostReasons.StationOccupied, "Client on host's station: " + contended.Reason + ".");
                CommandResult c = Send(plan.Client, "client", OutpostCommand.StartTask(Guid.NewGuid(), epoch, plan.ClientStation, plan.ClientTask));
                Require(c.Accepted, "Client StartTask: " + c.Reason + ".");
                Require(PumpUntil(() => Occupant(Snap(plan.Client), plan.HostStation) == plan.Host.Membership.Player
                    && Occupant(Snap(plan.Host), plan.ClientStation) == plan.Client.Membership.Player), "Both ports must show both occupants.");
            }));
            checks.Add(Check(p + "both tasks complete and both ports agree on stocks and objectives", Kind, () =>
            {
                Require(PumpUntil(() => Ended(Snap(plan.Host), plan.HostStation) && Ended(Snap(plan.Host), plan.ClientStation)), "Tasks did not end.");
                Require(PumpUntil(() => Snap(plan.Host).Revision == Snap(plan.Client).Revision), "Ports did not converge.");
                OutpostSessionSnapshot a = Snap(plan.Host), b = Snap(plan.Client);
                Require(a.Resources.LifeSupport.Water.PotableKilograms == b.Resources.LifeSupport.Water.PotableKilograms
                    && a.Resources.Power.EnergyJoules == b.Resources.Power.EnergyJoules, "Ports show different stocks.");
                Require(a.Objectives.Select(o => o.Current).SequenceEqual(b.Objectives.Select(o => o.Current)), "Ports show different objectives.");
                return "host end " + Last(a, plan.HostStation) + ", client end " + Last(a, plan.ClientStation) + ", outcome " + a.Outcome;
            }));
            checks.Add(Check(p + "restart from the client reaches both ports", Kind, () =>
            {
                long epoch = Snap(plan.Client).Epoch;
                Require(Send(plan.Client, "client", new OutpostCommand(Guid.NewGuid(), epoch, OutpostAction.Restart)).Accepted, "Restart.");
                Require(PumpUntil(() => Snap(plan.Host).Epoch == epoch + 1 && Snap(plan.Client).Epoch == epoch + 1), "Both ports must reach the new epoch.");
                Require(Snap(plan.Host).Crew.Count == 2, "Restart must keep both members.");
            }));
            checks.Add(Check(p + "client disconnect leaves no ghost on the host", Kind, () =>
            {
                if (plan.DisconnectClient == null) throw new Phase02CheckPending("No real disconnect hook.");
                PlayerId c = plan.Client.Membership.Player;
                plan.DisconnectClient();
                Require(PumpUntil(() => !Snap(plan.Host).TryGetCrew(c, out _)), "Host still lists the client.");
                Require(plan.Client.Membership.Status == MembershipStatus.Left, "Client membership must be Left.");
            }));
            checks.Add(Check(p + "host snapshot stream", Kind, () =>
            {
                Require(stream.Report.IsValid, stream.Report.ToString());
                return stream.Count + " snapshots";
            }));
            return checks;
        }

        private static PlayerId Occupant(OutpostSessionSnapshot s, StationId id) =>
            s.TryGetStation(id, out StationState st) ? st.Occupant : PlayerId.None;

        private static bool Ended(OutpostSessionSnapshot s, StationId id) =>
            s.TryGetStation(id, out StationState st) && st.ActiveTask == null && st.LastEnd != null;

        private static string Last(OutpostSessionSnapshot s, StationId id) =>
            s.TryGetStation(id, out StationState st) && st.LastEnd != null ? st.LastEnd.Reason.ToString() : "none";
    }
}
