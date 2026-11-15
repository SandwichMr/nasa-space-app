// No test asmdef exists, so this file compiles into Assembly-CSharp; the guard keeps it out of player builds (same
// convention as the phase 1–3 network tests). It is shared by D3's EditMode loopback tests and PlayMode socket tests.
#if UNITY_INCLUDE_TESTS
using System;
using System.Collections.Generic;
using Trainer.Application;
using Trainer.Application.BaseOperations;
using Trainer.Application.Crew;
using Trainer.Application.Development;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.Eva;
using Trainer.Simulation.LifeSupport;

/// <summary>
/// TEST DOUBLE, NOT THE PHASE 4 AUTHORITY. D2 implements the real <see cref="IBaseOperationsAuthority"/> (continuous session,
/// station rules, equipment through D1). This double exists only so that D3's network adapters can be exercised end to end
/// before that authority is published, exactly like the phase 2 <c>CrewTestAuthority</c>. It applies just enough of the rules
/// documented in <c>docs/phase-04/SESSION_CONTRACT.md</c> (§5 order of validation, identity, epoch, one id space with
/// deduplication, occupation, reach on the confirmed position, observation stamps, spare reservation released on cancel or
/// Leave, orders kept on Leave, restart) over the REAL <see cref="CrewRoster"/>:
/// <list type="bullet">
/// <item>stocks are the fixture's initial readings (the clock moves, nothing is consumed or produced) and there is no
/// equipment model: no batches, routing, isolation, faults or bed loading;</item>
/// <item>effects: Inspect → a new current observation; Intervene → spare reserved at start, consumed at completion, machine
/// state sequence +1 (older observations go stale); PlaceOrder → an order (Pending → InTransit → Arrived by time); CancelOrder;
/// UnloadDelivery → Unloaded; CancelOperation. Other accepted operations only occupy their station for their duration;</item>
/// <item>phase 2/3 commands: only Pause, Resume and Restart (anything else → UnknownAction). Everyone stays inside: the inner
/// hatch is closed as an obstacle, so the zone is always Interior with sequence 0.</item>
/// </list>
/// </summary>
public sealed class Phase04NetworkTestAuthority : IBaseOperationsAuthority
{
    public const string DecisionKey = "test-double-decision";

    private sealed class Operation
    {
        public BaseOperationCommand Command;
        public PlayerId Performer;
        public double Work;
        public double Duration;
        public string Spare;
    }

    private sealed class Seen
    {
        public PlayerId Sender;
        public ExpeditionCommand Expedition;
        public BaseOperationCommand Operation;
        public CommandResult Result;
    }

    private sealed class Observation
    {
        public long Sequence;
        public PlayerId By;
        public double At;
        public double Until;
        public long StateSequence;
    }

    private sealed class Order
    {
        public long Sequence;
        public ResupplyManifest Manifest;
        public PlayerId PlacedBy;
        public double PlacedAt;
        public OrderState State;
        public double? ClosedAt;
        public long Activity;
    }

    private readonly Dictionary<Guid, Seen> seen = new Dictionary<Guid, Seen>();
    private readonly List<Operation> operations = new List<Operation>();
    private readonly Dictionary<string, int> spares = new Dictionary<string, int>(StringComparer.Ordinal);
    private readonly long[] stateSequence = new long[2];
    private readonly Observation[] observations = new Observation[2];
    private readonly List<Order> orders = new List<Order>();
    private readonly List<ActivityRecord> activities = new List<ActivityRecord>();
    private readonly List<DecisionRecord> decisions = new List<DecisionRecord>();
    private BaseOperationsSnapshot snapshot;
    private long epoch, revision, observationCounter, orderCounter, activityCounter;
    private double time;
    private double? lastOrderAt;

    public BaseOperationsScenario Scenario { get; }
    public CrewRoster Roster { get; }
    public SessionState State { get; private set; } = SessionState.Ready;
    public List<PlayerId> Left { get; } = new List<PlayerId>();
    public int SubmitCount { get; private set; }
    public int OperationSubmitCount { get; private set; }
    /// <summary>Spares consumed by completed interventions (each completion exactly once).</summary>
    public int SparesConsumed { get; private set; }
    public BaseOperationsSnapshot Current => snapshot;

