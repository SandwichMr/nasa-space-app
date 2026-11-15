using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Trainer.Application.Expedition;
using Trainer.Application.Outpost;
using Trainer.Simulation.BaseOperations;

namespace Trainer.Application.BaseOperations
{
    /// <summary>
    /// Complete authoritative state of a phase 4 session (immutable; private read-only copies). It CONTAINS the phase 3
    /// <see cref="ExpeditionSessionSnapshot"/> (which contains the phase 2 one) with the same epoch and revision, so phase 2
    /// and phase 3 views keep working on the same port. Always a FULL state, never a delta. Order: (Epoch, Revision).
    ///
    /// The constructor refuses: a Continuous session in Completed; an end cause that does not match Interrupted; gauges not
    /// one per kind; machines not one per kind; operations by absent members, two operations for one member or one station,
    /// an operation where a phase 2 task is in progress or whose phase 2 station is not Unavailable; operations after the end;
    /// duplicate orders, activities or spares; more than the limits.
    /// </summary>
    public sealed class BaseOperationsSnapshot
    {
        public ExpeditionSessionSnapshot Expedition { get; }
        public BaseSessionStatus Session { get; }
        /// <summary>One per <see cref="GaugeKind"/>, in enum order.</summary>
        public IReadOnlyList<ResourceGauge> Gauges { get; }
        /// <summary>One per <see cref="EquipmentKind"/>, in enum order.</summary>
        public IReadOnlyList<EquipmentStatus> Equipment { get; }
        public WaterTreatmentStatus Water { get; }
        /// <summary>Spare stock. Spares named by <see cref="OperationProgress.ReservedSpare"/> are held by operations in
        /// progress and not consumed yet (see <see cref="FreeSpares"/>).</summary>
        public IReadOnlyList<SpareStock> Spares { get; }
        public IReadOnlyList<OperationProgress> Operations { get; }
        public IReadOnlyList<ResupplyOrder> Orders { get; }
        /// <summary>Most recent activities, oldest first (bounded window).</summary>
        public IReadOnlyList<ActivityRecord> Activities { get; }
        /// <summary>Most recent accepted decisions with explanation, oldest first (bounded window).</summary>
        public IReadOnlyList<DecisionRecord> Decisions { get; }
        public OperationsReport LatestReport { get; }

        public OutpostSessionSnapshot Outpost => Expedition.Outpost;
        public long Epoch => Expedition.Epoch;
        public long Revision => Expedition.Revision;
        public double SimulatedTimeSeconds => Expedition.SimulatedTimeSeconds;

