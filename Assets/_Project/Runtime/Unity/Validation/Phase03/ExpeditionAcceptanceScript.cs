using System;
using System.Collections.Generic;
using Trainer.Application;
using Trainer.Application.Crew;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;

namespace Trainer.Unity.Validation.Phase03
{
    public enum AcceptanceFlow
    {
        /// <summary>Prepare → go out → science → return → ingress → result (expected ScienceReturned).</summary>
        A,
        /// <summary>Prepare → go out → new information → cancel → return → shelter → result (expected ShelteredInTime).</summary>
        B
    }

    public enum AcceptanceRole
    {
        /// <summary>Wears a suit and goes out.</summary>
        Explorer,
        /// <summary>Stays inside: keeps the base working (A) or prepares the shelter when the warning arrives (B).</summary>
        Support
    }

    /// <summary>
    /// D5 acceptance player (plain C#, no UnityEngine): plays one role of recorrido A or B on the SHIPPED phase 3 scenario
    /// using ONLY what a human player has: its <see cref="IExpeditionSessionPort"/> for held movement directions
    /// (through the phase 2 <see cref="CrewInputSender"/>, never positions) and <see cref="IExpeditionIntents"/> for
    /// actions; it reads only the confirmed snapshot and decides nothing for the authority. Used by EditMode tests over the
    /// real stack and by the build's autopilot across real processes. Waypoints follow the shipped plan's corridors.
    /// </summary>
    public sealed class ExpeditionAcceptanceScript
    {
        private const double Arrival = 0.15;
        private const double SlowRadius = 0.6;
        private const double StuckSeconds = 6;

        // Waypoints of the shipped plan (metres).
        public static readonly BasePosition Door1 = new BasePosition(6.0, 5.0), Door2 = new BasePosition(12.0, 5.0);
        public static readonly BasePosition LabEast = new BasePosition(17.3, 7.4), Wing = new BasePosition(19.0, 7.4);
        public static readonly BasePosition RackA = new BasePosition(19.4, 8.45), Charger = new BasePosition(23.2, 8.45);
        public static readonly BasePosition Consumables = new BasePosition(24.9, 8.45), SuitCheck = new BasePosition(23.0, 6.45);
        public static readonly BasePosition BeforeInner = new BasePosition(25.4, 7.7), InnerDoorway = new BasePosition(26.7, 7.7);
        public static readonly BasePosition Chamber = new BasePosition(26.9, 7.1), OuterDoorway = new BasePosition(28.6, 7.7);
        public static readonly BasePosition Outside = new BasePosition(29.9, 7.7), BackIn = new BasePosition(28.2, 7.6);
        public static readonly BasePosition Regolith = new BasePosition(36.2, 4.6), Water = new BasePosition(9.0, 7.6);
        public static readonly BasePosition ShelterDoorAbove = new BasePosition(20.0, 5.75), ShelterDoorBelow = new BasePosition(20.0, 4.0);
        // Standing spots stay well inside station reach even with the arrival tolerance (the authority re-checks reach).
        public static readonly BasePosition ShelterPanelSpot = new BasePosition(24.6, 1.6), ShelterSpot = new BasePosition(22.5, 2.4);
        /// <summary>In recorrido B the explorer waits inside until this simulated time, so the authored warning (150 s)
        /// finds them working outside, in radio range.</summary>
        public const double FlowBEgressNotBeforeSeconds = 125;

        private static readonly StationId RackAStation = new StationId("suit-rack-a"), ChargerStation = new StationId("suit-charger");
        private static readonly StationId ConsumablesStation = new StationId("consumables-panel"), CheckStation = new StationId("suit-check");
        private static readonly StationId InnerPanel = new StationId("airlock-inner-panel"), RegolithStation = new StationId("regolith-site");
        private static readonly StationId WaterStation = new StationId("water-recycler"), ShelterPanel = new StationId("shelter-panel");
        private static readonly TaskTypeId DonA = new TaskTypeId("don-suit-a"), Charge = new TaskTypeId("charge-suit");
        private static readonly TaskTypeId Fill = new TaskTypeId("fill-suit"), Check = new TaskTypeId("check-suit");
        private static readonly TaskTypeId Collect = new TaskTypeId("collect-regolith"), TreatWater = new TaskTypeId("treat-water");
        private static readonly TaskTypeId PrepareShelter = new TaskTypeId("prepare-shelter");

