using System;
using System.Collections.Generic;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.LifeSupport;

namespace Trainer.Application.BaseOperations
{
    public sealed partial class BaseOperationsAuthority
    {
        /// <summary>A timed (or immediate) station operation in progress, mutable inside the authority only.</summary>
        private sealed class Operation
        {
            public Guid CommandId;
            public StationId Station;
            public BaseOperationAction Action;
            public OperationChoice Choice;
            public PlayerId Performer;
            public double Work, Duration, Power;
            public bool Stalled;
            public string Spare;
            public EquipmentKind? Machine;
            public long OrderSequence;
            public string ManifestKey;
            public long ObservationSequence;
            public long Activity;

            public OperationProgress ToProgress() =>
                new OperationProgress(CommandId, Station, Action, Choice, Performer, Work, Duration, Stalled, Spare);
        }

        /// <summary>What the authority tracks per machine besides the D1 truth: state sequence, observations, repair.</summary>
        private sealed class Machine
        {
            public EquipmentKind Kind;
            public string Signature;
            public long StateSequence;
            public EquipmentObservation Observation;
            public bool RepairOpen;
            public RepairSteps Completed;
            public double RepairStartedAt;
            public long RepairObservation;
            public bool? LastTestPassed;
            public bool FailureSeen;
            public long RepairActivity;
        }

        private sealed class Order
        {
            public long Sequence;
            public string ManifestKey;
            public PlayerId PlacedBy;
            public double PlacedAt, CommitAt, ArriveAt;
            public OrderState State;
            public List<ManifestItem> Remaining;
            public double? ClosedAt;
            public long Activity;

            public ResupplyOrder ToRecord() =>
                new ResupplyOrder(Sequence, ManifestKey, PlacedBy, PlacedAt, CommitAt, ArriveAt, State, Remaining, ClosedAt);

            public bool InFlight => State == OrderState.Pending || State == OrderState.InTransit || State == OrderState.Arrived;
        }

        private sealed class Activity
        {
            public long Sequence;
            public ActivityKind Kind;
            public string Key;
            public ActivityStatus Status;
            public PlayerId StartedBy;
            public double StartedAt;
            public double? EndedAt;
            public string ResultKey;

            public ActivityRecord ToRecord() => new ActivityRecord(Sequence, Kind, Key, Status, StartedBy, StartedAt, EndedAt, ResultKey);
        }

        /// <summary>
        /// The phase 4 state and rules, injected into the phase 3 authority as its close policy and step hooks. All the
        /// decisions of the living base live here; the phase 3 authority keeps zones, suits, airlock and tasks.
        /// </summary>
        private sealed partial class ContinuousPolicy : IExpeditionSessionPolicy
        {
            private readonly BaseOperationsAuthority owner;
            private readonly BaseOperationsScenario scenario;
            private readonly Dictionary<StationId, Operation> operations = new Dictionary<StationId, Operation>();
            private readonly Machine[] machines =
            {
                new Machine { Kind = EquipmentKind.WaterProcessor },
                new Machine { Kind = EquipmentKind.AirRevitalization }
            };
            private readonly List<Order> orders = new List<Order>();
            private readonly List<Activity> activities = new List<Activity>();
            private readonly List<DecisionRecord> decisions = new List<DecisionRecord>();
            private readonly double[] trendBefore = new double[8];
            private readonly bool[] trendBeforeValid = new bool[8];
            private readonly double?[] trends = new double?[8];

            private long observationSequence;
            private long orderSequence;
            private double? lastOrderAt;
            private long activitySequence;
            private int expeditionsFinished;
            private long sortieActivity;
            private long batchActivity;
            private double suspectKilograms;
            private long batchesProcessed;
            private double essentialUnserved;
            private IrreversibleCause endCause;
            private double? endedAt;
            private long reportSequence;
            private double reportFrom, nextReportAt;
            private int reportCompleted, reportCancelled;
            private double reportPotable, reportFood, reportEnergy;
            private OperationsReport latestReport;

            public ContinuousPolicy(BaseOperationsAuthority owner)
            {
                this.owner = owner;
                scenario = owner.Scenario;
            }

            private bool IsContinuous => owner.Mode == BaseSessionMode.Continuous;
            private OutpostSimulationSnapshot Base => owner.outpost.Snapshot;
            private BaseEquipmentSnapshot Truth => owner.equipment.Snapshot;

