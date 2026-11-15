using System;
using System.Collections.Generic;
using Trainer.Application;
using Trainer.Application.BaseOperations;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.BaseOperations;
using Trainer.Unity.Validation.Phase04;

namespace Trainer.Tooling.Validation.Phase04
{
    /// <summary>
    /// In-memory player of the REAL phase 4 authority (no network, no port adapter): identity only from Join, movement as held
    /// directions with increasing sequences, operations as <see cref="BaseOperationCommand"/>s stamped like D3's client (epoch,
    /// confirmed zone sequence, the machine's CURRENT observation). Results are taken only from CommandCompleted for this
    /// sender. Validation tooling, never in a scene.
    /// </summary>
    public sealed class AuthorityActor : IBaseOperationsActor, IDisposable
    {
        private readonly IBaseOperationsAuthority authority;
        private readonly BaseOperationsScenario scenario;
        private Guid pendingId;
        private string answer;
        private MoveDirection sent = MoveDirection.Zero;
        private long sentEpoch = -1;
        private long sequence;

        public AuthorityActor(IBaseOperationsAuthority authority, BaseOperationsScenario scenario)
        {
            this.authority = authority ?? throw new ArgumentNullException(nameof(authority));
            this.scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
            authority.CommandCompleted += OnCompleted;
        }

        public OutpostMembership Membership { get; private set; } = OutpostMembership.NotJoined;
        public PlayerId Player => Membership.Player;
        public bool HasPending => pendingId != Guid.Empty;
        /// <summary>Every result routed to this sender (id → reason), including replays.</summary>
        public List<CommandResult> Results { get; } = new List<CommandResult>();

        public OutpostMembership Join()
        {
            Membership = authority.Join();
            return Membership;
        }

        public bool Leave()
        {
            bool left = authority.Leave(Membership.Player);
            Membership = OutpostMembership.Left(Membership.Player, "validation-leave");
            return left;
        }

        public bool TryGetState(out BaseOperationsSnapshot snapshot) => authority.TryGetSnapshot(out snapshot);

        public void Move(double x, double y)
        {
            if (!Membership.IsJoined || !authority.TryGetSnapshot(out BaseOperationsSnapshot s)) return;
            MoveDirection wanted = MoveDirection.FromAxes(x, y);
            if (s.Epoch == sentEpoch && wanted.Equals(sent)) return;
            if (s.Outpost.TryGetCrew(Player, out CrewMemberState me)) sequence = Math.Max(sequence, me.LastInputSequence);
            authority.SubmitMovement(Player, new MovementInput(s.Epoch, ++sequence, wanted));
            sent = wanted;
            sentEpoch = s.Epoch;
        }

        public bool Request(BaseOperationAction action, StationId station, OperationChoice choice, string manifestKey, long orderSequence)
        {
            if (HasPending || !Membership.IsJoined || !authority.TryGetSnapshot(out BaseOperationsSnapshot s)) return false;
            long observation = BaseOperationCommand.Unstated;
            if (BaseOperationCommand.NeedsObservation(action))
            {
                EquipmentKind? kind = scenario.EquipmentAt(station);
                if (kind == null) return false;
                EquipmentObservation o = s.EquipmentOf(kind.Value).LatestObservation;
                if (o == null || !o.Current) return false;
                observation = o.Sequence;
            }
            var command = new BaseOperationCommand(Guid.NewGuid(), s.Epoch, action, station, choice,
                action == BaseOperationAction.PlaceOrder ? manifestKey : null,
                action == BaseOperationAction.CancelOrder || action == BaseOperationAction.UnloadDelivery ? orderSequence : BaseOperationCommand.Unstated,
                observation, ZoneSequence(s));
            Send(command);
            return true;
        }

        /// <summary>Submits a raw command (tests of malformed or forged intents) and returns the synchronous answer, or null.</summary>
        public string Send(BaseOperationCommand command)
        {
            pendingId = command.Id;
            answer = null;
            authority.Submit(Player, command);
            return answer;
        }