    public event Action<BaseOperationsSnapshot> SnapshotChanged;
    public event Action<PlayerId, CommandResult> CommandCompleted;

    public Phase04NetworkTestAuthority(BaseOperationsScenario scenario = null)
    {
        Scenario = scenario ?? BaseOperationsFixtures.Scenario(null);
        Roster = CrewRoster.For(Scenario.Expedition.Outpost);
        Roster.SetObstacleOverlay(new[] { Scenario.Expedition.Airlock.InnerHatch });
        ResetSpares();
        Publish(false);
    }

    public bool TryGetSnapshot(out BaseOperationsSnapshot value)
    {
        value = snapshot;
        return value != null;
    }

    public OutpostMembership Join()
    {
        OutpostMembership membership = Roster.Join();
        if (membership.IsJoined) Publish(true);
        return membership;
    }

    public bool Leave(PlayerId player)
    {
        bool hadOperation = EndOperationsOf(player);
        bool left = Roster.Leave(player);
        if (!left && !hadOperation) return false;
        if (left) Left.Add(player);
        Publish(true);
        return left;
    }

    public MovementInputDisposition SubmitMovement(PlayerId sender, MovementInput input) => Roster.SubmitMovement(sender, input, epoch);

    public void Submit(PlayerId sender, ExpeditionCommand command)
    {
        if (command == null) throw new ArgumentNullException(nameof(command));
        SubmitCount++;
        if (command.Id == Guid.Empty)
        {
            CommandCompleted?.Invoke(sender, Result(command.Id, command.Epoch, OutpostReasons.InvalidCommandId));
            return;
        }
        if (seen.TryGetValue(command.Id, out Seen previous))
        {
            bool same = previous.Sender == sender && previous.Expedition != null && previous.Expedition.HasSameContent(command);
            CommandCompleted?.Invoke(sender, same ? previous.Result : Result(command.Id, command.Epoch, OutpostReasons.CommandIdConflict));
            return;
        }
        string rejection = Validate(sender, command);
        CommandResult result = Result(command.Id, command.Epoch, rejection ?? OutpostReasons.Accepted);
        seen.Add(command.Id, new Seen { Sender = sender, Expedition = command, Result = result });
        if (rejection == null)
        {
            bool restart = command.Action == ExpeditionAction.Restart;
            if (restart) Restart();
            else State = command.Action == ExpeditionAction.Pause ? SessionState.Paused : SessionState.Running;
            Publish(!restart);
        }
        CommandCompleted?.Invoke(sender, result);
    }

    public void Submit(PlayerId sender, BaseOperationCommand command)
    {
        if (command == null) throw new ArgumentNullException(nameof(command));
        OperationSubmitCount++;
        if (command.Id == Guid.Empty)
        {
            CommandCompleted?.Invoke(sender, Result(command.Id, command.Epoch, OutpostReasons.InvalidCommandId));
            return;
        }
        if (seen.TryGetValue(command.Id, out Seen previous))
        {
            bool same = previous.Sender == sender && previous.Operation != null && previous.Operation.HasSameContent(command);
            CommandCompleted?.Invoke(sender, same ? previous.Result : Result(command.Id, command.Epoch, OutpostReasons.CommandIdConflict));
            return;
        }
        string rejection = Validate(sender, command);
        CommandResult result = Result(command.Id, command.Epoch, rejection ?? OutpostReasons.Accepted);
        seen.Add(command.Id, new Seen { Sender = sender, Operation = command, Result = result });
        if (rejection == null)
        {
            Apply(sender, command);
            Publish(true); // state committed before the result
        }
        CommandCompleted?.Invoke(sender, result);
    }