            // ------------------------------------------------------------ IExpeditionSessionPolicy

            public bool Continuous => IsContinuous;

            public bool EssentialLossIsFinal =>
                !IsContinuous || essentialUnserved >= scenario.Irreversible.EssentialUnservedGraceSeconds - Tolerance;

            public bool OwnsCommandId(Guid id) => owner.commands.ContainsKey(id);

            public bool IsPlayerBusy(PlayerId player) => FindOperation(player) != null;

            public bool IsStationUnavailable(StationId station) => operations.ContainsKey(station);

            public double ServiceWatts()
            {
                double watts = 0d;
                foreach (Operation op in operations.Values) watts += op.Power;
                return watts;
            }

            public double SecondsUntilEvent(OutpostDemand baseDemand, double now)
            {
                double next = owner.equipment.SecondsUntilEquipmentEvent(new EquipmentDemand(baseDemand.CrewAboard));
                foreach (Operation op in operations.Values)
                    next = Math.Min(next, Math.Max(op.Duration - op.Work, ExpeditionAuthority.MinimumWorkStepSeconds));
                foreach (Order order in orders)
                {
                    if (order.State == OrderState.Pending) next = Positive(next, order.CommitAt - now);
                    if (order.State == OrderState.Pending || order.State == OrderState.InTransit) next = Positive(next, order.ArriveAt - now);
                }
                next = Positive(next, nextReportAt - now);
                if (IsContinuous && !OutpostMissionEvaluator.IsEssentialServed(Base.Power))
                    next = Positive(next, scenario.Irreversible.EssentialUnservedGraceSeconds - essentialUnserved);
                return next;
            }

            private static bool Offers(ScienceOpportunity opportunity, TaskTypeId task)
            {
                foreach (TaskTypeId candidate in opportunity.Tasks)
                    if (candidate == task) return true;
                return false;
            }

            private static double Positive(double current, double candidate) =>
                candidate > Tolerance ? Math.Min(current, candidate) : current;

            /// <summary>Performers who left reach of their station end their operation before the demand is computed.</summary>
            public void ConfirmReach(double now)
            {
                foreach (Operation op in new List<Operation>(operations.Values))
                {
                    bool present = owner.inner.TryGetMember(op.Performer, out CrewMemberState pose, out _, out bool sealedNow);
                    scenario.Layout.TryGetStation(op.Station, out StationPlacement placement);
                    if (!present || sealedNow || !placement.IsWithinReach(pose.Pose.Position))
                        EndOperation(op, ActivityStatus.Cancelled, "performer-moved-away", now);
                }
            }

            /// <summary>Order of SESSION_CONTRACT §6: equipment with the same step and crew, then the CO2 factor, then the base.</summary>
            public void BeforeBaseAdvance(double step, OutpostDemand baseDemand, double now)
            {
                CaptureTrendBaseline();
                owner.equipment.Advance(step, new EquipmentDemand(baseDemand.CrewAboard));
                ((IAirServiceCapacity)owner.outpost).SetCarbonDioxideRemovalFactor(Truth.Air.RemovalFactor);
            }

            public void AfterStep(double elapsed, double fullyServedServiceSeconds, double now)
            {
                RefreshMachines();
                CreditOperations(elapsed, fullyServedServiceSeconds, now);
                AdvanceOrders(now);
                essentialUnserved = OutpostMissionEvaluator.IsEssentialServed(Base.Power) ? 0d : essentialUnserved + elapsed;
                ClampSuspect();
                ComputeTrends(elapsed);
                if (now >= nextReportAt - Tolerance) IssueReport(now);
                RefreshMachines();
            }

            public void TaskCompleted(TaskTypeId task, PlayerId performer, double now)
            {
                foreach (ScienceOpportunity opportunity in scenario.Science)
                    if (Offers(opportunity, task))
                    {
                        long sequence = StartActivity(ActivityKind.ScienceAssignment, opportunity.Key, performer, now);
                        EndActivity(sequence, ActivityStatus.Completed, "science-completed", now);
                    }
            }

            public void SortieStarted(PlayerId by, double now)
            {
                sortieActivity = StartActivity(ActivityKind.Expedition, "sortie", by, now);
            }

