using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using Trainer.Application.Outpost;
using Trainer.Simulation.Eva;
using Trainer.Simulation.LifeSupport;

namespace Trainer.Application.Expedition
{
    /// <summary>
    /// The SINGLE phase 3 session authority (D2), for the base and the exterior alike. It owns the only
    /// <see cref="IZonedCrewRoster"/> (D3, the phase 2 roster), the only base model (<see cref="IOutpostSimulation"/>, D1)
    /// and the only EVA model (<see cref="IEvaSimulation"/>, D1), and is their only caller. It alone decides zones,
    /// sealing, activity, task validation/reservation/progress/completion, airlock cycles, advisory reception,
    /// protection and the result. It never adds or removes stock (the models do, in Advance and Apply) and never
    /// moves crew (the roster does, against the plan plus the closed-hatch overlay this authority sets).
    ///
    /// One step (see <see cref="Tick"/>): crew moves; zones are confirmed (zone sequences advance) and tasks whose
    /// performer left reach or zone end (PerformerMovedAway); activities are derived; the base advances with the
    /// demand of the UNSEALED crew and of the service loads (tasks, charger, pump, ventilation) and the EVA model with
    /// the sealed suits only; tasks and the airlock cycle are credited with fully served seconds; completed tasks apply
    /// their effects once; advisories are issued and delivered by coverage; protection is observed; the end is
    /// evaluated. Single-threaded; state and command record are committed before events; subscriber exceptions
    /// propagate. Same command/epoch/revision rules as phase 2.
    /// </summary>
    public sealed class ExpeditionAuthority : IExpeditionAuthority
    {
        public const double DefaultMaxStepSeconds = 0.25;
        public const double MinimumWorkStepSeconds = 1e-3;
        public const double ServiceTolerance = 1e-9;
        private const double AmountTolerance = 1e-9;

        private sealed class StoredCommand
        {
            public PlayerId Sender;
            public ExpeditionCommand Command;
            public CommandResult Result;
        }

        private sealed class Member
        {
            public CrewZone Zone;
            public long ZoneSequence;
            public int Suit = -1;
            public SuitActivity Activity;
            public ProtectionState Protection;
        }

        private sealed class SuitEquipment
        {
            public SuitStatus Status;
            public PlayerId Wearer;
            public ChecklistSteps Completed;
        }

        private readonly IOutpostSimulation baseModel;
        /// <summary>Close policy and step hooks of the phase 4 continuous authority; null = the finite phase 3 rules (unchanged).</summary>
        private readonly IExpeditionSessionPolicy policy;
        private readonly IEvaSimulation eva;
        private readonly IZonedCrewRoster roster;
        private readonly double maxStepSeconds;
        private readonly OutpostTaskBoard board;
        private readonly ReturnRouteField route;
        private readonly AdvisoryTimeline timeline;
        private readonly ProtectionTracker protection = new ProtectionTracker();
        private readonly Dictionary<Guid, StoredCommand> commands = new Dictionary<Guid, StoredCommand>();
        private readonly Dictionary<PlayerId, Member> members = new Dictionary<PlayerId, Member>();
        private readonly SuitEquipment[] suits;
        private readonly int[] objectiveCounts;
        private readonly List<ExpeditionIncident> incidents = new List<ExpeditionIncident>();
        private readonly HashSet<(int, SuitService)> reserveRecorded = new HashSet<(int, SuitService)>();
        private List<ProtectionAssessment> assessments = new List<ProtectionAssessment>();

        private long epoch;
        private long revision;
        private SessionState state = SessionState.Ready;
        private ExpeditionOutcome outcome = ExpeditionOutcome.InProgress;
        private ExpeditionPhase phase = ExpeditionPhase.Preparing;
        private ReturnDecision decision;
        private AirlockState airlockState = AirlockState.PressurizedInnerOpen;
        private long cycleSequence;
        private double cycleWork, cycleDuration;
        private bool cycleStalled;
        private PlayerId cycleRequestedBy;
        private bool shelterPrepared, ventilationRunning, shelterServiceAvailable;
        private ExpeditionSessionSnapshot snapshot;

        public event Action<ExpeditionSessionSnapshot> SnapshotChanged;
        public event Action<PlayerId, CommandResult> CommandCompleted;

        public ExpeditionScenario Scenario { get; }
        public ExpeditionOutcome Outcome => outcome;
        /// <summary>Walking speed on suit supply, m/s.</summary>
        public double SealedSpeedMetersPerSecond => Scenario.Outpost.Movement.MaxSpeedMetersPerSecond * Scenario.SuitedSpeedFactor;
        public ReturnRouteField Route => route;

        /// <param name="roster">D3's roster built for the scenario's layout and movement settings; must be empty.</param>
        public ExpeditionAuthority(ExpeditionScenario scenario, IOutpostSimulation baseModel, IEvaSimulation eva,
            IZonedCrewRoster roster, double maxStepSeconds = DefaultMaxStepSeconds)
            : this(scenario, baseModel, eva, roster, maxStepSeconds, null)
        {
        }

        /// <summary>Phase 4: the same single authority with an injected close policy and step hooks (see
        /// <see cref="IExpeditionSessionPolicy"/>). Only the phase 4 continuous authority uses it.</summary>
        internal ExpeditionAuthority(ExpeditionScenario scenario, IOutpostSimulation baseModel, IEvaSimulation eva,
            IZonedCrewRoster roster, double maxStepSeconds, IExpeditionSessionPolicy policy)
        {
            this.policy = policy;
            Scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
            this.baseModel = baseModel ?? throw new ArgumentNullException(nameof(baseModel));
            this.eva = eva ?? throw new ArgumentNullException(nameof(eva));
            this.roster = roster ?? throw new ArgumentNullException(nameof(roster));
            if (double.IsNaN(maxStepSeconds) || double.IsInfinity(maxStepSeconds) || maxStepSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxStepSeconds));
            if (roster.Members == null || roster.Members.Count != 0) throw new ArgumentException("The roster must start empty.", nameof(roster));
            this.maxStepSeconds = maxStepSeconds;
            board = new OutpostTaskBoard(scenario.Layout);
            route = new ReturnRouteField(scenario);
            timeline = new AdvisoryTimeline(scenario.Episode);
            suits = new SuitEquipment[scenario.Eva.Suits.Count];
            for (int i = 0; i < suits.Length; i++) suits[i] = new SuitEquipment();
            objectiveCounts = new int[scenario.Rules.Objectives.Count];
            baseModel.Reset(scenario.Outpost.Simulation);
            eva.Reset(scenario.Eva);
            ApplyHatches();
            snapshot = Build();
        }

        public bool TryGetSnapshot(out ExpeditionSessionSnapshot value)
        {
            value = snapshot;
            return true;
        }

        private double Now => RequireBase().Power.SimulatedTimeSeconds;

        // ---------------------------------------------------------------- presence and movement

