using System;
using System.Collections.Generic;
using System.Linq;
using Trainer.Application;
using Trainer.Application.BaseOperations;
using Trainer.Simulation.BaseOperations;
using Trainer.Simulation.LifeSupport;

namespace Trainer.Tooling.Validation.Phase04
{
    /// <summary>
    /// D5 continuity invariants over the stream of FULL phase 4 snapshots one participant receives (from the authority, a local
    /// port or a network client). It needs no implementation detail, so the same validator runs on fixtures, in memory and on
    /// recorded build runs. Invariants (each reported once with its first violation):
    /// <list type="number">
    /// <item>One clock: within an epoch the revision increases and simulated time never goes back.</item>
    /// <item>Continuity: a continuous session is never Completed and never goes back to Ready inside an epoch; finishing an
    /// activity never ends the session.</item>
    /// <item>No hidden recovery: inside an epoch, potable water rises only when the hold is released or an order is unloaded,
    /// food and spares only when an order is unloaded; batch, activity and observation counters never go back.</item>
    /// <item>Pause: two consecutive Paused states have the same clock and stocks.</item>
    /// <item>Restart: a new epoch starts Ready with the scenario's stocks, spares, machines and no orders or activities.</item>
    /// <item>Readings: unknown (Unavailable) is never a value or a band; a reading's time is never in the future.</item>
    /// <item>Orders only move forward (Pending → InTransit → Arrived → Unloaded, or Pending → Cancelled).</item>
    /// </list>
    /// </summary>
    public sealed class BaseOperationsStreamValidator
    {
        private const double Tolerance = 1e-6;
        private static readonly string[] Names =
        {
            "one clock: revision increases and simulated time never goes back inside an epoch",
            "continuity: never Completed, never back to Ready, activities end without ending the session",
            "no hidden recovery: potable, food and spares rise only through routing or unloading; counters never go back",
            "pause: no clock and no consumption while Paused",
            "restart: new epoch starts from the scenario (stocks, spares, machines, no orders)",
            "readings: unknown is never a value or a band; no reading from the future",
            "orders only move forward"
        };

        private readonly BaseOperationsScenario scenario;
        private readonly string kind;
        private readonly string[] firstViolation = new string[Names.Length];
        private readonly int[] violations = new int[Names.Length];
        private BaseOperationsSnapshot previous;

        public int Observed { get; private set; }
        public int Epochs { get; private set; }
        public double LongestEpochSimulatedSeconds { get; private set; }

        public BaseOperationsStreamValidator(BaseOperationsScenario scenario, string kind)
        {
            this.scenario = scenario ?? throw new ArgumentNullException(nameof(scenario));
            this.kind = kind ?? throw new ArgumentNullException(nameof(kind));
        }

        public void Observe(BaseOperationsSnapshot s)
        {
            if (s == null) return;
            if (previous != null && s.Epoch == previous.Epoch && s.Revision == previous.Revision) return; // same state republished
            Observed++;
            CheckReadings(s);
            if (s.Session.Mode == BaseSessionMode.Continuous && s.Outpost.State == SessionState.Completed)
                Violate(1, $"rev {s.Revision}: continuous session Completed");
            if (previous == null)
            {
                Epochs = 1;
                previous = s;
                return;
            }
            if (s.Epoch < previous.Epoch) Violate(0, $"epoch went back {previous.Epoch} -> {s.Epoch}");
            else if (s.Epoch > previous.Epoch)
            {
                Epochs++;
                CheckRestart(s);
            }
            else SameEpoch(previous, s);
            if (s.Epoch == previous.Epoch) LongestEpochSimulatedSeconds = Math.Max(LongestEpochSimulatedSeconds, s.SimulatedTimeSeconds);
            previous = s;
        }

        public List<Phase04Check> Results(string label)
        {
            string p = string.IsNullOrEmpty(label) ? "" : label + ": ";
            var list = new List<Phase04Check>();
            if (Observed == 0)
            {
                list.Add(Phase04Checks.Pending(p + "snapshot stream", kind, "no snapshot observed"));
                return list;
            }
            for (int i = 0; i < Names.Length; i++)
                list.Add(Phase04Checks.Result(p + Names[i], kind, violations[i] == 0,
                    violations[i] == 0 ? $"{Observed} snapshots, {Epochs} epoch(s), up to {LongestEpochSimulatedSeconds:0.0} s simulated"
                        : $"{violations[i]} violation(s); first: {firstViolation[i]}"));
            return list;
        }

        public bool HasViolations => violations.Any(v => v > 0);

        private void Violate(int invariant, string detail)
        {
            if (violations[invariant]++ == 0) firstViolation[invariant] = detail;
        }