            public void SortieFinished(bool primaryMet, bool returnDeclared, double now)
            {
                expeditionsFinished++;
                if (sortieActivity == 0) return;
                EndActivity(sortieActivity, ActivityStatus.Completed, primaryMet ? "science-returned" : "returned-without-science", now);
                sortieActivity = 0;
            }

            public bool PlayerLeft(PlayerId player, double now)
            {
                Operation op = FindOperation(player);
                if (op == null) return false;
                EndOperation(op, ActivityStatus.Cancelled, "performer-left", now);
                return true;
            }

            public void Restarted()
            {
                owner.equipment.Reset(scenario.Equipment);
                ResetEpochState();
            }

            public void Closed(ExpeditionOutcome outcome, double now)
            {
                foreach (Operation op in new List<Operation>(operations.Values))
                    EndOperation(op, ActivityStatus.Cancelled, "session-ended", now);
                if (!IsContinuous) return;
                switch (outcome)
                {
                    case ExpeditionOutcome.EssentialServiceLost:
                        endCause = IrreversibleCause.EssentialPowerLost;
                        essentialUnserved = Math.Max(essentialUnserved, scenario.Irreversible.EssentialUnservedGraceSeconds);
                        break;
                    case ExpeditionOutcome.SuitServiceDepleted:
                        endCause = IrreversibleCause.SuitServiceDepleted;
                        break;
                    default:
                        endCause = IrreversibleCause.SimulationFault;
                        break;
                }
                endedAt = now;
            }

            // ------------------------------------------------------------ epoch state

            /// <summary>Everything back to the scenario (construction and Restart). The command record is kept.</summary>
            public void ResetEpochState()
            {
                operations.Clear();
                orders.Clear();
                activities.Clear();
                decisions.Clear();
                observationSequence = 0;
                orderSequence = 0;
                lastOrderAt = null;
                activitySequence = 0;
                expeditionsFinished = 0;
                sortieActivity = batchActivity = 0;
                suspectKilograms = 0d;
                batchesProcessed = 0;
                essentialUnserved = 0d;
                endCause = IrreversibleCause.None;
                endedAt = null;
                foreach (Machine machine in machines)
                {
                    machine.Signature = null;
                    machine.StateSequence = 0;
                    machine.Observation = null;
                    machine.RepairOpen = false;
                    machine.Completed = RepairSteps.None;
                    machine.RepairStartedAt = 0d;
                    machine.RepairObservation = 0;
                    machine.LastTestPassed = null;
                    machine.FailureSeen = false;
                    machine.RepairActivity = 0;
                }
                RefreshMachines();
                for (int i = 0; i < trends.Length; i++)
                {
                    trends[i] = null;
                    trendBeforeValid[i] = false;
                }
                double now = Base.Power.SimulatedTimeSeconds;
                reportSequence = 0;
                latestReport = null;
                StartReportInterval(now);
            }

            // ------------------------------------------------------------ machines

            private Machine MachineOf(EquipmentKind kind) => machines[(int)kind];

            /// <summary>The state sequence rises whenever a published-relevant part of the machine changes.</summary>
            private void RefreshMachines()
            {
                BaseEquipmentSnapshot truth = Truth;
                WaterProcessorState water = truth.WaterProcessor;
                AirRevitalizationState air = truth.Air;
                double knee = scenario.Equipment.Air.KneeLoadingFraction;
                Update(machines[0], $"{water.Condition}|{water.Operating}|{water.Fault}|{water.HoldKilograms > Tolerance}");
                Update(machines[1], $"{air.Operating}|{air.ActiveBed}|{air.ActiveLoadingFraction > knee}|{air.StandbyLoadingFraction > knee}|{air.InletFlowFraction < 1d - Tolerance}");
            }

            private static void Update(Machine machine, string signature)
            {
                if (machine.Signature == signature) return;
                if (machine.Signature != null) machine.StateSequence = checked(machine.StateSequence + 1);
                machine.Signature = signature;
            }

            private bool IsCurrent(EquipmentObservation observation, Machine machine, double now) =>
                observation != null && observation.StateSequence == machine.StateSequence && now < observation.ValidUntilSeconds - Tolerance;