        public OutpostMembership Join()
        {
            OutpostMembership membership = roster.Join() ?? throw new InvalidOperationException("Roster Join returned null.");
            if (!membership.IsJoined) return membership;
            CrewMemberState pose = FindPose(membership.Player);
            members[membership.Player] = new Member { Zone = ZoneOf(pose.Pose.Position) };
            // A newcomer appears inside the base (covered by the base radio): current advisories reach them now.
            timeline.Deliver(membership.Player, true, Now);
            RefreshProtection(Now);
            Commit();
            SnapshotChanged?.Invoke(snapshot);
            return membership;
        }

        public bool Leave(PlayerId player)
        {
            double now = Now;
            bool hadTask = false;
            if (board.TryGetForPlayer(player, out OutpostTaskBoard.ActiveTask task))
            {
                board.End(task.Station, TaskEndReason.PerformerLeft, now);
                hadTask = true;
            }
            if (policy != null && policy.PlayerLeft(player, now)) hadTask = true;
            if (members.TryGetValue(player, out Member member))
            {
                if (member.Suit >= 0)
                {
                    SuitEquipment suit = suits[member.Suit];
                    if (suit.Status == SuitStatus.Sealed)
                    {
                        // Nobody is left inside: the suit stops consuming and stays where it was until Restart.
                        suit.Status = SuitStatus.Abandoned;
                        AddIncident(new ExpeditionIncident(IncidentKind.DisconnectedWhileSealed, player, now,
                            Scenario.Eva.Suits[member.Suit].Key, null));
                    }
                    else suit.Status = SuitStatus.Stowed; // unsealed inside the base: back on its rack, levels kept
                    suit.Wearer = PlayerId.None;
                    if (suit.Status == SuitStatus.Stowed) suit.Completed = ChecklistSteps.None;
                }
                members.Remove(player);
                protection.Remove(player);
            }
            bool left = roster.Leave(player);
            if (!left && !hadTask) return false;
            Commit();
            SnapshotChanged?.Invoke(snapshot);
            return left;
        }