        private readonly IExpeditionSessionPort port;
        private readonly IExpeditionIntents intents;
        private readonly ExpeditionScenario scenario;
        private readonly CrewInputSender movement;
        private readonly int expectedCrew;
        private readonly List<Step> steps = new List<Step>();
        private int current;
        private double realTime;

        public AcceptanceFlow Flow { get; }
        public AcceptanceRole Role { get; }
        public bool Finished { get; private set; }
        public bool Passed { get; private set; }
        public string Verdict { get; private set; } = "";
        public ExpeditionOutcome Expected => Flow == AcceptanceFlow.A ? ExpeditionOutcome.ScienceReturned : ExpeditionOutcome.ShelteredInTime;
        public List<string> Log { get; } = new List<string>();
        public string CurrentStep => current < steps.Count ? steps[current].Name : "fin";

        public ExpeditionAcceptanceScript(IExpeditionSessionPort port, IExpeditionIntents intents, ExpeditionScenario scenario,
            AcceptanceFlow flow, AcceptanceRole role, int expectedCrew = 2)
        {
            this.port = port ?? throw new ArgumentNullException(nameof(port));
            this.intents = intents ?? throw new ArgumentNullException(nameof(intents));
            this.scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
            this.expectedCrew = Math.Max(1, expectedCrew);
            Flow = flow;
            Role = role;
            movement = new CrewInputSender(port);
            intents.Answered += reason => lastAnswer = reason;
            Plan();
        }

        private string lastAnswer;

        /// <summary>Advances the script by one frame of <paramref name="realSeconds"/>.</summary>
        public void Update(double realSeconds)
        {
            if (Finished) return;
            realTime += realSeconds;
            if (!port.Membership.IsJoined || !port.TryGetExpedition(out ExpeditionSessionSnapshot s)) return;
            if (s.Outpost.State == SessionState.Completed || s.Outpost.State == SessionState.Interrupted)
            {
                movement.Update(0, 0);
                Finish(s.Outcome == Expected, $"outcome={s.Outcome} expected={Expected} at t={s.SimulatedTimeSeconds:0.0}s step='{CurrentStep}'");
                return;
            }
            while (current < steps.Count)
            {
                Step step = steps[current];
                if (!step.Started)
                {
                    step.Started = true;
                    step.StartedAt = s.SimulatedTimeSeconds;
                    Note(s, "start " + step.Name);
                }
                StepStatus status = step.Run(this, s);
                if (status == StepStatus.Running) return;
                if (status == StepStatus.Failed)
                {
                    movement.Update(0, 0);
                    Finish(false, $"step '{step.Name}' failed at t={s.SimulatedTimeSeconds:0.0}s: {step.Detail}");
                    return;
                }
                Note(s, "done " + step.Name);
                current++;
            }
        }

        private void Finish(bool passed, string verdict)
        {
            Finished = true;
            Passed = passed;
            Verdict = verdict;
            Log.Add((passed ? "PASS " : "FAIL ") + verdict);
        }

        private void Note(ExpeditionSessionSnapshot s, string text) => Log.Add($"t={s.SimulatedTimeSeconds:0.0}s {Role} {text}");

        // ---------------------------------------------------------------- plan