    /// <summary>Advances the test clock while Running: roster movement, reach, operation progress and order times.</summary>
    public void Tick(double seconds)
    {
        if (State != SessionState.Running || seconds <= 0) return;
        Roster.Advance(seconds);
        time += seconds;
        foreach (Operation op in operations.ToArray())
        {
            Scenario.Layout.TryGetStation(op.Command.Station, out StationPlacement placement);
            if (!TryGetMember(op.Performer, out CrewMemberState member) || !placement.IsWithinReach(member.Pose.Position))
            {
                operations.Remove(op); // PerformerMovedAway: the reservation is released, energy is not refunded
                continue;
            }
            op.Work = Math.Min(op.Duration, op.Work + seconds);
            if (op.Work >= op.Duration) Complete(op);
        }
        foreach (Order order in orders)
        {
            if (order.State == OrderState.Pending && time >= CommitAt(order)) order.State = OrderState.InTransit;
            if (order.State == OrderState.InTransit && time >= ArriveAt(order)) order.State = OrderState.Arrived;
        }
        Publish(true);
    }

    // ---- validation (SESSION_CONTRACT §5, the subset this double needs) ----------------------------------------

    private string Validate(PlayerId sender, ExpeditionCommand command)
    {
        if (!TryGetMember(sender, out _)) return OutpostReasons.NotJoined;
        if (command.Epoch != epoch) return OutpostReasons.EpochMismatch;
        string shape = command.ShapeError();
        if (shape != null) return shape;
        switch (command.Action)
        {
            case ExpeditionAction.Restart: return null;
            case ExpeditionAction.Pause: return State == SessionState.Running ? null : OutpostReasons.InvalidState;
            case ExpeditionAction.Resume: return State == SessionState.Ready || State == SessionState.Paused ? null : OutpostReasons.InvalidState;
            default: return OutpostReasons.UnknownAction; // the double runs no phase 2/3 task, airlock or return
        }
    }

    private string Validate(PlayerId sender, BaseOperationCommand c)
    {
        if (!TryGetMember(sender, out CrewMemberState member)) return OutpostReasons.NotJoined;
        if (c.Epoch != epoch) return OutpostReasons.EpochMismatch;
        string shape = c.ShapeError();
        if (shape != null) return shape;
        bool cancel = c.Action == BaseOperationAction.CancelOperation || c.Action == BaseOperationAction.CancelOrder;
        if (!(State == SessionState.Running || (cancel && State == SessionState.Paused))) return OutpostReasons.InvalidState;
        if (!Scenario.Layout.TryGetStation(c.Station, out StationPlacement placement)) return OutpostReasons.UnknownStation;
        if (!StationFits(c)) return BaseOperationReasons.WrongStationForAction;
        if (c.Action == BaseOperationAction.CancelOperation)
            return operations.Exists(o => o.Performer == sender && o.Command.Station == c.Station) ? null : OutpostReasons.NoActiveTask;
        if (operations.Exists(o => o.Performer == sender)) return OutpostReasons.PlayerBusy;
        if (operations.Exists(o => o.Command.Station == c.Station)) return OutpostReasons.StationOccupied;
        if (c.ZoneSequence != BaseOperationCommand.Unstated && c.ZoneSequence != 0) return ExpeditionReasons.StaleZone;
        if (!placement.IsWithinReach(member.Pose.Position)) return OutpostReasons.OutOfRange;
        EquipmentKind? machine = Scenario.EquipmentAt(c.Station);
        if (BaseOperationCommand.NeedsObservation(c.Action))
        {
            Observation o = observations[(int)machine.Value];
            if (o == null) return BaseOperationReasons.ObservationRequired;
            if (o.Sequence != c.ObservationSequence || !IsCurrent(machine.Value, o)) return BaseOperationReasons.StaleObservation;
        }
        switch (c.Action)
        {
            case BaseOperationAction.Intervene:
                bool forWater = c.Choice == OperationChoice.ReplaceProcessorFilter;
                if (forWater != (machine.Value == EquipmentKind.WaterProcessor)) return BaseOperationReasons.InterventionNotForEquipment;
                return FreeSpares(SpareFor(c.Choice)) > 0 ? null : BaseOperationReasons.SpareUnavailable;
            case BaseOperationAction.PlaceOrder:
                if (!Scenario.TryGetManifest(c.ManifestKey, out _)) return BaseOperationReasons.UnknownManifest;
                if (orders.FindAll(o => o.State == OrderState.Pending || o.State == OrderState.InTransit || o.State == OrderState.Arrived).Count
                    >= Scenario.Logistics.MaxOrdersInFlight) return BaseOperationReasons.OrderLimitReached;
                if (lastOrderAt.HasValue && time < lastOrderAt.Value + Scenario.Logistics.MinSecondsBetweenOrders) return BaseOperationReasons.OrderTooSoon;
                return null;
            case BaseOperationAction.CancelOrder:
            {
                Order order = orders.Find(o => o.Sequence == c.OrderSequence);
                if (order == null) return BaseOperationReasons.UnknownOrder;
                return order.State == OrderState.Pending ? null : BaseOperationReasons.OrderNotCancellable;
            }
            case BaseOperationAction.UnloadDelivery:
            {
                Order order = orders.Find(o => o.Sequence == c.OrderSequence);
                if (order == null) return BaseOperationReasons.UnknownOrder;
                return order.State == OrderState.Arrived ? null : BaseOperationReasons.OrderNotArrived;
            }
            default: return null;
        }
    }