        /// <summary>Submits a phase 2/3 command (pause, resume, restart, tasks) and returns the synchronous answer, or null.</summary>
        public string Send(ExpeditionCommand command)
        {
            pendingId = command.Id;
            answer = null;
            authority.Submit(Player, command);
            return answer;
        }

        public long ZoneSequence(BaseOperationsSnapshot s) =>
            s.Expedition.TryGetCrew(Player, out ExpeditionCrewState crew) ? crew.ZoneSequence : BaseOperationCommand.Unstated;

        public string TakeAnswer()
        {
            string value = answer;
            answer = null;
            return value;
        }

        public void Dispose() => authority.CommandCompleted -= OnCompleted;

        private void OnCompleted(PlayerId player, CommandResult result)
        {
            if (player != Membership.Player) return;
            Results.Add(result);
            if (result.CommandId != pendingId) return;
            pendingId = Guid.Empty;
            answer = result.Reason;
        }
    }

    /// <summary>
    /// One in-memory phase 4 session over the REAL authority: actors, a fixed-step clock (the authority's own tick), the
    /// continuity stream validator on every published snapshot, and helpers that only act through intents.
    /// </summary>
    public sealed class Phase04Session : IDisposable
    {
        public const double Frame = 0.1;

        private readonly List<AuthorityActor> actors = new List<AuthorityActor>();
        private readonly Phase04Navigator navigator;

        public AuthorityInstance Instance { get; }
        public IBaseOperationsAuthority Authority => Instance.Authority;
        public BaseOperationsScenario Scenario { get; }
        public BaseOperationsStreamValidator Stream { get; }
        public double Driven { get; private set; }

        public Phase04Session(AuthorityInstance instance, BaseOperationsScenario scenario, string kind)
        {
            Instance = instance ?? throw new ArgumentNullException(nameof(instance));
            Scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
            Stream = new BaseOperationsStreamValidator(scenario, kind);
            navigator = new Phase04Navigator(scenario.Expedition);
            Authority.SnapshotChanged += Stream.Observe;
            if (Authority.TryGetSnapshot(out BaseOperationsSnapshot first)) Stream.Observe(first);
        }

        public static Phase04Session Create(Phase04ImplementationDiscovery discovery, BaseOperationsScenario scenario, string kind) =>
            new Phase04Session(discovery.CreateAuthority(scenario), scenario, kind);

        public BaseOperationsSnapshot State
        {
            get
            {
                if (!Authority.TryGetSnapshot(out BaseOperationsSnapshot s)) throw new Phase04CheckFailed("the authority published no state");
                return s;
            }
        }

        public AuthorityActor Join()
        {
            var actor = new AuthorityActor(Authority, Scenario);
            OutpostMembership m = actor.Join();
            if (!m.IsJoined) throw new Phase04CheckFailed("Join rejected: " + m.Reason);
            actors.Add(actor);
            return actor;
        }

        /// <summary>Resume from Ready/Paused through a player's session command.</summary>
        public void Resume(AuthorityActor by) => Session(by, ExpeditionAction.Resume, true);
        public void Pause(AuthorityActor by) => Session(by, ExpeditionAction.Pause, true);
        public void Restart(AuthorityActor by) => Session(by, ExpeditionAction.Restart, true);

        public string Session(AuthorityActor by, ExpeditionAction action, bool mustAccept)
        {
            string reason = by.Send(ExpeditionCommand.Session(Guid.NewGuid(), State.Epoch, action)) ?? AwaitAnswer(by);
            if (mustAccept && reason != OutpostReasons.Accepted) throw new Phase04CheckFailed(action + " refused: " + reason);
            return reason;
        }

        /// <summary>Advances the authority's clock in frames, updating any scripts in between.</summary>
        public void Advance(double seconds, Action<double> perFrame = null)
        {
            for (double done = 0; done < seconds - 1e-9; done += Frame)
            {
                double dt = Math.Min(Frame, seconds - done);
                perFrame?.Invoke(dt);
                Instance.Advance(dt);
                Driven += dt;
            }
        }