            /// <summary>The truth the instruments would show now (findings and readings), never more.</summary>
            private Findings FindingsOf(EquipmentKind kind)
            {
                BaseEquipmentSnapshot truth = Truth;
                Findings findings = Findings.None;
                if (kind == EquipmentKind.WaterProcessor)
                {
                    WaterProcessorState water = truth.WaterProcessor;
                    if (water.HoldKilograms > Tolerance && OffSpec(water.ProductConductivity)) findings |= Findings.ProductOffSpec;
                    if (water.Fault == EquipmentFault.ProcessorFilterRestricted) findings |= Findings.ProcessorFilterRestricted;
                    if (water.Condition == EquipmentCondition.Failed) findings |= Findings.ProcessorFailed;
                }
                else
                {
                    AirRevitalizationState air = truth.Air;
                    double knee = scenario.Equipment.Air.KneeLoadingFraction;
                    if (air.ActiveLoadingFraction > knee) findings |= Findings.ActiveBedSaturated;
                    if (air.StandbyLoadingFraction > knee) findings |= Findings.StandbyBedLoaded;
                    if (air.InletFlowFraction < 1d - Tolerance) findings |= Findings.InletFlowRestricted;
                }
                return findings;
            }

            private bool OffSpec(InstrumentReading reading) =>
                reading.Status == ReadingStatus.Valid && reading.Value.Value > scenario.Equipment.WaterProcessor.ProductSpecLimitMicrosiemens;

            private List<ObservedReading> ReadingsOf(EquipmentKind kind, double now)
            {
                BaseEquipmentSnapshot truth = Truth;
                var readings = new List<ObservedReading>();
                if (kind == EquipmentKind.WaterProcessor)
                {
                    InstrumentReading conductivity = truth.WaterProcessor.ProductConductivity;
                    if (conductivity.ObservedAtSeconds.HasValue && conductivity.ObservedAtSeconds.Value > now + 1e-6)
                        conductivity = InstrumentReading.Unavailable(ReadingUnit.MicrosiemensPerCentimeter);
                    readings.Add(new ObservedReading("product-conductivity", conductivity));
                }
                else
                {
                    AirRevitalizationState air = truth.Air;
                    readings.Add(new ObservedReading("active-bed-loading", InstrumentReading.Valid(ReadingUnit.Fraction, air.ActiveLoadingFraction, now)));
                    readings.Add(new ObservedReading("standby-bed-loading", InstrumentReading.Valid(ReadingUnit.Fraction, air.StandbyLoadingFraction, now)));
                    readings.Add(new ObservedReading("inlet-flow", InstrumentReading.Valid(ReadingUnit.Fraction, air.InletFlowFraction, now)));
                }
                return readings;
            }

            /// <summary>New current observation of <paramref name="kind"/> by <paramref name="by"/> (after the machine state is refreshed).</summary>
            private EquipmentObservation Observe(EquipmentKind kind, PlayerId by, double now)
            {
                RefreshMachines();
                Machine machine = MachineOf(kind);
                observationSequence = checked(observationSequence + 1);
                machine.Observation = new EquipmentObservation(observationSequence, kind, by, now, now + scenario.ObservationValiditySeconds,
                    machine.StateSequence, FindingsOf(kind), ReadingsOf(kind, now), true);
                return machine.Observation;
            }

            // ------------------------------------------------------------ activities and decisions

            private long StartActivity(ActivityKind kind, string key, PlayerId by, double now)
            {
                activitySequence = checked(activitySequence + 1);
                activities.Add(new Activity
                {
                    Sequence = activitySequence, Kind = kind, Key = key, Status = ActivityStatus.Active, StartedBy = by, StartedAt = now
                });
                while (activities.Count > BaseOperationsLimits.MaxActivities) activities.RemoveAt(0);
                return activitySequence;
            }

            private void EndActivity(long sequence, ActivityStatus status, string resultKey, double now)
            {
                if (sequence == 0) return;
                foreach (Activity activity in activities)
                {
                    if (activity.Sequence != sequence || activity.Status != ActivityStatus.Active) continue;
                    activity.Status = status;
                    activity.EndedAt = Math.Max(now, activity.StartedAt);
                    activity.ResultKey = resultKey;
                    if (status == ActivityStatus.Completed) reportCompleted++;
                    else reportCancelled++;
                    return;
                }
            }