        private void SameEpoch(BaseOperationsSnapshot a, BaseOperationsSnapshot b)
        {
            string at = $"rev {a.Revision}->{b.Revision} t={a.SimulatedTimeSeconds:0.###}->{b.SimulatedTimeSeconds:0.###}s";
            if (b.Revision <= a.Revision) Violate(0, at + ": revision did not increase");
            if (b.SimulatedTimeSeconds + Tolerance < a.SimulatedTimeSeconds) Violate(0, at + ": clock went back");

            if (a.Outpost.State != SessionState.Ready && b.Outpost.State == SessionState.Ready) Violate(1, at + ": back to Ready without a new epoch");
            foreach (ActivityRecord ended in b.Activities.Where(x => x.Status != ActivityStatus.Active))
            {
                ActivityRecord before = a.Activities.FirstOrDefault(x => x.Sequence == ended.Sequence);
                bool justEnded = before == null || before.Status == ActivityStatus.Active;
                if (justEnded && b.Session.Mode == BaseSessionMode.Continuous && b.Outpost.State != SessionState.Running
                    && b.Outpost.State != SessionState.Paused && b.Session.EndCause == IrreversibleCause.None)
                    Violate(1, at + $": activity {ended.Kind} '{ended.Key}' ended and the session is {b.Outpost.State}");
            }

            bool delivered = Delivered(a, b, out bool deliveredFood, out bool deliveredWater, out HashSet<string> deliveredSpares);
            bool released = b.Water.HoldKilograms + Tolerance < a.Water.HoldKilograms;
            double potableUp = Potable(b) - Potable(a);
            if (potableUp > Tolerance && !released && !deliveredWater)
                Violate(2, at + $": potable +{potableUp:0.######} kg without routing or unloading");
            double foodUp = Food(b) - Food(a);
            if (foodUp > Tolerance && !deliveredFood) Violate(2, at + $": food +{foodUp:0.######} kg without unloading");
            foreach (SpareStock spare in b.Spares)
            {
                SpareStock old = a.Spares.FirstOrDefault(x => x.Key == spare.Key);
                if (old != null && spare.Count > old.Count && !deliveredSpares.Contains(spare.Key))
                    Violate(2, at + $": spare '{spare.Key}' {old.Count}->{spare.Count} without unloading");
            }
            if (b.Water.BatchesProcessed < a.Water.BatchesProcessed) Violate(2, at + ": batch counter went back");
            if (b.Session.ActivitySequence < a.Session.ActivitySequence) Violate(2, at + ": activity sequence went back");
            if (b.Session.ExpeditionsFinished < a.Session.ExpeditionsFinished) Violate(2, at + ": expeditions counter went back");
            for (int i = 0; i < b.Equipment.Count; i++)
            {
                EquipmentStatus x = a.Equipment[i], y = b.Equipment[i];
                if (y.StateSequence < x.StateSequence) Violate(2, at + $": {y.Equipment} state sequence went back");
                if (x.LatestObservation != null && y.LatestObservation != null && y.LatestObservation.Sequence < x.LatestObservation.Sequence)
                    Violate(2, at + $": {y.Equipment} observation sequence went back");
            }

            if (a.Outpost.State == SessionState.Paused && b.Outpost.State == SessionState.Paused)
            {
                if (Math.Abs(b.SimulatedTimeSeconds - a.SimulatedTimeSeconds) > Tolerance) Violate(3, at + ": clock advanced while Paused");
                if (Math.Abs(Potable(b) - Potable(a)) > Tolerance || Math.Abs(Food(b) - Food(a)) > Tolerance
                    || Math.Abs(Energy(b) - Energy(a)) > Tolerance)
                    Violate(3, at + ": stocks changed while Paused");
            }

            foreach (ResupplyOrder order in b.Orders)
                if (a.TryGetOrder(order.Sequence, out ResupplyOrder old) && Rank(order.State) < Rank(old.State))
                    Violate(6, at + $": order {order.Sequence} {old.State} -> {order.State}");
                else if (old != null && old.State == OrderState.Cancelled && order.State != OrderState.Cancelled)
                    Violate(6, at + $": cancelled order {order.Sequence} reopened");
                else if (old != null && order.State == OrderState.Cancelled && old.State != OrderState.Pending && old.State != OrderState.Cancelled)
                    Violate(6, at + $": order {order.Sequence} cancelled after {old.State}");
        }