        /// <summary>Advances until <paramref name="condition"/> holds or <paramref name="maxSeconds"/> pass; false on timeout.</summary>
        public bool AdvanceUntil(Func<BaseOperationsSnapshot, bool> condition, double maxSeconds, Action<double> perFrame = null, double frame = Frame)
        {
            for (double done = 0; done < maxSeconds; done += frame)
            {
                if (condition(State)) return true;
                perFrame?.Invoke(frame);
                Instance.Advance(frame);
                Driven += frame;
            }
            return condition(State);
        }

        public string AwaitAnswer(AuthorityActor actor, double maxSeconds = 2)
        {
            string reason = actor.TakeAnswer();
            for (double done = 0; reason == null && done < maxSeconds; done += Frame)
            {
                Instance.Advance(Frame);
                Driven += Frame;
                reason = actor.TakeAnswer();
            }
            return reason;
        }

        /// <summary>Walks <paramref name="actor"/> into reach of <paramref name="station"/> with held directions only.</summary>
        public void WalkTo(AuthorityActor actor, StationId station, double maxSeconds = 90)
        {
            if (!Scenario.Layout.TryGetStation(station, out StationPlacement placement)) throw new Phase04CheckFailed("unknown station " + station);
            List<BasePosition> path = null;
            int index = 0;
            bool reached = AdvanceUntil(s =>
            {
                if (!s.Outpost.TryGetCrew(actor.Player, out CrewMemberState me)) return false;
                BasePosition p = me.Pose.Position;
                if (placement.IsWithinReach(p))
                {
                    actor.Move(0, 0);
                    return true;
                }
                if (path == null)
                {
                    path = navigator.PlanToStation(p, station) ?? throw new Phase04CheckFailed("no route to " + station + " from " + p);
                    index = 0;
                }
                while (index < path.Count - 1 && p.DistanceTo(path[index]) <= 0.2) index++;
                double dx = path[index].X - p.X, dy = path[index].Y - p.Y, d = Math.Sqrt(dx * dx + dy * dy);
                if (d > 1e-6) actor.Move(dx / d * (d < 0.5 ? 0.4 : 1), dy / d * (d < 0.5 ? 0.4 : 1));
                return false;
            }, maxSeconds);
            actor.Move(0, 0);
            Advance(Frame);
            if (!reached) throw new Phase04CheckFailed("could not reach " + station + " in " + maxSeconds + " s");
        }

        /// <summary>Sends one operation and returns the authority's answer (null = no answer).</summary>
        public string Do(AuthorityActor actor, BaseOperationAction action, StationId station, OperationChoice choice = OperationChoice.None,
            string manifest = null, long order = BaseOperationCommand.Unstated)
        {
            actor.TakeAnswer();
            if (!actor.Request(action, station, choice, manifest, order))
                return "NotSent(no state, busy or no current observation)";
            return AwaitAnswer(actor);
        }

        /// <summary>Do + require Accepted + wait until the actor's operation (if timed) ends.</summary>
        public void Complete(AuthorityActor actor, BaseOperationAction action, StationId station, OperationChoice choice = OperationChoice.None,
            string manifest = null, long order = BaseOperationCommand.Unstated, double maxSeconds = 120)
        {
            string reason = Do(actor, action, station, choice, manifest, order);
            if (reason != OutpostReasons.Accepted) throw new Phase04CheckFailed(action + "/" + choice + " at " + station + " refused: " + reason);
            if (!AdvanceUntil(s => !s.TryGetOperation(actor.Player, out _), maxSeconds))
                throw new Phase04CheckFailed(action + " did not finish in " + maxSeconds + " s (stalled power?)");
        }

        public void Dispose()
        {
            Authority.SnapshotChanged -= Stream.Observe;
            foreach (AuthorityActor actor in actors) actor.Dispose();
            (Authority as IDisposable)?.Dispose();
        }
    }
}