    private bool StationFits(BaseOperationCommand c)
    {
        switch (c.Action)
        {
            case BaseOperationAction.CancelOperation: return true;
            case BaseOperationAction.ProcessBatch:
            case BaseOperationAction.RouteProduct:
            case BaseOperationAction.RecheckPotable: return c.Station == Scenario.WaterStation;
            case BaseOperationAction.ServiceAir: return c.Station == Scenario.AirStation;
            case BaseOperationAction.PlaceOrder:
            case BaseOperationAction.CancelOrder:
            case BaseOperationAction.UnloadDelivery: return c.Station == Scenario.Logistics.Console;
            default: return Scenario.EquipmentAt(c.Station).HasValue;
        }
    }

    // ---- effects -------------------------------------------------------------------------------------------------

    private void Apply(PlayerId sender, BaseOperationCommand c)
    {
        if (c.Action == BaseOperationAction.CancelOperation)
        {
            operations.RemoveAll(o => o.Performer == sender && o.Command.Station == c.Station); // reservation released
            return;
        }
        if (c.Action == BaseOperationAction.CancelOrder)
        {
            Order order = orders.Find(o => o.Sequence == c.OrderSequence);
            order.State = OrderState.Cancelled;
            order.ClosedAt = time;
            CloseActivity(order.Activity, ActivityStatus.Cancelled, "order-cancelled");
            return;
        }
        if (BaseOperationCommand.NeedsObservation(c.Action))
        {
            decisions.Add(new DecisionRecord(c.Id, sender, c.Action, c.Choice, Scenario.EquipmentAt(c.Station).Value, time,
                DecisionAssessment.Appropriate, DecisionKey));
            if (decisions.Count > BaseOperationsLimits.MaxDecisions) decisions.RemoveAt(0);
        }
        OperationDefinition definition = Scenario.DefinitionOf(c.Action, c.Choice);
        if (definition.DurationSeconds <= 0) return; // immediate (Isolate, Restore): no equipment model in this double
        operations.Add(new Operation
        {
            Command = c, Performer = sender, Duration = definition.DurationSeconds,
            Spare = c.Action == BaseOperationAction.Intervene ? SpareFor(c.Choice) : null
        });
    }