        private void Plan()
        {
            Add("wait for the crew", (me, s) => s.Outpost.Crew.Count >= expectedCrew ? StepStatus.Done : StepStatus.Running);
            if (Role == AcceptanceRole.Explorer)
                Add("start the mission", (me, s) => s.Outpost.State != SessionState.Ready ? StepStatus.Done
                    : me.Act(() => me.Session(ExpeditionAction.Resume)));
            else Add("mission running", (me, s) => s.Outpost.State == SessionState.Running ? StepStatus.Done : StepStatus.Running);

            if (Role == AcceptanceRole.Explorer)
            {
                Walk(Door1, Door2, LabEast, Wing, RackA);
                Task(RackAStation, DonA);
                Walk(Charger);
                Task(ChargerStation, Charge);
                Walk(Consumables);
                Task(ConsumablesStation, Fill);
                Walk(SuitCheck);
                Task(CheckStation, Check);
                if (Flow == AcceptanceFlow.B)
                    Add("wait inside before going out", (me, s) => s.SimulatedTimeSeconds >= FlowBEgressNotBeforeSeconds ? StepStatus.Done : StepStatus.Running);
                Walk(BeforeInner, InnerDoorway, Chamber);
                Cycle(AirlockState.DepressurizedOuterOpen);
                Walk(OuterDoorway, Outside, Regolith);
                if (Flow == AcceptanceFlow.A)
                {
                    Task(RegolithStation, Collect);
                    Walk(Outside, BackIn, Chamber);
                    Cycle(AirlockState.PressurizedInnerOpen);
                }
                else
                {
                    Add("start the science", (me, s) => me.Act(() => intents.RequestStart(RegolithStation, Collect)));
                    Add("receive new information", (me, s) => me.Received(s) ? StepStatus.Done : StepStatus.Running);
                    Add("cancel the science", (me, s) => !me.HasOwnTask(s) ? StepStatus.Done : me.Act(() => intents.RequestCancel(RegolithStation)));
                    Add("declare the return", (me, s) => s.Phase == ExpeditionPhase.Returning ? StepStatus.Done
                        : me.Act(() => intents.RequestDeclareReturn(), ExpeditionReasons.ReturnAlreadyDeclared));
                    Walk(Outside, BackIn, Chamber);
                    Cycle(AirlockState.PressurizedInnerOpen);
                    Walk(BeforeInner, ShelterDoorAbove, ShelterDoorBelow, ShelterSpot);
                }
            }
            else if (Flow == AcceptanceFlow.A)
            {
                Walk(Door1, Water);
                Task(WaterStation, TreatWater); // the base keeps working while someone is outside
            }
            else
            {
                Walk(Door1, Door2, LabEast, Wing, ShelterDoorAbove, ShelterDoorBelow, ShelterPanelSpot);
                Add("receive new information", (me, s) => me.Received(s) ? StepStatus.Done : StepStatus.Running);
                Task(ShelterPanel, PrepareShelter);
            }
            Add("wait for the result", (me, s) => StepStatus.Running);
        }

        private void Add(string name, Func<ExpeditionAcceptanceScript, ExpeditionSessionSnapshot, StepStatus> run) =>
            steps.Add(new Step(name, run));

        private void Walk(params BasePosition[] points)
        {
            foreach (BasePosition target in points)
            {
                var progress = new WalkProgress();
                Add($"walk to {target}", (me, s) => me.WalkTo(s, target, progress));
            }
        }

        private void Task(StationId station, TaskTypeId task)
        {
            Add($"start {task}", (me, s) => me.Act(() => intents.RequestStart(station, task)));
            Add($"finish {task}", (me, s) =>
            {
                s.Outpost.TryGetStation(station, out StationState state);
                if (state.ActiveTask != null) return StepStatus.Running;
                if (state.LastEnd != null && state.LastEnd.Performer == port.Membership.Player && state.LastEnd.Task == task)
                    return state.LastEnd.Reason == TaskEndReason.Completed ? StepStatus.Done : me.Fail("ended " + state.LastEnd.Reason);
                return me.Fail("no task end recorded");
            });
        }

        private void Cycle(AirlockState until)
        {
            Add("cycle the airlock", (me, s) => me.Act(() => intents.RequestCycle(InnerPanel)));
            Add($"airlock {until}", (me, s) => s.Airlock.State == until ? StepStatus.Done : StepStatus.Running);
        }

        // ---------------------------------------------------------------- step machinery

        private enum StepStatus { Running, Done, Failed }

        private sealed class Step
        {
            public readonly string Name;
            public readonly Func<ExpeditionAcceptanceScript, ExpeditionSessionSnapshot, StepStatus> Body;
            public bool Started, Sent;
            public double StartedAt, RetryAt;
            public string Detail;
            public Step(string name, Func<ExpeditionAcceptanceScript, ExpeditionSessionSnapshot, StepStatus> body)
            {
                Name = name;
                Body = body;
            }
            public StepStatus Run(ExpeditionAcceptanceScript me, ExpeditionSessionSnapshot s) => Body(me, s);
        }