            private void Decide(Guid id, PlayerId performer, BaseOperationAction action, OperationChoice choice, EquipmentKind kind,
                double now, DecisionAssessment assessment, string explanation)
            {
                decisions.Add(new DecisionRecord(id, performer, action, choice, kind, now, assessment, explanation));
                while (decisions.Count > BaseOperationsLimits.MaxDecisions) decisions.RemoveAt(0);
            }

            // ------------------------------------------------------------ orders and reports

            private void AdvanceOrders(double now)
            {
                foreach (Order order in orders)
                {
                    if (order.State == OrderState.Pending && now >= order.CommitAt - Tolerance) order.State = OrderState.InTransit;
                    if (order.State == OrderState.InTransit && now >= order.ArriveAt - Tolerance) order.State = OrderState.Arrived;
                }
            }

            private void AddOrder(Order order)
            {
                orders.Add(order);
                // Keep every open order; forget the oldest closed ones beyond the published window.
                while (orders.Count > BaseOperationsLimits.MaxOrders)
                {
                    int closed = orders.FindIndex(o => !o.InFlight);
                    if (closed < 0) break;
                    orders.RemoveAt(closed);
                }
            }

            private void StartReportInterval(double now)
            {
                LifeSupportSnapshot life = Base.LifeSupport;
                reportFrom = now;
                nextReportAt = now + scenario.ReportIntervalSeconds;
                reportCompleted = reportCancelled = 0;
                reportPotable = life.Water.PotableKilograms;
                reportFood = life.Food.StoredKilograms;
                reportEnergy = Base.Power.EnergyJoules;
            }

            private void IssueReport(double now)
            {
                if (now <= reportFrom) return;
                LifeSupportSnapshot life = Base.LifeSupport;
                reportSequence = checked(reportSequence + 1);
                latestReport = new OperationsReport(reportSequence, reportFrom, now, reportCompleted, reportCancelled,
                    life.Water.PotableKilograms - reportPotable, life.Food.StoredKilograms - reportFood, Base.Power.EnergyJoules - reportEnergy);
                StartReportInterval(now);
            }

            private void ClampSuspect()
            {
                double potable = Base.LifeSupport.Water.PotableKilograms;
                if (suspectKilograms > potable) suspectKilograms = potable;
                if (suspectKilograms <= Tolerance) suspectKilograms = 0d;
            }

            // ------------------------------------------------------------ trends

            private double?[] GaugeValues()
            {
                OutpostSimulationSnapshot r = Base;
                LifeSupportSnapshot life = r.LifeSupport;
                return new double?[]
                {
                    r.Power.EnergyJoules,
                    PowerBalance(r),
                    life.Water.PotableKilograms,
                    life.Water.UntreatedKilograms,
                    Truth.WaterProcessor.HoldKilograms,
                    life.Food.StoredKilograms,
                    life.Cabin.Oxygen.Status == ReadingStatus.Valid ? life.Cabin.Oxygen.PartialPressureKilopascals : null,
                    life.Cabin.CarbonDioxide.Status == ReadingStatus.Valid ? life.Cabin.CarbonDioxide.PartialPressureKilopascals : null
                };
            }

            private static double PowerBalance(OutpostSimulationSnapshot r) =>
                r.Power.GenerationWatts - r.Power.EssentialRequestedWatts - r.Power.ScienceRequestedWatts - r.ServiceLoadRequestedWatts;

            private void CaptureTrendBaseline()
            {
                double?[] values = GaugeValues();
                for (int i = 0; i < values.Length; i++)
                {
                    trendBeforeValid[i] = values[i].HasValue;
                    trendBefore[i] = values[i] ?? 0d;
                }
            }

            private void ComputeTrends(double elapsed)
            {
                double?[] values = GaugeValues();
                for (int i = 0; i < values.Length; i++)
                    trends[i] = elapsed > 0d && trendBeforeValid[i] && values[i].HasValue ? (values[i].Value - trendBefore[i]) / elapsed : (double?)null;
            }

            // ------------------------------------------------------------ helpers

            private Operation FindOperation(PlayerId player)
            {
                foreach (Operation op in operations.Values)
                    if (op.Performer == player) return op;
                return null;
            }
        }
    }
}