    private void Complete(Operation op)
    {
        operations.Remove(op);
        BaseOperationCommand c = op.Command;
        EquipmentKind? machine = Scenario.EquipmentAt(c.Station);
        switch (c.Action)
        {
            case BaseOperationAction.Inspect:
                observations[(int)machine.Value] = new Observation
                {
                    Sequence = ++observationCounter, By = op.Performer, At = time, Until = time + Scenario.ObservationValiditySeconds,
                    StateSequence = stateSequence[(int)machine.Value]
                };
                break;
            case BaseOperationAction.Intervene:
                spares[op.Spare]--; // consumed exactly once, at completion
                SparesConsumed++;
                stateSequence[(int)machine.Value]++;
                break;
            case BaseOperationAction.PlaceOrder:
                Scenario.TryGetManifest(c.ManifestKey, out ResupplyManifest manifest);
                long activity = OpenActivity(ActivityKind.Resupply, manifest.Key, op.Performer);
                orders.Add(new Order { Sequence = ++orderCounter, Manifest = manifest, PlacedBy = op.Performer, PlacedAt = time,
                    State = OrderState.Pending, Activity = activity });
                if (orders.Count > BaseOperationsLimits.MaxOrders) orders.RemoveAt(0);
                lastOrderAt = time;
                break;
            case BaseOperationAction.UnloadDelivery:
                Order order = orders.Find(o => o.Sequence == c.OrderSequence);
                if (order != null && order.State == OrderState.Arrived)
                {
                    order.State = OrderState.Unloaded;
                    order.ClosedAt = time;
                    CloseActivity(order.Activity, ActivityStatus.Completed, "delivery-unloaded");
                }
                break;
        }
    }

    private bool EndOperationsOf(PlayerId player) => operations.RemoveAll(o => o.Performer == player) > 0; // PerformerLeft

    private void Restart()
    {
        epoch = checked(epoch + 1);
        revision = 0;
        time = 0;
        State = SessionState.Ready;
        operations.Clear();
        orders.Clear();
        activities.Clear();
        decisions.Clear();
        observations[0] = observations[1] = null;
        stateSequence[0] = stateSequence[1] = 0;
        observationCounter = orderCounter = activityCounter = 0;
        lastOrderAt = null;
        ResetSpares();
        Roster.ResetForRestart();
    }

    private void ResetSpares()
    {
        spares.Clear();
        foreach (SpareStock spare in Scenario.Equipment.InitialSpares) spares[spare.Key] = spare.Count;
    }

    private string SpareFor(OperationChoice choice) => choice == OperationChoice.ReplaceProcessorFilter
        ? Scenario.Equipment.WaterProcessor.FilterSpareKey : Scenario.Equipment.Air.InletFilterSpareKey;

    private int FreeSpares(string key)
    {
        spares.TryGetValue(key, out int count);
        foreach (Operation op in operations)
            if (op.Spare == key) count--;
        return count;
    }

    private bool IsCurrent(EquipmentKind machine, Observation o) => o.StateSequence == stateSequence[(int)machine] && time < o.Until;

    private long OpenActivity(ActivityKind kind, string key, PlayerId by)
    {
        long sequence = ++activityCounter;
        activities.Add(new ActivityRecord(sequence, kind, key, ActivityStatus.Active, by, time, null, null));
        if (activities.Count > BaseOperationsLimits.MaxActivities) activities.RemoveAt(0);
        return sequence;
    }

    private void CloseActivity(long sequence, ActivityStatus status, string resultKey)
    {
        int i = activities.FindIndex(a => a.Sequence == sequence);
        if (i < 0) return;
        ActivityRecord a = activities[i];
        activities[i] = new ActivityRecord(a.Sequence, a.Kind, a.Key, status, a.StartedBy, a.StartedAtSeconds, time, resultKey);
    }

    private double CommitAt(Order order) => order.PlacedAt + order.Manifest.CancelWindowSeconds;
    private double ArriveAt(Order order) => order.PlacedAt + order.Manifest.LeadSeconds;

    private bool TryGetMember(PlayerId player, out CrewMemberState member)
    {
        foreach (CrewMemberState candidate in Roster.Members)
            if (candidate.Player == player)
            {
                member = candidate;
                return true;
            }
        member = null;
        return false;
    }

    private static CommandResult Result(Guid id, long epoch, string reason) =>
        new CommandResult(id, epoch, reason == OutpostReasons.Accepted, reason);

    // ---- publication: one full snapshot, phase 2 ⊂ phase 3 ⊂ phase 4 ---------------------------------------------