        private sealed class WalkProgress
        {
            public double Best = double.PositiveInfinity;
            public double BestAt;
        }

        private Step Running => steps[current];

        private StepStatus Fail(string detail)
        {
            Running.Detail = detail;
            return StepStatus.Failed;
        }

        /// <summary>Sends one intent and waits for its answer; transient refusals are retried after 0.5 s.</summary>
        private StepStatus Act(Func<bool> send, params string[] alsoDone)
        {
            Step step = Running;
            if (!step.Sent)
            {
                if (realTime < step.RetryAt || intents.HasPending) return StepStatus.Running;
                lastAnswer = null;
                if (!send()) return StepStatus.Running; // nothing sent (busy or no state yet): try next frame
                step.Sent = true;
                return StepStatus.Running;
            }
            if (intents.HasPending || lastAnswer == null) return StepStatus.Running;
            string answer = lastAnswer;
            lastAnswer = null;
            step.Sent = false;
            if (answer == OutpostReasons.Accepted || Array.IndexOf(alsoDone, answer) >= 0) return StepStatus.Done;
            if (answer == ExpeditionReasons.StaleZone || answer == ExpeditionReasons.StaleAirlockState || answer == ExpeditionReasons.AirlockBusy
                || answer == OutpostReasons.StationOccupied || answer == OutpostReasons.ConnectionLost)
            {
                step.RetryAt = realTime + 0.5;
                return StepStatus.Running;
            }
            return Fail("refused: " + answer);
        }

        private bool Session(ExpeditionAction action)
        {
            if (!port.TryGetExpedition(out ExpeditionSessionSnapshot s)) return false;
            // Session actions go through the port like the phase 2 session controls; the answer reaches the intents
            // listener only for its own requests, so it is tracked here through the port result.
            var command = ExpeditionCommand.Session(Guid.NewGuid(), s.Epoch, action);
            void OnResult(CommandResult r)
            {
                if (r.CommandId != command.Id) return;
                port.CommandCompleted -= OnResult;
                lastAnswer = r.Reason;
            }
            port.CommandCompleted += OnResult;
            port.Submit(command);
            return true;
        }

        private StepStatus WalkTo(ExpeditionSessionSnapshot s, BasePosition target, WalkProgress progress)
        {
            if (!s.Outpost.TryGetCrew(port.Membership.Player, out CrewMemberState pose)) return StepStatus.Running;
            double dx = target.X - pose.Pose.Position.X, dy = target.Y - pose.Pose.Position.Y, d = Math.Sqrt(dx * dx + dy * dy);
            if (d <= Arrival)
            {
                movement.Update(0, 0);
                return StepStatus.Done;
            }
            if (d < progress.Best - 0.05)
            {
                progress.Best = d;
                progress.BestAt = s.SimulatedTimeSeconds;
            }
            else if (s.Outpost.State == SessionState.Running && s.SimulatedTimeSeconds - progress.BestAt > StuckSeconds
                && !double.IsInfinity(progress.Best))
                return Fail($"stuck at {pose.Pose.Position} going to {target}");
            if (double.IsInfinity(progress.Best)) progress.BestAt = s.SimulatedTimeSeconds;
            // Quantized held direction (32 headings, two speeds) so the client sends only on real changes.
            double angle = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 16)) * (Math.PI / 16);
            double magnitude = d > SlowRadius ? 1.0 : 0.35;
            movement.Update(Math.Cos(angle) * magnitude, Math.Sin(angle) * magnitude);
            return StepStatus.Running;
        }

        private bool Received(ExpeditionSessionSnapshot s)
        {
            if (!s.Instruction.InForce) return false;
            foreach (AdvisoryReception r in s.Receptions)
                if (r.Player == port.Membership.Player) return true;
            return false;
        }

        private bool HasOwnTask(ExpeditionSessionSnapshot s)
        {
            foreach (StationState station in s.Outpost.Stations)
                if (station.Occupant == port.Membership.Player) return true;
            return false;
        }
    }
}