        public BaseOperationsSnapshot(ExpeditionSessionSnapshot expedition, BaseSessionStatus session, IEnumerable<ResourceGauge> gauges,
            IEnumerable<EquipmentStatus> equipment, WaterTreatmentStatus water, IEnumerable<SpareStock> spares,
            IEnumerable<OperationProgress> operations, IEnumerable<ResupplyOrder> orders, IEnumerable<ActivityRecord> activities,
            IEnumerable<DecisionRecord> decisions, OperationsReport latestReport)
        {
            Expedition = expedition ?? throw new ArgumentNullException(nameof(expedition));
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Water = water ?? throw new ArgumentNullException(nameof(water));
            LatestReport = latestReport;
            OutpostSessionSnapshot outpost = expedition.Outpost;
            bool interrupted = outpost.State == SessionState.Interrupted;
            bool ended = interrupted || outpost.State == SessionState.Completed;
            if (session.Mode == BaseSessionMode.Continuous && outpost.State == SessionState.Completed)
                throw new ArgumentException("A continuous session never completes; activities end, the session does not.", nameof(session));
            if (session.Mode == BaseSessionMode.Continuous && (session.EndCause != IrreversibleCause.None) != interrupted)
                throw new ArgumentException("A continuous session is Interrupted exactly with an irreversible cause.", nameof(session));
            if (session.Mode == BaseSessionMode.Training && session.EndCause != IrreversibleCause.None)
                throw new ArgumentException("Training ends through the phase 3 outcome, not an irreversible cause.", nameof(session));

            var gaugeCopy = Copy(gauges, nameof(gauges), BaseOperationsLimits.MaxGauges);
            if (gaugeCopy.Count != Enum.GetValues(typeof(GaugeKind)).Length) throw new ArgumentException("One gauge per kind.", nameof(gauges));
            for (int i = 0; i < gaugeCopy.Count; i++)
                if ((int)gaugeCopy[i].Kind != i) throw new ArgumentException("Gauges in kind order.", nameof(gauges));
            Gauges = gaugeCopy;

            var equipmentCopy = Copy(equipment, nameof(equipment), Enum.GetValues(typeof(EquipmentKind)).Length);
            if (equipmentCopy.Count != Enum.GetValues(typeof(EquipmentKind)).Length) throw new ArgumentException("One status per machine.", nameof(equipment));
            for (int i = 0; i < equipmentCopy.Count; i++)
            {
                if ((int)equipmentCopy[i].Equipment != i) throw new ArgumentException("Machines in kind order.", nameof(equipment));
                if (!outpost.TryGetStation(equipmentCopy[i].Station, out _))
                    throw new ArgumentException($"Station '{equipmentCopy[i].Station}' is not in the snapshot.", nameof(equipment));
            }
            if (equipmentCopy[0].Station == equipmentCopy[1].Station) throw new ArgumentException("Machines at distinct stations.", nameof(equipment));
            Equipment = equipmentCopy;

            var spareCopy = Copy(spares, nameof(spares), BaseEquipmentLimits.MaxSpareKinds);
            var spareKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (SpareStock spare in spareCopy)
                if (!spareKeys.Add(spare.Key)) throw new ArgumentException($"Duplicate spare '{spare.Key}'.", nameof(spares));
            Spares = spareCopy;

            var operationCopy = Copy(operations, nameof(operations), BaseOperationsLimits.MaxOperations);
            if (ended && operationCopy.Count > 0) throw new ArgumentException("No operation in progress after the session ended.", nameof(operations));
            var performers = new HashSet<PlayerId>();
            var stations = new HashSet<StationId>();
            var reserved = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (OperationProgress op in operationCopy)
            {
                if (!outpost.TryGetCrew(op.Performer, out _)) throw new ArgumentException($"Performer {op.Performer} is not present.", nameof(operations));
                if (!performers.Add(op.Performer)) throw new ArgumentException($"{op.Performer} has two operations.", nameof(operations));
                if (!stations.Add(op.Station)) throw new ArgumentException($"Two operations at '{op.Station}'.", nameof(operations));
                if (!outpost.TryGetStation(op.Station, out StationState station))
                    throw new ArgumentException($"Station '{op.Station}' is not in the snapshot.", nameof(operations));
                if (station.Availability != StationAvailability.Unavailable)
                    throw new ArgumentException("A station with an operation is published Unavailable in phase 2.", nameof(operations));
                foreach (StationState other in outpost.Stations)
                    if (other.Occupant == op.Performer) throw new ArgumentException($"{op.Performer} also has a phase 2 task.", nameof(operations));
                if (op.ReservedSpare != null)
                {
                    if (!spareKeys.Contains(op.ReservedSpare)) throw new ArgumentException($"Unknown reserved spare '{op.ReservedSpare}'.", nameof(operations));
                    reserved.TryGetValue(op.ReservedSpare, out int count);
                    reserved[op.ReservedSpare] = count + 1;
                }
            }
            foreach (SpareStock spare in spareCopy)
                if (reserved.TryGetValue(spare.Key, out int count) && count > spare.Count)
                    throw new ArgumentException($"More reservations than spares of '{spare.Key}'.", nameof(operations));
            Operations = operationCopy;

            var orderCopy = Copy(orders, nameof(orders), BaseOperationsLimits.MaxOrders);
            var orderSequences = new HashSet<long>();
            foreach (ResupplyOrder order in orderCopy)
            {
                if (!orderSequences.Add(order.Sequence)) throw new ArgumentException($"Duplicate order {order.Sequence}.", nameof(orders));
                if (order.PlacedAtSeconds > SimulatedTimeSeconds + ExpeditionSessionSnapshot.TimeToleranceSeconds)
                    throw new ArgumentException("An order cannot be placed in the future.", nameof(orders));
            }
            Orders = orderCopy;

            var activityCopy = Copy(activities, nameof(activities), BaseOperationsLimits.MaxActivities);
            for (int i = 1; i < activityCopy.Count; i++)
                if (activityCopy[i].Sequence <= activityCopy[i - 1].Sequence) throw new ArgumentException("Activities in increasing sequence.", nameof(activities));
            foreach (ActivityRecord activity in activityCopy)
                if (activity.Sequence > session.ActivitySequence) throw new ArgumentException("Activity beyond the session sequence.", nameof(activities));
            Activities = activityCopy;

            Decisions = Copy(decisions, nameof(decisions), BaseOperationsLimits.MaxDecisions);
            if (latestReport != null && latestReport.ToSeconds > SimulatedTimeSeconds + ExpeditionSessionSnapshot.TimeToleranceSeconds)
                throw new ArgumentException("A report cannot cover the future.", nameof(latestReport));
        }

        public bool IsNewerThan(BaseOperationsSnapshot other) => other == null || Expedition.IsNewerThan(other.Expedition);

        public ResourceGauge GaugeOf(GaugeKind kind) => Gauges[(int)kind];
        public EquipmentStatus EquipmentOf(EquipmentKind kind) => Equipment[(int)kind];

        /// <summary>Spares of <paramref name="key"/> not reserved by an operation in progress (0 when unknown).</summary>
        public int FreeSpares(string key)
        {
            int total = 0;
            foreach (SpareStock spare in Spares)
                if (spare.Key == key) total = spare.Count;
            foreach (OperationProgress op in Operations)
                if (op.ReservedSpare == key) total--;
            return total;
        }

        public bool TryGetOperation(PlayerId performer, out OperationProgress operation)
        {
            foreach (OperationProgress candidate in Operations)
                if (candidate.Performer == performer)
                {
                    operation = candidate;
                    return true;
                }
            operation = null;
            return false;
        }

        public bool TryGetOperationAt(StationId station, out OperationProgress operation)
        {
            foreach (OperationProgress candidate in Operations)
                if (candidate.Station == station)
                {
                    operation = candidate;
                    return true;
                }
            operation = null;
            return false;
        }

        public bool TryGetOrder(long sequence, out ResupplyOrder order)
        {
            foreach (ResupplyOrder candidate in Orders)
                if (candidate.Sequence == sequence)
                {
                    order = candidate;
                    return true;
                }
            order = null;
            return false;
        }

        private static IReadOnlyList<T> Copy<T>(IEnumerable<T> items, string name, int max) where T : class
        {
            if (items == null) throw new ArgumentNullException(name);
            var copy = new List<T>();
            foreach (T item in items)
            {
                if (item == null) throw new ArgumentException("Null entry.", name);
                copy.Add(item);
            }
            if (copy.Count > max) throw new ArgumentException($"More than {max} entries.", name);
            return new ReadOnlyCollection<T>(copy.ToArray());
        }
    }
}