    private void Publish(bool bump)
    {
        if (bump) revision = checked(revision + 1);
        ExpeditionScenario p3 = Scenario.Expedition;
        ExpeditionSessionSnapshot template = ExpeditionFixtures.Initial(p3, epoch);
        var stations = new List<StationState>();
        foreach (StationPlacement placement in Scenario.Layout.Stations)
            stations.Add(operations.Exists(o => o.Command.Station == placement.Id)
                ? new StationState(placement.Id, StationAvailability.Unavailable, null, null) : StationState.Free(placement.Id));
        OutpostSimulationSnapshot initial = OutpostFixtures.InitialResources(p3.Outpost);
        PowerSnapshot p = initial.Power;
        var resources = new OutpostSimulationSnapshot(new PowerSnapshot(time, p.EnergyJoules, p.CapacityJoules, p.GenerationWatts,
                p.EssentialRequestedWatts, p.EssentialServedWatts, p.ScienceRequestedWatts, p.ScienceServedWatts),
            initial.ServiceLoadRequestedWatts, initial.ServiceLoadServedWatts, initial.LifeSupport);
        var outpost = new OutpostSessionSnapshot(epoch, revision, State, OutpostOutcome.InProgress, Scenario.Id, Roster.Members,
            stations, resources, template.Outpost.Objectives);
        var crew = new List<ExpeditionCrewState>();
        foreach (CrewMemberState member in outpost.Crew)
            crew.Add(new ExpeditionCrewState(member.Player, CrewZone.Interior, 0, -1, false, SuitActivity.Rest,
                ReturnMarginEstimate.NotApplicable(time), ProtectionState.NotRequired));
        var expedition = new ExpeditionSessionSnapshot(outpost, crew, template.Suits, template.Airlock, template.Shelter, template.Supply,
            template.Advisories, template.Receptions, template.Instruction, template.Phase, null, ExpeditionOutcome.InProgress,
            template.Objectives, template.Assessments, template.Incidents, null);

        var equipment = new List<EquipmentStatus>();
        foreach (EquipmentKind machine in new[] { EquipmentKind.WaterProcessor, EquipmentKind.AirRevitalization })
        {
            Observation o = observations[(int)machine];
            EquipmentObservation published = o == null ? null : new EquipmentObservation(o.Sequence, machine, o.By, o.At, o.Until,
                o.StateSequence, Findings.None, Array.Empty<ObservedReading>(), IsCurrent(machine, o));
            equipment.Add(new EquipmentStatus(machine, Scenario.StationOf(machine), EquipmentOperatingState.Online,
                published != null && published.Current ? EquipmentKnowledge.Nominal : EquipmentKnowledge.Unknown, Findings.None,
                stateSequence[(int)machine], published, null));
        }
        var water = new WaterTreatmentStatus(0, InstrumentReading.Unavailable(ReadingUnit.MicrosiemensPerCentimeter),
            Scenario.Equipment.WaterProcessor.ProductSpecLimitMicrosiemens, PotableQuality.Verified, 0, 0);
        var spareStock = new List<SpareStock>();
        foreach (SpareStock initialSpare in Scenario.Equipment.InitialSpares)
            spareStock.Add(new SpareStock(initialSpare.Key, spares[initialSpare.Key], initialSpare.Capacity));
        var progress = new List<OperationProgress>();
        foreach (Operation op in operations)
            progress.Add(new OperationProgress(op.Command.Id, op.Command.Station, op.Command.Action, op.Command.Choice, op.Performer,
                op.Work, op.Duration, false, op.Spare));
        var published2 = new List<ResupplyOrder>();
        foreach (Order order in orders)
        {
            bool closed = order.State == OrderState.Unloaded || order.State == OrderState.Cancelled;
            published2.Add(new ResupplyOrder(order.Sequence, order.Manifest.Key, order.PlacedBy, order.PlacedAt, CommitAt(order),
                ArriveAt(order), order.State, closed ? (IEnumerable<ManifestItem>)Array.Empty<ManifestItem>() : order.Manifest.Items,
                order.ClosedAt));
        }
        var session = new BaseSessionStatus(BaseSessionMode.Continuous, 0, Scenario.Irreversible.EssentialUnservedGraceSeconds,
            IrreversibleCause.None, null, activityCounter, 0);
        snapshot = new BaseOperationsSnapshot(expedition, session, BaseOperationsFixtures.GaugesFrom(Scenario, outpost, 0), equipment,
            water, spareStock, progress, published2, activities, decisions, null);
        SnapshotChanged?.Invoke(snapshot);
    }