        private void CheckRestart(BaseOperationsSnapshot s)
        {
            string at = $"epoch {s.Epoch} rev {s.Revision}";
            var problems = new List<string>();
            if (s.Outpost.State != SessionState.Ready && s.Outpost.State != SessionState.Running) problems.Add("state " + s.Outpost.State);
            WaterStock initial = scenario.Expedition.Outpost.Simulation.LifeSupport.InitialWater;
            // Only the first snapshot of the epoch can be compared exactly; later ones may already have consumed.
            if (s.Outpost.State == SessionState.Ready)
            {
                if (Math.Abs(Potable(s) - initial.PotableKilograms) > Tolerance) problems.Add("potable not reset");
                if (Math.Abs(Food(s) - scenario.Expedition.Outpost.Simulation.LifeSupport.InitialFood.StoredKilograms) > Tolerance) problems.Add("food not reset");
                if (s.SimulatedTimeSeconds > Tolerance) problems.Add($"clock at {s.SimulatedTimeSeconds:0.###} s");
            }
            if (s.Orders.Count > 0) problems.Add(s.Orders.Count + " order(s) kept");
            if (s.Water.HoldKilograms > Tolerance) problems.Add("hold not empty");
            foreach (SpareStock spare in scenario.Equipment.InitialSpares)
            {
                SpareStock now = s.Spares.FirstOrDefault(x => x.Key == spare.Key);
                if (now == null || now.Count != spare.Count) problems.Add("spare " + spare.Key + " not reset");
            }
            foreach (EquipmentStatus e in s.Equipment)
                if (e.Repair != null || e.Operating != EquipmentOperatingState.Online) problems.Add(e.Equipment + " not back online");
            if (problems.Count > 0) Violate(4, at + ": " + string.Join(", ", problems));
        }

        private void CheckReadings(BaseOperationsSnapshot s)
        {
            foreach (ResourceGauge g in s.Gauges)
            {
                InstrumentReading r = g.Reading;
                if (r.Status == ReadingStatus.Unavailable && (r.Value.HasValue || g.Band != GaugeBand.Unknown))
                    Violate(5, $"rev {s.Revision}: {g.Kind} unavailable but shown as {r.Value?.ToString() ?? "-"} / {g.Band}");
                if (r.ObservedAtSeconds.HasValue && r.ObservedAtSeconds.Value > s.SimulatedTimeSeconds + Tolerance)
                    Violate(5, $"rev {s.Revision}: {g.Kind} reading from the future");
            }
            InstrumentReading c = s.Water.ProductConductivity;
            if (c.ObservedAtSeconds.HasValue && c.ObservedAtSeconds.Value > s.SimulatedTimeSeconds + Tolerance)
                Violate(5, $"rev {s.Revision}: product conductivity from the future");
            if (c.Status != ReadingStatus.Valid && s.Water.ProductWithinLimit)
                Violate(5, $"rev {s.Revision}: non-valid conductivity treated as within the limit");
        }

        /// <summary>True when an order was (partly) unloaded between the two states, with what it brought.</summary>
        private static bool Delivered(BaseOperationsSnapshot a, BaseOperationsSnapshot b, out bool food, out bool water, out HashSet<string> spares)
        {
            food = false;
            water = false;
            spares = new HashSet<string>(StringComparer.Ordinal);
            bool any = false;
            foreach (ResupplyOrder order in b.Orders)
            {
                if (!a.TryGetOrder(order.Sequence, out ResupplyOrder old)) continue;
                if (old.State != OrderState.Arrived) continue;
                if (order.State != OrderState.Unloaded && order.Remaining.Count >= old.Remaining.Count
                    && SameRemaining(old, order)) continue;
                any = true;
                foreach (ManifestItem item in old.Remaining)
                {
                    if (item.IsSpare) spares.Add(item.SpareKey);
                    else if (item.Resource == ResourceKind.StoredFood) food = true;
                    else water = true;
                }
            }
            return any;
        }

        private static bool SameRemaining(ResupplyOrder a, ResupplyOrder b)
        {
            if (a.Remaining.Count != b.Remaining.Count) return false;
            for (int i = 0; i < a.Remaining.Count; i++)
                if (a.Remaining[i].Kilograms != b.Remaining[i].Kilograms || a.Remaining[i].Count != b.Remaining[i].Count) return false;
            return true;
        }

        private static int Rank(OrderState s)
        {
            switch (s)
            {
                case OrderState.Pending: return 0;
                case OrderState.InTransit: return 1;
                case OrderState.Arrived: return 2;
                case OrderState.Cancelled: return 1;
                default: return 3;
            }
        }

        private static double Potable(BaseOperationsSnapshot s) => s.Outpost.Resources.LifeSupport.Water.PotableKilograms;
        private static double Food(BaseOperationsSnapshot s) => s.Outpost.Resources.LifeSupport.Food.StoredKilograms;
        private static double Energy(BaseOperationsSnapshot s) => s.Outpost.Resources.Power.EnergyJoules;
    }
}