        public MovementInputDisposition SubmitMovement(PlayerId sender, MovementInput input)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            return roster.SubmitMovement(sender, input, epoch);
        }

        // ---------------------------------------------------------------- commands

        public void Submit(PlayerId sender, ExpeditionCommand command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (command.Id == Guid.Empty)
            {
                CommandCompleted?.Invoke(sender, Reject(command, OutpostReasons.InvalidCommandId));
                return;
            }
            if (commands.TryGetValue(command.Id, out StoredCommand stored))
            {
                bool same = stored.Sender == sender && stored.Command.HasSameContent(command);
                CommandCompleted?.Invoke(sender, same ? stored.Result : Reject(command, OutpostReasons.CommandIdConflict));
                return;
            }
            if (policy != null && policy.OwnsCommandId(command.Id))
            {
                CommandCompleted?.Invoke(sender, Reject(command, OutpostReasons.CommandIdConflict));
                return;
            }

            string rejection = Validate(sender, command);
            bool changed = false;
            Exception fault = null;
            if (rejection == null)
            {
                try
                {
                    Apply(sender, command);
                }
                catch (Exception e)
                {
                    fault = e;
                    rejection = OutpostReasons.SimulationFault;
                    EnterFault();
                }
                changed = true;
            }
            var result = new CommandResult(command.Id, command.Epoch, rejection == null, rejection ?? OutpostReasons.Accepted);
            commands.Add(command.Id, new StoredCommand { Sender = sender, Command = command, Result = result });
            try
            {
                if (changed) SnapshotChanged?.Invoke(snapshot);
            }
            finally
            {
                CommandCompleted?.Invoke(sender, result);
            }
            if (fault != null) ExceptionDispatchInfo.Capture(fault).Throw();
        }

        private static CommandResult Reject(ExpeditionCommand command, string reason) =>
            new CommandResult(command.Id, command.Epoch, false, reason);

        private bool Ended => state == SessionState.Completed || state == SessionState.Interrupted;

        /// <summary>Validation in the order fixed by docs/phase-03/SESSION_CONTRACT.md; the first failure is the reason.</summary>
        private string Validate(PlayerId sender, ExpeditionCommand command)
        {
            CrewMemberState pose = sender.IsValid ? FindPose(sender) : null;
            if (pose == null || !members.TryGetValue(sender, out Member member)) return OutpostReasons.NotJoined;
            if (command.Epoch != epoch) return OutpostReasons.EpochMismatch;
            string shape = command.ShapeError();
            if (shape != null) return shape;
            switch (command.Action)
            {
                case ExpeditionAction.Restart:
                    return null;
                case ExpeditionAction.Pause:
                    if (Ended) return OutpostReasons.SessionEnded;
                    return state == SessionState.Running ? null : OutpostReasons.InvalidState;
                case ExpeditionAction.Resume:
                    if (Ended) return OutpostReasons.SessionEnded;
                    return state == SessionState.Ready || state == SessionState.Paused ? null : OutpostReasons.InvalidState;
                case ExpeditionAction.StartTask:
                    return ValidateStart(sender, pose, member, command);
                case ExpeditionAction.CancelTask:
                    if (Ended) return OutpostReasons.SessionEnded;
                    if (state != SessionState.Running && state != SessionState.Paused) return OutpostReasons.InvalidState;
                    if (!Scenario.Layout.TryGetStation(command.Station, out _)) return OutpostReasons.UnknownStation;
                    if (!board.TryGetAtStation(command.Station, out OutpostTaskBoard.ActiveTask active) || active.Performer != sender)
                        return OutpostReasons.NoActiveTask;
                    return null;
                case ExpeditionAction.CycleAirlock:
                    return ValidateCycle(pose, member, command);
                case ExpeditionAction.DeclareReturn:
                    if (Ended) return OutpostReasons.SessionEnded;
                    if (state != SessionState.Running && state != SessionState.Paused) return OutpostReasons.InvalidState;
                    if (!AnySealed()) return ExpeditionReasons.NoExpeditionUnderway;
                    if (decision != null) return ExpeditionReasons.ReturnAlreadyDeclared;
                    return null;
                default:
                    return OutpostReasons.UnknownAction;
            }
        }

        private string ValidateStart(PlayerId sender, CrewMemberState pose, Member member, ExpeditionCommand command)
        {
            if (Ended) return OutpostReasons.SessionEnded;
            if (state != SessionState.Running) return OutpostReasons.InvalidState;
            if (!Scenario.Layout.TryGetStation(command.Station, out StationPlacement placement)) return OutpostReasons.UnknownStation;
            if (!Scenario.Outpost.TryGetTask(command.Task, out TaskDefinition definition)) return OutpostReasons.UnknownTaskType;
            if (definition.Station != command.Station) return OutpostReasons.TaskNotOfferedAtStation;
            if (board.TryGetForPlayer(sender, out _) || (policy != null && policy.IsPlayerBusy(sender))) return OutpostReasons.PlayerBusy;
            if (board.TryGetAtStation(command.Station, out _)) return OutpostReasons.StationOccupied;
            if (command.ZoneSequence != ExpeditionCommand.Unstated && command.ZoneSequence != member.ZoneSequence)
                return ExpeditionReasons.StaleZone;
            if (!placement.IsWithinReach(pose.Pose.Position)) return OutpostReasons.OutOfRange;
            ExpeditionTaskRule rule = Scenario.RuleFor(command.Task);
            if (member.Zone != rule.RequiredZone) return ExpeditionReasons.WrongZone;
            string suitRule = CheckSuitRule(member, rule);
            if (suitRule != null) return suitRule;
            if (rule.Purpose == TaskPurpose.ExteriorWork && phase == ExpeditionPhase.Returning) return ExpeditionReasons.ReturnDeclared;
            if (!CanApplyAtStart(member, definition, rule)) return OutpostReasons.InsufficientResources;
            return null;
        }

        private string CheckSuitRule(Member member, ExpeditionTaskRule rule)
        {
            bool sealedNow = IsSealed(member);
            switch (rule.Suit)
            {
                case SuitRequirement.NotSealed:
                    return sealedNow ? ExpeditionReasons.SealedNotAllowed : null;
                case SuitRequirement.Unsuited:
                    if (member.Suit >= 0) return ExpeditionReasons.AlreadySuited;
                    return suits[Scenario.Eva.IndexOf(rule.SuitKey)].Status == SuitStatus.Stowed ? null : ExpeditionReasons.SuitNotOnRack;
                case SuitRequirement.SuitWorn:
                {
                    if (member.Suit < 0) return ExpeditionReasons.SuitRequired;
                    if (sealedNow) return ExpeditionReasons.SealedNotAllowed;
                    ChecklistSteps done = suits[member.Suit].Completed;
                    ChecklistSteps needed = Checklist.Prerequisites(rule.Step.Value);
                    if ((done & needed) != needed) return ExpeditionReasons.ChecklistOutOfOrder;
                    if ((done & Checklist.Flag(rule.Step.Value)) != 0) return ExpeditionReasons.ChecklistStepDone;
                    return null;
                }
                case SuitRequirement.Sealed:
                    return sealedNow ? null : ExpeditionReasons.SuitRequired;
                default:
                    return OutpostReasons.InvalidActionValue;
            }
        }

        /// <summary>Start-time prediction (reserves nothing; completion decides again, all or nothing).</summary>
        private bool CanApplyAtStart(Member member, TaskDefinition definition, ExpeditionTaskRule rule)
        {
            if (!OutpostMissionEvaluator.CanApply(RequireBase().LifeSupport, definition.CompletionEffect)) return false;
            if (rule.Purpose == TaskPurpose.SuitPreparation && rule.Step == ChecklistStep.FillConsumables)
            {
                FillAmounts(member.Suit, out double oxygen, out double water);
                EvaSnapshot evaNow = eva.Snapshot;
                if (evaNow.Supply.OxygenRechargeKilograms + AmountTolerance < oxygen) return false;
                if (RequireBase().LifeSupport.Water.PotableKilograms + AmountTolerance < water) return false;
            }
            return true;
        }

        private string ValidateCycle(CrewMemberState pose, Member member, ExpeditionCommand command)
        {
            if (Ended) return OutpostReasons.SessionEnded;
            if (state != SessionState.Running) return OutpostReasons.InvalidState;
            if (!Scenario.Layout.TryGetStation(command.Station, out StationPlacement panel)) return OutpostReasons.UnknownStation;
            if (!Scenario.Airlock.IsPanel(command.Station)) return ExpeditionReasons.NotAnAirlockPanel;
            if (command.ZoneSequence != ExpeditionCommand.Unstated && command.ZoneSequence != member.ZoneSequence)
                return ExpeditionReasons.StaleZone;
            if (command.AirlockCycle != ExpeditionCommand.Unstated && command.AirlockCycle != cycleSequence)
                return ExpeditionReasons.StaleAirlockState;
            if (!panel.IsWithinReach(pose.Pose.Position)) return OutpostReasons.OutOfRange;
            if (IsCycling) return ExpeditionReasons.AirlockBusy;
            if (!roster.IsClear(Scenario.Airlock.InnerHatch) || !roster.IsClear(Scenario.Airlock.OuterHatch))
                return ExpeditionReasons.AirlockObstructed;
            List<PlayerId> occupants = Occupants();
            if (occupants.Count > Scenario.Airlock.Capacity) return ExpeditionReasons.AirlockOverCapacity;
            if (airlockState == AirlockState.PressurizedInnerOpen)
            {
                foreach (PlayerId occupant in occupants)
                {
                    Member m = members[occupant];
                    if (m.Suit < 0 || suits[m.Suit].Status != SuitStatus.Worn || suits[m.Suit].Completed != ChecklistSteps.All)
                        return ExpeditionReasons.SuitNotReady;
                }
                if (occupants.Count > 0 && timeline.Instruction(Now).InForce) return ExpeditionReasons.EgressBlockedByInstruction;
            }
            else if (Scenario.Airlock.MakeupGasPerCycleKilograms > 0d
                && eva.Snapshot.Supply.MakeupGasKilograms + AmountTolerance < Scenario.Airlock.MakeupGasPerCycleKilograms)
                return OutpostReasons.InsufficientResources;
            return null;
        }

        private void Apply(PlayerId sender, ExpeditionCommand command)
        {
            switch (command.Action)
            {
                case ExpeditionAction.Restart:
                    Restart();
                    return;
                case ExpeditionAction.Pause:
                    state = SessionState.Paused;
                    break;
                case ExpeditionAction.Resume:
                    state = SessionState.Running;
                    break;
                case ExpeditionAction.StartTask:
                    Scenario.Outpost.TryGetTask(command.Task, out TaskDefinition definition);
                    board.Reserve(command.Station, definition, sender, command.Id);
                    break;
                case ExpeditionAction.CancelTask:
                    board.End(command.Station, TaskEndReason.CancelledByPerformer, Now);
                    break;
                case ExpeditionAction.CycleAirlock:
                    StartCycle(sender, command.Id);
                    break;
                case ExpeditionAction.DeclareReturn:
                    decision = new ReturnDecision(sender, Now, timeline.KnownBy(sender), TeamWorstMargin());
                    phase = ExpeditionPhase.Returning;
                    break;
                default:
                    throw new InvalidOperationException("Unvalidated action.");
            }
            Commit();
        }

        // ---------------------------------------------------------------- airlock

        private bool IsCycling => airlockState == AirlockState.Depressurizing || airlockState == AirlockState.Repressurizing;

        private void StartCycle(PlayerId requester, Guid id)
        {
            bool depressurize = airlockState == AirlockState.PressurizedInnerOpen;
            List<PlayerId> occupants = Occupants();
            if (!depressurize && Scenario.Airlock.MakeupGasPerCycleKilograms > 0d)
            {
                EvaTransferStatus gas = eva.Apply(new EvaTransfer(id, EvaTransferKind.UseMakeupGas, -1, Scenario.Airlock.MakeupGasPerCycleKilograms));
                if (gas != EvaTransferStatus.Applied) throw new InvalidOperationException($"Make-up gas transfer returned {gas}.");
            }
            airlockState = depressurize ? AirlockState.Depressurizing : AirlockState.Repressurizing;
            cycleSequence = checked(cycleSequence + 1);
            cycleWork = 0d;
            cycleDuration = depressurize ? Scenario.Airlock.DepressurizeSeconds : Scenario.Airlock.RepressurizeSeconds;
            cycleStalled = false;
            cycleRequestedBy = requester;
            ApplyHatches(); // validated: both doorways are clear
            if (depressurize)
                foreach (PlayerId occupant in occupants) Seal(occupant);
        }

        private void FinishCycle()
        {
            if (airlockState == AirlockState.Depressurizing) airlockState = AirlockState.DepressurizedOuterOpen;
            else
            {
                airlockState = AirlockState.PressurizedInnerOpen;
                foreach (PlayerId occupant in Occupants())
                    if (IsSealed(members[occupant])) Unseal(occupant);
            }
            cycleWork = 0d;
            cycleDuration = 0d;
            cycleStalled = false;
            cycleRequestedBy = PlayerId.None;
            ApplyHatches(); // opening a hatch only removes an obstacle
        }

        private void ApplyHatches()
        {
            var closed = new List<BaseRect>(2);
            if (airlockState != AirlockState.PressurizedInnerOpen) closed.Add(Scenario.Airlock.InnerHatch);
            if (airlockState != AirlockState.DepressurizedOuterOpen) closed.Add(Scenario.Airlock.OuterHatch);
            roster.SetObstacleOverlay(closed);
        }

        private void Seal(PlayerId player)
        {
            Member member = members[player];
            SuitEquipment suit = suits[member.Suit];
            suit.Status = SuitStatus.Sealed;
            roster.SetSpeedFactor(player, Scenario.SuitedSpeedFactor);
            if (phase == ExpeditionPhase.Preparing)
            {
                phase = ExpeditionPhase.Underway;
                policy?.SortieStarted(player, Now);
            }
        }

        private void Unseal(PlayerId player)
        {
            Member member = members[player];
            SuitEquipment suit = suits[member.Suit];
            suit.Status = SuitStatus.Worn;
            suit.Completed = ChecklistSteps.Don; // consumables were used: recharge and recheck before another egress
            roster.SetSpeedFactor(player, 1d);
        }

        // ---------------------------------------------------------------- restart

        /// <summary>New epoch at revision 0: models reset, tasks and ends erased without effects, suits back on their
        /// racks, airlock pressurized, advisories forgotten, crew back to spawn (ids, slots, sequences kept).</summary>
        private void Restart()
        {
            long next = checked(epoch + 1);
            baseModel.Reset(Scenario.Outpost.Simulation); // may throw: nothing else has changed yet
            eva.Reset(Scenario.Eva);
            policy?.Restarted();
            board.Clear();
            Array.Clear(objectiveCounts, 0, objectiveCounts.Length);
            roster.ResetForRestart();
            airlockState = AirlockState.PressurizedInnerOpen;
            cycleSequence = 0;
            cycleWork = cycleDuration = 0d;
            cycleStalled = false;
            cycleRequestedBy = PlayerId.None;
            ApplyHatches();
            foreach (SuitEquipment suit in suits)
            {
                suit.Status = SuitStatus.Stowed;
                suit.Wearer = PlayerId.None;
                suit.Completed = ChecklistSteps.None;
            }
            foreach (CrewMemberState pose in roster.Members)
            {
                Member member = members[pose.Player];
                CrewZone zone = ZoneOf(pose.Pose.Position);
                if (zone != member.Zone) member.ZoneSequence = checked(member.ZoneSequence + 1);
                member.Zone = zone;
                member.Suit = -1;
                member.Activity = SuitActivity.Rest;
                member.Protection = ProtectionState.NotRequired;
            }
            shelterPrepared = ventilationRunning = shelterServiceAvailable = false;
            timeline.Reset();
            protection.Reset();
            decision = null;
            phase = ExpeditionPhase.Preparing;
            assessments = new List<ProtectionAssessment>();
            incidents.Clear();
            reserveRecorded.Clear();
            epoch = next;
            revision = 0;
            state = SessionState.Ready;
            outcome = ExpeditionOutcome.InProgress;
            snapshot = Build();
        }

        // ---------------------------------------------------------------- clock

        /// <summary>
        /// Advances the mission by simulated seconds (finite, nonnegative). Only the authority's runner calls it. Does
        /// nothing unless Running; publishes at most one snapshot per call. If a model or the roster throws, completed
        /// sub-steps are kept, the mission becomes Interrupted (SimulationFault) and the exception is rethrown.
        /// </summary>
        public void Tick(double elapsedSimulatedSeconds)
        {
            if (double.IsNaN(elapsedSimulatedSeconds) || double.IsInfinity(elapsedSimulatedSeconds) || elapsedSimulatedSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(elapsedSimulatedSeconds));
            if (elapsedSimulatedSeconds == 0 || state != SessionState.Running) return;

            double remaining = elapsedSimulatedSeconds;
            bool progressed = false;
            try
            {
                while (remaining > 0 && outcome == ExpeditionOutcome.InProgress)
                {
                    double now = Now;
                    double step = NextStep(remaining, now);
                    double boundary = baseModel.SecondsUntilServiceChange(BaseDemand());
                    if (boundary == 0d)
                    {
                        outcome = Evaluate(now);
                        if (outcome != ExpeditionOutcome.InProgress)
                        {
                            progressed = true;
                            break;
                        }
                    }
                    else if (boundary > 0d) step = Math.Min(step, boundary);

                    roster.Advance(step);
                    progressed = true;
                    ConfirmZonesAndReach(now);
                    DeriveActivities();
                    ventilationRunning = shelterPrepared && ShelterOccupants() > 0;
                    EvaDemand suitsDemand = SuitsDemand();
                    policy?.ConfirmReach(now);
                    OutpostDemand baseDemand = BaseDemand();
                    policy?.BeforeBaseAdvance(step, baseDemand, now);
                    baseModel.Advance(step, baseDemand);
                    double after = Now;
                    double elapsed = after - now;
                    if (!(elapsed > 0)) break; // a model whose clock does not move would loop forever
                    eva.Advance(elapsed, suitsDemand);

                    double service = Clamp(baseModel.LastFullyServedServiceSeconds, elapsed);
                    double science = Clamp(baseModel.LastFullyServedScienceSeconds, elapsed);
                    shelterServiceAvailable = ventilationRunning && service >= elapsed * (1d - ServiceTolerance);
                    CreditTasks(elapsed, service, science, after);
                    CreditCycle(elapsed, service);
                    policy?.AfterStep(elapsed, service, after);
                    RefreshProtection(after);   // observed with the instruction known during the step
                    timeline.IssueDue(after);
                    DeliverAdvisories(after);
                    RefreshProtection(after);   // published with what is known now
                    RecordReserveUse(after);
                    remaining -= step;
                    outcome = Evaluate(after);
                }
            }
            catch
            {
                EnterFault();
                try { SnapshotChanged?.Invoke(snapshot); }
                catch { /* the model failure is the error reported to the caller */ }
                throw;
            }

            if (!progressed) return;
            if (outcome != ExpeditionOutcome.InProgress) Close();
            Commit();
            SnapshotChanged?.Invoke(snapshot);
        }

        private double NextStep(double remaining, double now)
        {
            double step = Math.Min(remaining, maxStepSeconds);
            double timeLeft = Scenario.Rules.MissionDurationSeconds - now;
            if (timeLeft > 0 && (policy == null || !policy.Continuous)) step = Math.Min(step, timeLeft);
            if (policy != null)
            {
                double hook = policy.SecondsUntilEvent(BaseDemand(), now);
                if (hook > AdvisoryTimeline.TimeToleranceSeconds && !double.IsInfinity(hook)) step = Math.Min(step, hook);
                else if (hook > 0d && hook <= AdvisoryTimeline.TimeToleranceSeconds) step = Math.Min(step, MinimumWorkStepSeconds);
            }
            foreach (OutpostTaskBoard.ActiveTask task in board.Active())
                step = Math.Min(step, Math.Max(task.RemainingSeconds, MinimumWorkStepSeconds));
            if (IsCycling) step = Math.Min(step, Math.Max(cycleDuration - cycleWork, MinimumWorkStepSeconds));
            double advisory = timeline.NextEventAfter(now) - now;
            if (advisory > AdvisoryTimeline.TimeToleranceSeconds) step = Math.Min(step, advisory);
            double suitEvent = eva.SecondsUntilServiceEvent(SuitsDemand());
            if (suitEvent > AdvisoryTimeline.TimeToleranceSeconds && !double.IsInfinity(suitEvent)) step = Math.Min(step, suitEvent);
            return step;
        }

        private void ConfirmZonesAndReach(double now)
        {
            foreach (CrewMemberState pose in roster.Members)
            {
                Member member = members[pose.Player];
                CrewZone zone = ZoneOf(pose.Pose.Position);
                if (zone == member.Zone) continue;
                member.Zone = zone;
                member.ZoneSequence = checked(member.ZoneSequence + 1);
            }
            foreach (OutpostTaskBoard.ActiveTask task in board.Active())
            {
                Scenario.Layout.TryGetStation(task.Station, out StationPlacement placement);
                CrewMemberState pose = FindPose(task.Performer);
                bool inZone = pose != null && members[task.Performer].Zone == Scenario.RuleFor(task.Definition.Id).RequiredZone;
                if (pose == null || !placement.IsWithinReach(pose.Pose.Position) || !inZone)
                    board.End(task.Station, TaskEndReason.PerformerMovedAway, now);
            }
        }

        private void DeriveActivities()
        {
            foreach (CrewMemberState pose in roster.Members)
            {
                Member member = members[pose.Player];
                bool working = board.TryGetForPlayer(pose.Player, out OutpostTaskBoard.ActiveTask task)
                    && Scenario.RuleFor(task.Definition.Id).Purpose == TaskPurpose.ExteriorWork;
                member.Activity = working ? SuitActivity.Work : pose.Pose.Motion == CrewMotion.Walking ? SuitActivity.Walk : SuitActivity.Rest;
            }
        }

        /// <summary>Base demand: the UNSEALED crew breathes base air; service loads of tasks, airlock pump and shelter
        /// ventilation join the phase 2 service channel; science tasks request the phase 1 science channel.</summary>
        private OutpostDemand BaseDemand()
        {
            int aboard = 0;
            foreach (Member member in members.Values)
                if (!IsSealed(member)) aboard++;
            bool science = false;
            double serviceWatts = 0d;
            foreach (OutpostTaskBoard.ActiveTask task in board.Active())
            {
                if (task.Definition.Category == TaskCategory.Science) science = true;
                else serviceWatts += task.Definition.PowerWatts;
            }
            if (IsCycling) serviceWatts += Scenario.Airlock.PumpPowerWatts;
            if (ventilationRunning) serviceWatts += Scenario.Shelter.VentilationWatts;
            if (policy != null) serviceWatts += policy.ServiceWatts();
            return new OutpostDemand(aboard, science, serviceWatts);
        }

        /// <summary>EVA demand: exactly the sealed suits, with their wearer's activity and exterior tool load.</summary>
        private EvaDemand SuitsDemand()
        {
            var list = new List<SuitDemand>();
            for (int i = 0; i < suits.Length; i++)
            {
                if (suits[i].Status != SuitStatus.Sealed) continue;
                Member member = members[suits[i].Wearer];
                list.Add(new SuitDemand(i, member.Activity, ToolWattsOf(suits[i].Wearer, member)));
            }
            return new EvaDemand(list);
        }

        private double ToolWattsOf(PlayerId player, Member member)
        {
            if (member.Activity != SuitActivity.Work || !board.TryGetForPlayer(player, out OutpostTaskBoard.ActiveTask task)) return 0d;
            return Scenario.RuleFor(task.Definition.Id).SuitToolWatts;
        }

        /// <summary>Progress = fully served seconds: science channel for Science, service channel for Support with base
        /// power, elapsed time for Support at 0 W; exterior work advances while the performer's suit has every service.</summary>
        private void CreditTasks(double elapsed, double service, double science, double now)
        {
            foreach (OutpostTaskBoard.ActiveTask task in board.Active())
            {
                TaskDefinition definition = task.Definition;
                ExpeditionTaskRule rule = Scenario.RuleFor(definition.Id);
                double credit;
                if (rule.Purpose == TaskPurpose.ExteriorWork) credit = SuitServing(task.Performer) ? elapsed : 0d;
                else credit = definition.Category == TaskCategory.Science ? science : definition.PowerWatts > 0d ? service : elapsed;
                task.Stalled = credit < elapsed * (1d - ServiceTolerance);
                task.WorkSeconds = Math.Min(definition.DurationSeconds, task.WorkSeconds + credit);
                if (task.RemainingSeconds <= OutpostMissionEvaluator.TimeToleranceSeconds)
                {
                    task.WorkSeconds = definition.DurationSeconds;
                    Complete(task, rule, now);
                }
            }
        }

        private void CreditCycle(double elapsed, double service)
        {
            if (!IsCycling) return;
            double credit = Scenario.Airlock.PumpPowerWatts > 0d ? service : elapsed;
            cycleStalled = credit < elapsed * (1d - ServiceTolerance);
            cycleWork = Math.Min(cycleDuration, cycleWork + credit);
            if (cycleDuration - cycleWork <= OutpostMissionEvaluator.TimeToleranceSeconds) FinishCycle();
        }

        private void Complete(OutpostTaskBoard.ActiveTask task, ExpeditionTaskRule rule, double now)
        {
            TaskDefinition definition = task.Definition;
            Member member = members[task.Performer];
            bool done;
            switch (rule.Purpose)
            {
                case TaskPurpose.BaseOperation:
                    done = ApplyBaseEffect(task.TransferId, definition.CompletionEffect);
                    break;
                case TaskPurpose.SuitPreparation:
                    done = CompleteChecklistStep(task, rule, member);
                    break;
                case TaskPurpose.ShelterPreparation:
                    shelterPrepared = true;
                    done = true;
                    break;
                default:
                    done = true; // exterior work moves no stock; its suit cost was consumed while it progressed
                    break;
            }
            board.End(task.Station, done ? TaskEndReason.Completed : TaskEndReason.InsufficientResources, now);
            if (!done) return;
            for (int i = 0; i < objectiveCounts.Length; i++)
                if (Scenario.Rules.Objectives[i].Counts(definition.Id)) objectiveCounts[i]++;
            policy?.TaskCompleted(definition.Id, task.Performer, now);
        }

        private bool ApplyBaseEffect(Guid id, ResourceEffect effect)
        {
            if (effect.IsEmpty) return true;
            ResourceTransferStatus status = baseModel.Apply(new ResourceTransfer(id, effect));
            if (status == ResourceTransferStatus.Applied) return true;
            if (status == ResourceTransferStatus.InsufficientStock || status == ResourceTransferStatus.ExceedsCapacity) return false;
            throw new InvalidOperationException($"IOutpostSimulation.Apply returned {status} for a new transfer.");
        }

        private bool CompleteChecklistStep(OutpostTaskBoard.ActiveTask task, ExpeditionTaskRule rule, Member member)
        {
            switch (rule.Step.Value)
            {
                case ChecklistStep.Don:
                {
                    int index = Scenario.Eva.IndexOf(rule.SuitKey);
                    if (member.Suit >= 0 || suits[index].Status != SuitStatus.Stowed) return false;
                    suits[index].Status = SuitStatus.Worn;
                    suits[index].Wearer = task.Performer;
                    suits[index].Completed = ChecklistSteps.Don;
                    member.Suit = index;
                    return true;
                }
                case ChecklistStep.ChargeBattery:
                {
                    if (member.Suit < 0) return false;
                    double delivered = task.Definition.PowerWatts * task.Definition.DurationSeconds * rule.ChargeEfficiency;
                    Expect(eva.Apply(new EvaTransfer(task.TransferId, EvaTransferKind.ChargeBattery, member.Suit, delivered)));
                    suits[member.Suit].Completed |= ChecklistSteps.ChargeBattery;
                    return true;
                }
                case ChecklistStep.FillConsumables:
                {
                    if (member.Suit < 0) return false;
                    FillAmounts(member.Suit, out double oxygen, out double water);
                    if (eva.Snapshot.Supply.OxygenRechargeKilograms + AmountTolerance < oxygen) return false;
                    if (water > 0d)
                    {
                        // Base potable water leaves the base boundary into the suit: one balanced phase 2 transfer, then
                        // the suit side with the same id. Checked before, so the suit side cannot fail after the base one.
                        var effect = new ResourceEffect(new[] { new ResourceChange(ResourceKind.PotableWater, -water) }, water);
                        if (!ApplyBaseEffect(task.TransferId, effect)) return false;
                        Expect(eva.Apply(new EvaTransfer(task.TransferId, EvaTransferKind.FillFeedwater, member.Suit, water)));
                    }
                    if (oxygen > 0d)
                        Expect(eva.Apply(new EvaTransfer(TransferIds.Derive(task.TransferId, 1), EvaTransferKind.FillOxygen, member.Suit, oxygen)));
                    suits[member.Suit].Completed |= ChecklistSteps.FillConsumables;
                    return true;
                }
                case ChecklistStep.SealCheck:
                {
                    if (member.Suit < 0) return false;
                    suits[member.Suit].Completed |= ChecklistSteps.SealCheck;
                    return true;
                }
                default:
                    return false;
            }
        }

        private static void Expect(EvaTransferStatus status)
        {
            if (status != EvaTransferStatus.Applied)
                throw new InvalidOperationException($"IEvaSimulation.Apply returned {status} for a checked new transfer.");
        }

        /// <summary>What a fill moves into <paramref name="suitIndex"/>: each deficit to capacity (0 when full).</summary>
        private void FillAmounts(int suitIndex, out double oxygen, out double water)
        {
            oxygen = water = 0d;
            if (suitIndex < 0) return;
            SuitLevels levels = eva.Snapshot.Suits[suitIndex].Levels;
            SuitModelConfiguration model = Scenario.Eva.Model;
            oxygen = Deficit(model.Get(SuitService.Oxygen).Capacity, levels.OxygenKilograms);
            water = Deficit(model.Get(SuitService.Feedwater).Capacity, levels.FeedwaterKilograms);
        }

        private static double Deficit(double capacity, double level)
        {
            double deficit = capacity - level;
            return deficit > AmountTolerance * Math.Max(1d, capacity) ? deficit : 0d;
        }

        private bool SuitServing(PlayerId player)
        {
            Member member = members[player];
            return member.Suit >= 0 && !eva.Snapshot.Suits[member.Suit].IsDepleted;
        }

        private void DeliverAdvisories(double now)
        {
            foreach (CrewMemberState pose in roster.Members)
                timeline.Deliver(pose.Player, Scenario.Comms.Covers(members[pose.Player].Zone, pose.Pose.Position), now);
        }

        private void RefreshProtection(double now)
        {
            ShelterInstructionStatus instruction = timeline.Instruction(now);
            foreach (CrewMemberState pose in roster.Members)
            {
                Member member = members[pose.Player];
                member.Protection = !instruction.InForce ? ProtectionState.NotRequired
                    : member.Zone != CrewZone.Shelter ? ProtectionState.NotInShelter
                    : !shelterPrepared ? ProtectionState.ShelterNotPrepared
                    : !shelterServiceAvailable ? ProtectionState.ShelterServiceUnavailable
                    : ProtectionState.Protected;
                protection.Observe(pose.Player, member.Protection, timeline.FirstInstructionReception(pose.Player), instruction, now);
            }
        }

        private void RecordReserveUse(double now)
        {
            EvaSnapshot readings = eva.Snapshot;
            for (int i = 0; i < suits.Length; i++)
            {
                if (suits[i].Status != SuitStatus.Sealed) continue;
                foreach (SuitService service in readings.Suits[i].BelowReserve)
                    if (reserveRecorded.Add((i, service)))
                        AddIncident(new ExpeditionIncident(IncidentKind.ReserveUsed, suits[i].Wearer, now, Scenario.Eva.Suits[i].Key, service));
            }
        }

        private void AddIncident(ExpeditionIncident incident)
        {
            if (incidents.Count < ExpeditionLimits.MaxIncidents) incidents.Add(incident);
        }

        // ---------------------------------------------------------------- end

        /// <summary>End conditions in the documented order (base essential, suit depletion, instruction end, closed
        /// expedition, mission time). Uses only issued information and confirmed state, never the hidden event.</summary>
        private ExpeditionOutcome Evaluate(double now)
        {
            if (!OutpostMissionEvaluator.IsEssentialServed(RequireBase().Power) && (policy == null || policy.EssentialLossIsFinal))
                return ExpeditionOutcome.EssentialServiceLost;
            EvaSnapshot readings = eva.Snapshot;
            for (int i = 0; i < suits.Length; i++)
                if (suits[i].Status == SuitStatus.Sealed && readings.Suits[i].IsDepleted) return ExpeditionOutcome.SuitServiceDepleted;
            ShelterInstructionStatus instruction = timeline.Instruction(now);
            if (policy != null && policy.Continuous)
            {
                // Continuous (phase 4): a finished sortie is an activity, not the end. When everybody who went out is
                // back off suit supply, the sortie closes and the expedition phase returns to Preparing without a
                // decision; objectives restart for the next sortie. Stocks, clock, crew and machines are kept.
                if (!instruction.InForce && phase != ExpeditionPhase.Preparing && !AnySealed())
                {
                    policy.SortieFinished(PrimaryMet(), decision != null, now);
                    phase = ExpeditionPhase.Preparing;
                    decision = null;
                    Array.Clear(objectiveCounts, 0, objectiveCounts.Length);
                }
                return ExpeditionOutcome.InProgress;
            }
            if (instruction.State == InstructionState.Ended)
            {
                assessments = AssessPresent(instruction);
                return ProtectionTracker.Outcome(assessments);
            }
            bool primary = PrimaryMet();
            // Closed (frozen contract, end condition 4): someone went out and nobody is on suit supply any more; the
            // repressurization that unseals the last member is the ingress.
            if (!instruction.InForce && phase != ExpeditionPhase.Preparing && !AnySealed() && (primary || decision != null))
                return primary ? ExpeditionOutcome.ScienceReturned : ExpeditionOutcome.ReturnedWithoutScience;
            if (now >= Scenario.Rules.MissionDurationSeconds - OutpostMissionEvaluator.TimeToleranceSeconds)
            {
                if (instruction.InForce) assessments = AssessPresent(instruction);
                return ExpeditionOutcome.TimeExpired;
            }
            return ExpeditionOutcome.InProgress;
        }

        private List<ProtectionAssessment> AssessPresent(ShelterInstructionStatus instruction)
        {
            var list = new List<ProtectionAssessment>();
            foreach (CrewMemberState pose in roster.Members)
            {
                ProtectionAssessment assessment = protection.Assess(pose.Player, instruction.ShelterBySeconds.Value);
                if (assessment.Category == ProtectionCategory.NotReceived)
                    AddIncident(new ExpeditionIncident(IncidentKind.AdvisoryNotReceived, pose.Player, Now, null, null));
                list.Add(assessment);
            }
            return list;
        }

        private void Close()
        {
            state = outcome == ExpeditionOutcome.SuitServiceDepleted || outcome == ExpeditionOutcome.EssentialServiceLost
                || outcome == ExpeditionOutcome.SimulationFault ? SessionState.Interrupted : SessionState.Completed;
            board.EndAll(TaskEndReason.MissionEnded, Now);
            phase = ExpeditionPhase.Closed;
            policy?.Closed(outcome, Now);
        }

        private void EnterFault()
        {
            outcome = ExpeditionOutcome.SimulationFault;
            state = SessionState.Interrupted;
            board.EndAll(TaskEndReason.MissionEnded, Now);
            phase = ExpeditionPhase.Closed;
            policy?.Closed(outcome, Now);
            Commit();
        }

        // ---------------------------------------------------------------- snapshots

        private void Commit()
        {
            revision = checked(revision + 1);
            snapshot = Build();
        }

        private ExpeditionSessionSnapshot Build()
        {
            double now = Now;
            OutpostSimulationSnapshot resources = RequireBase();
            EvaSnapshot readings = eva.Snapshot ?? throw new InvalidOperationException("IEvaSimulation returned a null snapshot.");
            var crew = new List<ExpeditionCrewState>();
            foreach (CrewMemberState pose in roster.Members)
            {
                Member member = members[pose.Player];
                bool sealedNow = IsSealed(member);
                ReturnMarginEstimate margin = sealedNow ? MarginOf(pose, member, readings, now) : ReturnMarginEstimate.NotApplicable(now);
                crew.Add(new ExpeditionCrewState(pose.Player, member.Zone, member.ZoneSequence, member.Suit, sealedNow,
                    member.Activity, margin, member.Protection));
            }
            var suitStates = new List<SuitState>(suits.Length);
            for (int i = 0; i < suits.Length; i++)
                suitStates.Add(new SuitState(i, Scenario.Eva.Suits[i].Key, suits[i].Status, suits[i].Wearer, suits[i].Completed, readings.Suits[i]));
            var airlock = new AirlockStatus(airlockState, cycleSequence, cycleWork, cycleDuration, cycleStalled, Occupants(), cycleRequestedBy);
            var shelter = new ShelterStatus(shelterPrepared, ventilationRunning, shelterServiceAvailable, ShelterOccupants());
            var objectives = new List<ExpeditionObjective>(objectiveCounts.Length);
            var compatibility = new List<ObjectiveProgress>(objectiveCounts.Length);
            for (int i = 0; i < objectiveCounts.Length; i++)
            {
                ExpeditionObjectiveRule rule = Scenario.Rules.Objectives[i];
                int current = rule.Role == ObjectiveRole.Safety ? (ExpeditionOutcomes.IsSafe(outcome) ? 1 : 0) : objectiveCounts[i];
                objectives.Add(new ExpeditionObjective(rule.Key, rule.Role, current, rule.TargetCount));
                compatibility.Add(new ObjectiveProgress(rule.Key, current, rule.TargetCount, ProgressUnit.Count, rule.Role == ObjectiveRole.Safety));
            }
            var outpost = new OutpostSessionSnapshot(epoch, revision, state, ExpeditionOutcomes.ToOutpostOutcome(outcome), Scenario.Id,
                roster.Members, StationStates(), resources, compatibility);
            EventDisclosure disclosure = Ended && Scenario.Episode != null ? EventDisclosure.Of(Scenario.Episode) : null;
            return new ExpeditionSessionSnapshot(outpost, crew, suitStates, airlock, shelter, readings.Supply, timeline.Issued(now),
                timeline.Receptions, timeline.Instruction(now), phase, decision, outcome, objectives, assessments, incidents, disclosure);
        }

        /// <summary>Phase 2 station states; a station held by a phase 4 operation is published Unavailable.</summary>
        private List<StationState> StationStates()
        {
            List<StationState> states = board.BuildStates();
            if (policy == null) return states;
            for (int i = 0; i < states.Count; i++)
                if (states[i].Availability == StationAvailability.Available && policy.IsStationUnavailable(states[i].Station))
                    states[i] = new StationState(states[i].Station, StationAvailability.Unavailable, null, states[i].LastEnd);
            return states;
        }

        // ---------------------------------------------------------------- phase 4 access (continuous authority only)

        internal long CurrentEpoch => epoch;
        internal SessionState CurrentState => state;
        internal bool IsEnded => Ended;
        internal double SimulatedNow => Now;
        internal bool HasCommand(Guid id) => commands.ContainsKey(id);
        internal bool HasTask(PlayerId player) => board.TryGetForPlayer(player, out _);
        internal IReadOnlyList<CrewMemberState> CrewPoses => roster.Members;

        /// <summary>Confirmed pose, zone sequence and sealed state of a present member.</summary>
        internal bool TryGetMember(PlayerId player, out CrewMemberState pose, out long zoneSequence, out bool sealedNow)
        {
            pose = player.IsValid ? FindPose(player) : null;
            zoneSequence = 0;
            sealedNow = false;
            if (pose == null || !members.TryGetValue(player, out Member member)) return false;
            zoneSequence = member.ZoneSequence;
            sealedNow = IsSealed(member);
            return true;
        }

        /// <summary>Commits a change made by the phase 4 authority (new revision). The caller raises the event.</summary>
        internal void CommitExternal() => Commit();

        internal void RaiseSnapshotChanged() => SnapshotChanged?.Invoke(snapshot);

        /// <summary>A phase 4 model call failed: SimulationFault, only Restart is accepted.</summary>
        internal void FaultExternal() => EnterFault();

        /// <summary>Conservative return plan of a sealed member from confirmed state (see SESSION_CONTRACT.md).</summary>
        private ReturnMarginEstimate MarginOf(CrewMemberState pose, Member member, EvaSnapshot readings, double now)
        {
            AirlockConfiguration airlock = Scenario.Airlock;
            bool inChamber = member.Zone == CrewZone.Airlock;
            double path = 0d;
            if (!inChamber && !route.TryGetPathMeters(pose.Pose.Position, out path))
                path = pose.Pose.Position.DistanceTo(airlock.ExteriorEntry);
            double walk = path / SealedSpeedMetersPerSecond;
            double remainingCycle = Math.Max(0d, cycleDuration - cycleWork);
            double wait = 0d, ingress = airlock.RepressurizeSeconds;
            switch (airlockState)
            {
                case AirlockState.Depressurizing: wait = remainingCycle; break;
                case AirlockState.PressurizedInnerOpen: wait = inChamber ? 0d : airlock.DepressurizeSeconds; break;
                case AirlockState.Repressurizing:
                    if (inChamber) ingress = remainingCycle;
                    else wait = remainingCycle + airlock.DepressurizeSeconds;
                    break;
            }
            bool gasNeeded = !(inChamber && airlockState == AirlockState.Repressurizing);
            bool reachable = !gasNeeded || airlock.MakeupGasPerCycleKilograms <= 0d
                || readings.Supply.MakeupGasKilograms + AmountTolerance >= airlock.MakeupGasPerCycleKilograms;
            var plan = new ReturnPlan(path, walk, wait, ingress, reachable);
            return ReturnMarginCalculator.Estimate(Scenario.Eva.Model, readings.Suits[member.Suit].Levels, member.Activity,
                ToolWattsOf(pose.Player, member), plan, now);
        }

        /// <summary>Worst valid margin among sealed members now (the team's, never an average); null if none.</summary>
        private double? TeamWorstMargin()
        {
            EvaSnapshot readings = eva.Snapshot;
            double? worst = null;
            double now = Now;
            foreach (CrewMemberState pose in roster.Members)
            {
                Member member = members[pose.Player];
                if (!IsSealed(member)) continue;
                ReturnMarginEstimate margin = MarginOf(pose, member, readings, now);
                if (margin.Status != MarginStatus.Sufficient && margin.Status != MarginStatus.Insufficient) continue;
                if (!worst.HasValue || margin.MarginSeconds < worst.Value) worst = margin.MarginSeconds;
            }
            return worst;
        }

        // ---------------------------------------------------------------- helpers

        private bool IsSealed(Member member) => member.Suit >= 0 && suits[member.Suit].Status == SuitStatus.Sealed;

        private bool AnySealed()
        {
            foreach (SuitEquipment suit in suits)
                if (suit.Status == SuitStatus.Sealed) return true;
            return false;
        }


        private bool PrimaryMet()
        {
            for (int i = 0; i < objectiveCounts.Length; i++)
            {
                ExpeditionObjectiveRule rule = Scenario.Rules.Objectives[i];
                if (rule.Role == ObjectiveRole.Primary && objectiveCounts[i] < rule.TargetCount) return false;
            }
            return true;
        }

        private List<PlayerId> Occupants()
        {
            var list = new List<PlayerId>();
            foreach (CrewMemberState pose in roster.Members)
                if (members[pose.Player].Zone == CrewZone.Airlock) list.Add(pose.Player);
            return list;
        }

        private int ShelterOccupants()
        {
            int count = 0;
            foreach (Member member in members.Values)
                if (member.Zone == CrewZone.Shelter) count++;
            return count;
        }

        private CrewZone ZoneOf(BasePosition position)
        {
            if (!Scenario.TryGetZone(position, out CrewZone zone))
                throw new InvalidOperationException($"Confirmed position {position} is in no zone (scenario coverage error).");
            return zone;
        }

        private CrewMemberState FindPose(PlayerId player)
        {
            foreach (CrewMemberState member in roster.Members)
                if (member.Player == player) return member;
            return null;
        }

        private static double Clamp(double seconds, double elapsed) =>
            double.IsNaN(seconds) || seconds < 0d ? 0d : Math.Min(seconds, elapsed);

        private OutpostSimulationSnapshot RequireBase() =>
            baseModel.Snapshot ?? throw new InvalidOperationException("IOutpostSimulation returned a null snapshot.");
    }

    /// <summary>
    /// Close policy and step hooks injected by the phase 4 continuous authority (D2). Without it the phase 3 authority keeps
    /// its finite rules exactly. With it: the base demand gains the phase 4 operation loads; the step is also cut at the
    /// phase 4 events; the equipment advances right before the base with the same step and demand; operations, orders
    /// and the irreversible rule are credited after the step; and, when <see cref="Continuous"/>, a finished sortie and
    /// the mission duration never end the session (only the irreversible rule, suit depletion or a fault do).
    /// Callbacks run inside the authority's own call; they must not call back into its public API.
    /// </summary>
    internal interface IExpeditionSessionPolicy
    {
        bool Continuous { get; }
        /// <summary>False while the essential-power recovery window has not elapsed (always true for finite rules).</summary>
        bool EssentialLossIsFinal { get; }
        bool OwnsCommandId(Guid id);
        bool IsPlayerBusy(PlayerId player);
        bool IsStationUnavailable(StationId station);
        double ServiceWatts();
        double SecondsUntilEvent(OutpostDemand baseDemand, double now);
        /// <summary>After the crew moved and zones were confirmed: phase 4 operations whose performer left reach end.</summary>
        void ConfirmReach(double now);
        void BeforeBaseAdvance(double step, OutpostDemand baseDemand, double now);
        void AfterStep(double elapsed, double fullyServedServiceSeconds, double now);
        void TaskCompleted(TaskTypeId task, PlayerId performer, double now);
        void SortieStarted(PlayerId by, double now);
        void SortieFinished(bool primaryMet, bool returnDeclared, double now);
        /// <summary>Returns true if the player had a phase 4 operation (ended with PerformerLeft).</summary>
        bool PlayerLeft(PlayerId player, double now);
        /// <summary>Called inside Restart right after the base and EVA models were reset.</summary>
        void Restarted();
        void Closed(ExpeditionOutcome outcome, double now);
    }

    /// <summary>Deterministic ids for the second and later EVA transfers caused by ONE command (each id applies once).</summary>
    public static class TransferIds
    {
        public static Guid Derive(Guid id, byte salt)
        {
            byte[] bytes = id.ToByteArray();
            bytes[15] ^= (byte)(0x5A ^ salt);
            bytes[7] ^= salt;
            return new Guid(bytes);
        }
    }
}
