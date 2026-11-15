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
        private sealed partial class ContinuousPolicy
        {
            // ------------------------------------------------------------ validation (SESSION_CONTRACT §5 order)

            public string Validate(PlayerId sender, BaseOperationCommand command)
            {
                ExpeditionAuthority session = owner.inner;
                if (!session.TryGetMember(sender, out CrewMemberState pose, out long zoneSequence, out bool sealedNow)) return OutpostReasons.NotJoined;
                if (command.Epoch != session.CurrentEpoch) return OutpostReasons.EpochMismatch;
                string shape = command.ShapeError();
                if (shape != null) return shape;
                if (session.IsEnded) return OutpostReasons.SessionEnded;
                bool allowedPaused = command.Action == BaseOperationAction.CancelOperation || command.Action == BaseOperationAction.CancelOrder;
                SessionState state = session.CurrentState;
                if (state != SessionState.Running && !(allowedPaused && state == SessionState.Paused)) return OutpostReasons.InvalidState;
                if (!scenario.Layout.TryGetStation(command.Station, out StationPlacement placement)) return OutpostReasons.UnknownStation;
                if (!StationFits(command.Action, command.Station)) return BaseOperationReasons.WrongStationForAction;
                if (command.Action == BaseOperationAction.CancelOperation)
                    return operations.TryGetValue(command.Station, out Operation own) && own.Performer == sender ? null : OutpostReasons.NoActiveTask;
                if (session.HasTask(sender) || FindOperation(sender) != null) return OutpostReasons.PlayerBusy;
                if (operations.ContainsKey(command.Station)) return OutpostReasons.StationOccupied;
                if (command.ZoneSequence != BaseOperationCommand.Unstated && command.ZoneSequence != zoneSequence) return ExpeditionReasons.StaleZone;
                if (!placement.IsWithinReach(pose.Pose.Position)) return OutpostReasons.OutOfRange;
                if (sealedNow) return ExpeditionReasons.SealedNotAllowed;
                return ValidateAction(command, session.SimulatedNow);
            }

            private bool StationFits(BaseOperationAction action, StationId station)
            {
                switch (action)
                {
                    case BaseOperationAction.ProcessBatch:
                    case BaseOperationAction.RouteProduct:
                    case BaseOperationAction.RecheckPotable:
                        return station == scenario.WaterStation;
                    case BaseOperationAction.ServiceAir:
                        return station == scenario.AirStation;
                    case BaseOperationAction.Inspect:
                    case BaseOperationAction.Isolate:
                    case BaseOperationAction.Intervene:
                    case BaseOperationAction.TestEquipment:
                    case BaseOperationAction.Restore:
                        return scenario.EquipmentAt(station).HasValue;
                    case BaseOperationAction.PlaceOrder:
                    case BaseOperationAction.CancelOrder:
                    case BaseOperationAction.UnloadDelivery:
                        return station == scenario.Logistics.Console;
                    case BaseOperationAction.CancelOperation:
                        return scenario.EquipmentAt(station).HasValue || station == scenario.Logistics.Console;
                    default:
                        return false;
                }
            }

            private bool Isolated(EquipmentKind kind) =>
                (kind == EquipmentKind.WaterProcessor ? Truth.WaterProcessor.Operating : Truth.Air.Operating) == EquipmentOperatingState.Isolated;

            private string CheckObservation(Machine machine, long stated, double now)
            {
                if (machine.Observation == null) return BaseOperationReasons.ObservationRequired;
                if (stated != machine.Observation.Sequence || !IsCurrent(machine.Observation, machine, now)) return BaseOperationReasons.StaleObservation;
                return null;
            }

            /// <summary>Repair is justified by a fault finding (not by a saturated bed, which is serviced without isolation).</summary>
            private static bool JustifiesRepair(EquipmentKind kind, Findings findings) =>
                kind == EquipmentKind.WaterProcessor
                    ? (findings & (Findings.ProcessorFilterRestricted | Findings.ProcessorFailed)) != 0
                    : (findings & Findings.InletFlowRestricted) != 0;

            private string ValidateAction(BaseOperationCommand command, double now)
            {
                LifeSupportSnapshot life = Base.LifeSupport;
                WaterProcessorState water = Truth.WaterProcessor;
                EquipmentKind kind = scenario.EquipmentAt(command.Station) ?? EquipmentKind.WaterProcessor;
                Machine machine = MachineOf(kind);
                string reason;
                switch (command.Action)
                {
                    case BaseOperationAction.Inspect:
                        return Isolated(kind) ? BaseOperationReasons.EquipmentIsolated : null;

                    case BaseOperationAction.ProcessBatch:
                        if (Isolated(kind)) return BaseOperationReasons.EquipmentIsolated;
                        if (water.Condition == EquipmentCondition.Failed)
                        {
                            machine.FailureSeen = true; // the refusal itself is the visible symptom
                            return BaseOperationReasons.EquipmentFailed;
                        }
                        if (water.HoldKilograms > Tolerance) return BaseOperationReasons.HoldNotEmpty;
                        if (life.Water.UntreatedKilograms + Tolerance < scenario.Equipment.WaterProcessor.BatchKilograms)
                            return OutpostReasons.InsufficientResources;
                        return null;

                    case BaseOperationAction.RouteProduct:
                        if (Isolated(kind)) return BaseOperationReasons.EquipmentIsolated;
                        if ((reason = CheckObservation(machine, command.ObservationSequence, now)) != null) return reason;
                        if (water.HoldKilograms <= Tolerance) return BaseOperationReasons.HoldEmpty;
                        return RouteFits(command.Choice, water.HoldKilograms) ? null : BaseOperationReasons.NoStorageCapacity;

                    case BaseOperationAction.RecheckPotable:
                        if (suspectKilograms <= 0d) return BaseOperationReasons.PotableNotSuspect;
                        return RecheckAmount() > Tolerance ? null : BaseOperationReasons.NoStorageCapacity;

                    case BaseOperationAction.ServiceAir:
                        if (Isolated(kind)) return BaseOperationReasons.EquipmentIsolated;
                        return CheckObservation(machine, command.ObservationSequence, now);

                    case BaseOperationAction.Isolate:
                        if (Isolated(kind)) return BaseOperationReasons.EquipmentIsolated;
                        if ((reason = CheckObservation(machine, command.ObservationSequence, now)) != null) return reason;
                        if (!JustifiesRepair(kind, machine.Observation.Findings)) return BaseOperationReasons.NothingToRepair;
                        // Decide the held product first: the test of a repair must not sample product made before it.
                        if (kind == EquipmentKind.WaterProcessor && water.HoldKilograms > Tolerance) return BaseOperationReasons.HoldNotEmpty;
                        return null;

                    case BaseOperationAction.Intervene:
                    {
                        if (!Isolated(kind)) return BaseOperationReasons.EquipmentNotIsolated;
                        if ((reason = CheckObservation(machine, command.ObservationSequence, now)) != null) return reason;
                        if (!RepairSequence.IsAllowed(machine.Completed, RepairSteps.Intervene, machine.LastTestPassed))
                            return BaseOperationReasons.RepairStepOutOfOrder;
                        bool forWater = command.Choice == OperationChoice.ReplaceProcessorFilter;
                        if (forWater != (kind == EquipmentKind.WaterProcessor)) return BaseOperationReasons.InterventionNotForEquipment;
                        return FreeSpares(SpareFor(command.Choice)) >= 1 ? null : BaseOperationReasons.SpareUnavailable;
                    }

                    case BaseOperationAction.TestEquipment:
                        if (!Isolated(kind)) return BaseOperationReasons.EquipmentNotIsolated;
                        return RepairSequence.IsAllowed(machine.Completed, RepairSteps.Test, machine.LastTestPassed)
                            ? null : BaseOperationReasons.RepairStepOutOfOrder;

                    case BaseOperationAction.Restore:
                        if (!Isolated(kind)) return BaseOperationReasons.EquipmentNotIsolated;
                        if ((reason = CheckObservation(machine, command.ObservationSequence, now)) != null) return reason;
                        return RepairSequence.IsAllowed(machine.Completed, RepairSteps.Restore, machine.LastTestPassed)
                            ? null : BaseOperationReasons.RepairStepOutOfOrder;

                    case BaseOperationAction.PlaceOrder:
                        return OrderRule(command.ManifestKey, now);

                    case BaseOperationAction.CancelOrder:
                    {
                        Order order = FindOrder(command.OrderSequence);
                        if (order == null) return BaseOperationReasons.UnknownOrder;
                        return order.State == OrderState.Pending ? null : BaseOperationReasons.OrderNotCancellable;
                    }

                    case BaseOperationAction.UnloadDelivery:
                    {
                        Order order = FindOrder(command.OrderSequence);
                        if (order == null) return BaseOperationReasons.UnknownOrder;
                        if (order.State != OrderState.Arrived) return BaseOperationReasons.OrderNotArrived;
                        return UnloadPlan(order, out _, out _, out _) ? null : BaseOperationReasons.NoStorageCapacity;
                    }

                    default:
                        return OutpostReasons.UnknownAction;
                }
            }

            private string OrderRule(string manifestKey, double now)
            {
                if (!scenario.TryGetManifest(manifestKey, out _)) return BaseOperationReasons.UnknownManifest;
                int inFlight = 0;
                foreach (Order order in orders)
                    if (order.InFlight) inFlight++;
                if (inFlight >= scenario.Logistics.MaxOrdersInFlight) return BaseOperationReasons.OrderLimitReached;
                if (lastOrderAt.HasValue && now < lastOrderAt.Value + scenario.Logistics.MinSecondsBetweenOrders - Tolerance)
                    return BaseOperationReasons.OrderTooSoon;
                return null;
            }

            private bool RouteFits(OperationChoice choice, double hold)
            {
                WaterStock w = Base.LifeSupport.Water;
                double fit = choice == OperationChoice.AcceptToPotable
                    ? BaseOperationsTransfers.Fit(hold, w.PotableKilograms, w.PotableCapacityKilograms)
                    : BaseOperationsTransfers.Fit(hold, w.UntreatedKilograms, w.UntreatedCapacityKilograms);
                return fit >= hold - Tolerance;
            }

            /// <summary>Suspect mass that a recheck can move now (what remains of it, limited by untreated room).</summary>
            private double RecheckAmount()
            {
                WaterStock w = Base.LifeSupport.Water;
                double suspect = Math.Min(suspectKilograms, w.PotableKilograms);
                return suspect <= Tolerance ? 0d : BaseOperationsTransfers.Fit(suspect, w.UntreatedKilograms, w.UntreatedCapacityKilograms);
            }

            private string SpareFor(OperationChoice choice) =>
                choice == OperationChoice.ReplaceProcessorFilter ? scenario.Equipment.WaterProcessor.FilterSpareKey : scenario.Equipment.Air.InletFilterSpareKey;

            private int FreeSpares(string key)
            {
                int free = Truth.TryGetSpare(key, out SpareStock spare) ? spare.Count : 0;
                foreach (Operation op in operations.Values)
                    if (op.Spare == key) free--;
                return free;
            }

            private Order FindOrder(long sequence) => orders.Find(o => o.Sequence == sequence);

            /// <summary>What fits now: potable and food masses and a spare count per key. False when nothing fits.</summary>
            private bool UnloadPlan(Order order, out double potable, out double food, out Dictionary<string, int> spares)
            {
                WaterStock w = Base.LifeSupport.Water;
                FoodStock f = Base.LifeSupport.Food;
                potable = food = 0d;
                spares = new Dictionary<string, int>(StringComparer.Ordinal);
                bool any = false;
                foreach (ManifestItem item in order.Remaining)
                {
                    if (item.IsSpare)
                    {
                        int n = Math.Min(item.Count, BaseOperationsTransfers.SpareRoom(Truth, item.SpareKey));
                        if (n > 0)
                        {
                            spares[item.SpareKey] = n;
                            any = true;
                        }
                    }
                    else if (item.Resource == ResourceKind.PotableWater)
                    {
                        potable = BaseOperationsTransfers.Fit(item.Kilograms, w.PotableKilograms, w.PotableCapacityKilograms);
                        any |= potable > Tolerance;
                    }
                    else if (item.Resource == ResourceKind.StoredFood)
                    {
                        food = BaseOperationsTransfers.Fit(item.Kilograms, f.StoredKilograms, f.CapacityKilograms);
                        any |= food > Tolerance;
                    }
                }
                if (potable <= Tolerance) potable = 0d;
                if (food <= Tolerance) food = 0d;
                return any;
            }

            // ------------------------------------------------------------ acceptance

            public void Accept(PlayerId sender, BaseOperationCommand command)
            {
                double now = owner.inner.SimulatedNow;
                switch (command.Action)
                {
                    case BaseOperationAction.CancelOperation:
                        EndOperation(operations[command.Station], ActivityStatus.Cancelled, "cancelled-by-performer", now);
                        return;
                    case BaseOperationAction.CancelOrder:
                    {
                        Order order = FindOrder(command.OrderSequence);
                        order.State = OrderState.Cancelled;
                        order.ClosedAt = now;
                        EndActivity(order.Activity, ActivityStatus.Cancelled, "order-cancelled", now);
                        return;
                    }
                }

                OperationDefinition definition = scenario.DefinitionOf(command.Action, command.Choice);
                var op = new Operation
                {
                    CommandId = command.Id,
                    Station = command.Station,
                    Action = command.Action,
                    Choice = command.Choice,
                    Performer = sender,
                    Duration = definition.DurationSeconds,
                    Power = definition.PowerWatts,
                    Machine = scenario.EquipmentAt(command.Station),
                    OrderSequence = command.OrderSequence,
                    ManifestKey = command.ManifestKey,
                    ObservationSequence = command.ObservationSequence
                };
                if (BaseOperationCommand.NeedsObservation(command.Action)) RecordDecision(op, now);
                switch (command.Action)
                {
                    case BaseOperationAction.ProcessBatch:
                        batchActivity = StartActivity(ActivityKind.WaterBatch, "water-batch", sender, now);
                        break;
                    case BaseOperationAction.ServiceAir:
                        op.Activity = StartActivity(ActivityKind.AirService, "air-service", sender, now);
                        break;
                    case BaseOperationAction.Intervene:
                        op.Spare = SpareFor(command.Choice); // reserved now, consumed by D1 only at completion
                        break;
                }
                if (definition.DurationSeconds <= 0d) Complete(op, now);
                else operations.Add(op.Station, op);
            }

            /// <summary>Judges a decision against the truth at acceptance (explanation, not a score).</summary>
            private void RecordDecision(Operation op, double now)
            {
                EquipmentKind kind = op.Machine ?? EquipmentKind.WaterProcessor;
                Findings truth = FindingsOf(kind);
                DecisionAssessment assessment;
                string key;
                switch (op.Action)
                {
                    case BaseOperationAction.RouteProduct:
                    {
                        bool offSpec = OffSpec(Truth.WaterProcessor.ProductConductivity);
                        if (op.Choice == OperationChoice.AcceptToPotable)
                        {
                            assessment = offSpec ? DecisionAssessment.Harmful : DecisionAssessment.Appropriate;
                            key = offSpec ? "accepted-off-spec-product" : "accepted-within-limit";
                        }
                        else
                        {
                            assessment = offSpec ? DecisionAssessment.Appropriate : DecisionAssessment.NotIndicated;
                            key = offSpec ? "reprocessed-off-spec-product" : "reprocessed-within-limit";
                        }
                        break;
                    }
                    case BaseOperationAction.ServiceAir:
                        if (op.Choice == OperationChoice.SwitchBed)
                        {
                            bool saturated = (truth & Findings.ActiveBedSaturated) != 0;
                            bool standbyLoaded = (truth & Findings.StandbyBedLoaded) != 0;
                            assessment = saturated && !standbyLoaded ? DecisionAssessment.Appropriate : DecisionAssessment.NotIndicated;
                            key = !saturated ? "bed-switch-not-needed" : standbyLoaded ? "switched-to-loaded-bed" : "bed-switch-restored-removal";
                        }
                        else
                        {
                            bool standbyLoaded = (truth & Findings.StandbyBedLoaded) != 0;
                            assessment = standbyLoaded ? DecisionAssessment.Appropriate : DecisionAssessment.NotIndicated;
                            key = standbyLoaded ? "standby-bed-regenerated" : "standby-bed-already-clean";
                        }
                        break;
                    case BaseOperationAction.Isolate:
                        assessment = DecisionAssessment.Appropriate;
                        key = "isolated-for-repair";
                        break;
                    case BaseOperationAction.Intervene:
                        if (op.Choice == OperationChoice.ReplaceProcessorFilter)
                        {
                            bool restricted = (truth & Findings.ProcessorFilterRestricted) != 0;
                            assessment = restricted ? DecisionAssessment.Appropriate : DecisionAssessment.NotIndicated;
                            key = restricted ? "processor-filter-replaced" : "processor-filter-not-indicated";
                        }
                        else
                        {
                            bool restricted = (truth & Findings.InletFlowRestricted) != 0;
                            assessment = restricted ? DecisionAssessment.Appropriate : DecisionAssessment.NotIndicated;
                            key = restricted ? "inlet-filter-replaced" : "inlet-filter-not-indicated";
                        }
                        break;
                    default:
                        assessment = DecisionAssessment.Appropriate;
                        key = "restored-after-passed-test";
                        break;
                }
                Decide(op.CommandId, op.Performer, op.Action, op.Choice, kind, now, assessment, key);
            }

            // ------------------------------------------------------------ progress and completion

            private void CreditOperations(double elapsed, double service, double now)
            {
                foreach (StationPlacement station in scenario.Layout.Stations)
                {
                    if (!operations.TryGetValue(station.Id, out Operation op)) continue;
                    double credit = op.Power > 0d ? service : elapsed;
                    op.Stalled = credit < elapsed * (1d - ExpeditionAuthority.ServiceTolerance);
                    op.Work = Math.Min(op.Duration, op.Work + credit);
                    if (op.Duration - op.Work > OutpostMissionEvaluator.TimeToleranceSeconds) continue;
                    op.Work = op.Duration;
                    operations.Remove(op.Station);
                    Complete(op, now);
                }
            }

            /// <summary>Ends an operation without its effect (cancel, leave, moved away, session end, failed completion).
            /// The reserved spare is released by removing the operation; energy already used is not refunded.</summary>
            private void EndOperation(Operation op, ActivityStatus status, string resultKey, double now)
            {
                operations.Remove(op.Station);
                if (op.Action == BaseOperationAction.ProcessBatch)
                {
                    EndActivity(batchActivity, status, resultKey, now);
                    batchActivity = 0;
                }
                else if (op.Activity != 0) EndActivity(op.Activity, status, resultKey, now);
            }

            private void Complete(Operation op, double now)
            {
                EquipmentKind kind = op.Machine ?? EquipmentKind.WaterProcessor;
                Machine machine = MachineOf(kind);
                switch (op.Action)
                {
                    case BaseOperationAction.Inspect:
                        Observe(kind, op.Performer, now);
                        break;

                    case BaseOperationAction.ProcessBatch:
                    {
                        WaterProcessorState water = Truth.WaterProcessor;
                        if (water.Operating == EquipmentOperatingState.Isolated || water.Condition == EquipmentCondition.Failed)
                        {
                            machine.FailureSeen = water.Condition == EquipmentCondition.Failed;
                            EndOperation(op, ActivityStatus.Failed, "processor-failed", now);
                            return;
                        }
                        WaterProcessorConfiguration config = scenario.Equipment.WaterProcessor;
                        double kg = config.BatchKilograms;
                        // Destination first (D1 rule): the equipment must be able to take the batch BEFORE the base gives up
                        // the untreated water, so a refusal never loses mass or faults the session (integration fix).
                        double recovery = water.Condition == EquipmentCondition.Nominal ? config.NominalRecoveryFraction : config.DegradedRecoveryFraction;
                        if (water.HoldKilograms > 0d || kg * recovery > config.HoldCapacityKilograms + Tolerance)
                        {
                            EndOperation(op, ActivityStatus.Failed, "equipment-refused", now);
                            return;
                        }
                        if (!ApplyBase(BaseOperationsTransfers.BatchIntake(op.CommandId, kg)))
                        {
                            EndOperation(op, ActivityStatus.Failed, "insufficient-untreated-water", now);
                            return;
                        }
                        Expect(owner.equipment.Apply(EquipmentEffect.ReceiveBatch(op.CommandId, kg)));
                        batchesProcessed++;
                        Observe(kind, op.Performer, now); // the batch reading is a new observation
                        break;
                    }

                    case BaseOperationAction.RouteProduct:
                    {
                        WaterProcessorState water = Truth.WaterProcessor;
                        double hold = water.HoldKilograms;
                        if (hold <= Tolerance || !RouteFits(op.Choice, hold))
                        {
                            EndOperation(op, ActivityStatus.Failed, "no-storage-capacity", now);
                            return;
                        }
                        bool offSpec = OffSpec(water.ProductConductivity);
                        if (op.Choice == OperationChoice.AcceptToPotable)
                        {
                            // D1 rule: check the destination before releasing; then equipment side, then base side, same id.
                            Expect(owner.equipment.Apply(EquipmentEffect.ReleaseToPotable(op.CommandId, hold)));
                            ExpectBase(BaseOperationsTransfers.ProductToPotable(op.CommandId, hold));
                            if (offSpec) suspectKilograms += hold;
                            EndActivity(batchActivity, ActivityStatus.Completed, offSpec ? "accepted-off-spec-product" : "product-accepted", now);
                        }
                        else
                        {
                            Expect(owner.equipment.Apply(EquipmentEffect.ReleaseForReprocess(op.CommandId, hold)));
                            ExpectBase(BaseOperationsTransfers.ProductToUntreated(op.CommandId, hold));
                            EndActivity(batchActivity, ActivityStatus.Completed, "product-reprocessed", now);
                        }
                        batchActivity = 0;
                        break;
                    }

                    case BaseOperationAction.RecheckPotable:
                    {
                        double move = RecheckAmount();
                        if (move <= Tolerance) break; // nothing suspect remains (drunk or already moved)
                        ExpectBase(BaseOperationsTransfers.RecheckPotable(op.CommandId, move));
                        suspectKilograms = Math.Max(0d, Math.Min(suspectKilograms, Base.LifeSupport.Water.PotableKilograms + move) - move);
                        ClampSuspect();
                        break;
                    }

                    case BaseOperationAction.ServiceAir:
                    {
                        EquipmentEffect effect = op.Choice == OperationChoice.SwitchBed
                            ? EquipmentEffect.SwitchBed(op.CommandId) : EquipmentEffect.RegenerateStandbyBed(op.CommandId);
                        if (owner.equipment.Apply(effect) != EquipmentEffectStatus.Applied)
                        {
                            EndOperation(op, ActivityStatus.Failed, "equipment-refused", now);
                            return;
                        }
                        EndActivity(op.Activity, ActivityStatus.Completed,
                            op.Choice == OperationChoice.SwitchBed ? "bed-switched" : "standby-bed-regenerated", now);
                        break;
                    }

                    case BaseOperationAction.Isolate:
                        Expect(owner.equipment.Apply(EquipmentEffect.SetOperatingState(op.CommandId, kind, EquipmentOperatingState.Isolated)));
                        machine.RepairOpen = true;
                        machine.Completed = RepairSteps.Inspect | RepairSteps.Isolate;
                        machine.RepairStartedAt = now;
                        machine.RepairObservation = op.ObservationSequence;
                        machine.LastTestPassed = null;
                        machine.RepairActivity = StartActivity(ActivityKind.Repair, kind == EquipmentKind.WaterProcessor ? "water-processor" : "air-revitalization",
                            op.Performer, now);
                        Observe(kind, op.Performer, now); // the isolated machine as it stands: the basis of the intervention
                        break;

                    case BaseOperationAction.Intervene:
                    {
                        EquipmentEffect effect = op.Choice == OperationChoice.ReplaceProcessorFilter
                            ? EquipmentEffect.ReplaceProcessorFilter(op.CommandId) : EquipmentEffect.ReplaceInletFilter(op.CommandId);
                        if (owner.equipment.Apply(effect) != EquipmentEffectStatus.Applied)
                        {
                            EndOperation(op, ActivityStatus.Failed, "equipment-refused", now);
                            return;
                        }
                        machine.Completed = (machine.Completed | RepairSteps.Intervene) & ~RepairSteps.Test; // a new test is needed
                        machine.LastTestPassed = null;
                        break;
                    }

                    case BaseOperationAction.TestEquipment:
                    {
                        bool passed;
                        if (kind == EquipmentKind.WaterProcessor)
                        {
                            Expect(owner.equipment.Apply(EquipmentEffect.SampleProduct(op.CommandId)));
                            InstrumentReading reading = Truth.WaterProcessor.ProductConductivity;
                            passed = reading.Status == ReadingStatus.Valid && !OffSpec(reading);
                        }
                        else passed = Truth.Air.InletFlowFraction >= 1d - Tolerance;
                        machine.Completed |= RepairSteps.Test;
                        machine.LastTestPassed = passed;
                        Observe(kind, op.Performer, now);
                        break;
                    }

                    case BaseOperationAction.Restore:
                        Expect(owner.equipment.Apply(EquipmentEffect.SetOperatingState(op.CommandId, kind, EquipmentOperatingState.Online)));
                        machine.RepairOpen = false;
                        machine.Completed = RepairSteps.None;
                        machine.LastTestPassed = null;
                        machine.FailureSeen = false;
                        EndActivity(machine.RepairActivity, ActivityStatus.Completed, "equipment-restored", now);
                        machine.RepairActivity = 0;
                        Observe(kind, op.Performer, now);
                        break;

                    case BaseOperationAction.PlaceOrder:
                    {
                        if (OrderRule(op.ManifestKey, now) != null) break; // limits changed meanwhile: nothing ordered
                        scenario.TryGetManifest(op.ManifestKey, out ResupplyManifest manifest);
                        orderSequence = checked(orderSequence + 1);
                        var order = new Order
                        {
                            Sequence = orderSequence,
                            ManifestKey = manifest.Key,
                            PlacedBy = op.Performer,
                            PlacedAt = now,
                            CommitAt = now + manifest.CancelWindowSeconds,
                            ArriveAt = now + manifest.LeadSeconds,
                            State = OrderState.Pending,
                            Remaining = new List<ManifestItem>(manifest.Items)
                        };
                        order.Activity = StartActivity(ActivityKind.Resupply, manifest.Key, op.Performer, now);
                        lastOrderAt = now;
                        AddOrder(order);
                        break;
                    }

                    case BaseOperationAction.UnloadDelivery:
                    {
                        Order order = FindOrder(op.OrderSequence);
                        if (order == null || order.State != OrderState.Arrived
                            || !UnloadPlan(order, out double potable, out double food, out Dictionary<string, int> spares))
                            break; // an interrupted or impossible unload applies nothing
                        ResourceTransfer inflow = BaseOperationsTransfers.DeliveryInflow(
                            BaseOperationsTransfers.DeriveId(op.CommandId, "unload-mass"), potable, food);
                        if (inflow != null) ExpectBase(inflow);
                        foreach (KeyValuePair<string, int> spare in spares)
                            Expect(owner.equipment.Apply(EquipmentEffect.ReceiveSpares(
                                BaseOperationsTransfers.DeriveId(op.CommandId, "unload-spare-" + spare.Key), spare.Key, spare.Value)));
                        var remaining = new List<ManifestItem>();
                        foreach (ManifestItem item in order.Remaining)
                        {
                            if (item.IsSpare)
                            {
                                spares.TryGetValue(item.SpareKey, out int n);
                                if (item.Count - n > 0) remaining.Add(ManifestItem.Spares(item.SpareKey, item.Count - n));
                            }
                            else
                            {
                                double moved = item.Resource == ResourceKind.PotableWater ? potable : food;
                                double left = item.Kilograms - moved;
                                if (left > 1e-6) remaining.Add(ManifestItem.Mass(item.Resource.Value, left));
                            }
                        }
                        order.Remaining = remaining;
                        if (remaining.Count == 0)
                        {
                            order.State = OrderState.Unloaded;
                            order.ClosedAt = now;
                            EndActivity(order.Activity, ActivityStatus.Completed, "delivery-unloaded", now);
                        }
                        break;
                    }
                }
                RefreshMachines();
            }

            private bool ApplyBase(ResourceTransfer transfer)
            {
                ResourceTransferStatus status = owner.outpost.Apply(transfer);
                if (status == ResourceTransferStatus.Applied) return true;
                if (status == ResourceTransferStatus.InsufficientStock || status == ResourceTransferStatus.ExceedsCapacity) return false;
                throw new InvalidOperationException($"IOutpostSimulation.Apply returned {status} for a new transfer.");
            }

            private void ExpectBase(ResourceTransfer transfer)
            {
                if (!ApplyBase(transfer)) throw new InvalidOperationException("A checked base transfer was refused (pair would break).");
            }

            private static void Expect(EquipmentEffectStatus status)
            {
                if (status != EquipmentEffectStatus.Applied)
                    throw new InvalidOperationException($"IBaseEquipmentModel.Apply returned {status} for a checked new effect.");
            }
        }
    }
}