    /// <summary>The D2 fixture scenario (<see cref="BaseOperationsFixtures.Scenario"/>) with the four spawn points moved within
    /// reach of the phase 4 stations: slots 0 and 1 at the logistics console, slot 2 at the air unit, slot 3 at the water
    /// processor. Nothing else changes. It keeps the socket tests short; the loopback tests also walk for real.</summary>
    public static BaseOperationsScenario NearStationsScenario()
    {
        BaseOperationsScenario s = BaseOperationsFixtures.Scenario(null);
        ExpeditionScenario e = s.Expedition;
        OutpostScenario o = e.Outpost;
        var spawns = new[] { AtConsoleA, AtConsoleB, AtAir, AtWater };
        var layout = new OutpostLayout(o.Layout.Bounds, o.Layout.Obstacles, o.Layout.Stations, spawns);
        var outpost = new OutpostScenario(o.Id, o.Provenance, layout, o.Movement, o.Tasks, o.Simulation);
        var expedition = new ExpeditionScenario(outpost, e.Zones, e.Airlock, e.Racks, e.Eva, e.TaskRules, e.Shelter, e.Comms, e.Episode,
            e.Rules, e.SuitedSpeedFactor);
        return new BaseOperationsScenario(expedition, s.Equipment, s.WaterStation, s.AirStation, s.Operations, s.ObservationValiditySeconds,
            s.Logistics, s.Manifests, s.Gauges, s.Irreversible, s.ReportIntervalSeconds, s.Science, s.Provenance);
    }

    // In reach of the fixture stations (logistics (5, 6), air (2, 6), water (10, 1.5); reach 1.2 m) and clear of each other.
    public static readonly BasePosition AtConsoleA = new BasePosition(5.6, 5.4);
    public static readonly BasePosition AtConsoleB = new BasePosition(4.3, 6.4);
    public static readonly BasePosition AtAir = new BasePosition(2.9, 5.6);
    public static readonly BasePosition AtWater = new BasePosition(10.0, 2.5);
}

/// <summary>Deep, exact text of a published object graph (public properties, doubles round-trip), so two states can be
/// compared field by field: the cable is binary, so a remote copy must be identical, not approximately equal.</summary>
public static class Phase04NetworkFingerprint
{
    public static string Of(object value)
    {
        var sb = new System.Text.StringBuilder();
        Dump(value, sb, 0);
        return sb.ToString();
    }

    private static void Dump(object value, System.Text.StringBuilder sb, int depth)
    {
        if (depth > 14) { sb.Append('…'); return; }
        if (value == null) { sb.Append("null"); return; }
        Type t = value.GetType();
        if (t.IsPrimitive || t.IsEnum || value is string || value is decimal || value is Guid)
        {
            sb.Append(value is double d ? d.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : value.ToString());
            return;
        }
        if (value is System.Collections.IEnumerable list)
        {
            sb.Append('[');
            foreach (object item in list) { Dump(item, sb, depth + 1); sb.Append(','); }
            sb.Append(']');
            return;
        }
        sb.Append(t.Name).Append('{');
        var properties = new List<System.Reflection.PropertyInfo>(t.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance));
        properties.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        foreach (System.Reflection.PropertyInfo p in properties)
        {
            if (p.GetIndexParameters().Length > 0) continue;
            sb.Append(p.Name).Append('=');
            Dump(p.GetValue(value), sb, depth + 1);
            sb.Append(';');
        }
        sb.Append('}');
    }
}
#endif
