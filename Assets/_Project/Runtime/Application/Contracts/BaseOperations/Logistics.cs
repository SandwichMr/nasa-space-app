using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Trainer.Application.Outpost;
using Trainer.Simulation.LifeSupport;

namespace Trainer.Application.BaseOperations
{
    /// <summary>One line of a resupply manifest (immutable): a mass of a phase 2 stock (kg) OR a count of a spare.</summary>
    public sealed class ManifestItem
    {
        public ResourceKind? Resource { get; }
        public double Kilograms { get; }
        public string SpareKey { get; }
        public int Count { get; }
        public bool IsSpare => SpareKey != null;

        private ManifestItem(ResourceKind? resource, double kilograms, string spareKey, int count)
        {
            Resource = resource;
            Kilograms = kilograms;
            SpareKey = spareKey;
            Count = count;
        }

        /// <summary>Mass of potable water or stored food (untreated water is never delivered).</summary>
        public static ManifestItem Mass(ResourceKind resource, double kilograms)
        {
            OutpostGuard.Defined(resource, nameof(resource));
            if (resource == ResourceKind.UntreatedWater) throw new ArgumentException("Untreated water is not a delivery.", nameof(resource));
            return new ManifestItem(resource, OutpostGuard.Positive(kilograms, nameof(kilograms)), null, 0);
        }

        public static ManifestItem Spares(string spareKey, int count)
        {
            StableKey.Require(spareKey, nameof(spareKey));
            if (count < 1 || count > Trainer.Simulation.BaseOperations.BaseEquipmentLimits.MaxSpareCount)
                throw new ArgumentOutOfRangeException(nameof(count));
            return new ManifestItem(null, 0, spareKey, count);
        }

        internal string Identity => IsSpare ? "spare:" + SpareKey : "mass:" + Resource;
    }

    internal static class ManifestItems
    {
        public static IReadOnlyList<ManifestItem> Copy(IEnumerable<ManifestItem> items, string name, bool allowEmpty)
        {
            if (items == null) throw new ArgumentNullException(name);
            var copy = new List<ManifestItem>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (ManifestItem item in items)
            {
                if (item == null) throw new ArgumentException("Null item.", name);
                if (!seen.Add(item.Identity)) throw new ArgumentException("One line per resource or spare.", name);
                copy.Add(item);
            }
            if ((!allowEmpty && copy.Count == 0) || copy.Count > BaseOperationsLimits.MaxManifestItems)
                throw new ArgumentException("Between 1 and MaxManifestItems lines.", name);
            return new ReadOnlyCollection<ManifestItem>(copy.ToArray());
        }
    }

    /// <summary>
    /// What one order brings and when (configuration, immutable, SYNTHETIC). Cost = lead time, a limited number of orders in
    /// flight, a minimum interval between orders and crew time to unload. Food and water enter the base only through an
    /// unloaded delivery; nothing appears by pressing a button.
    /// </summary>
    public sealed class ResupplyManifest
    {
        public string Key { get; }
        public IReadOnlyList<ManifestItem> Items { get; }
        /// <summary>Simulated seconds from placing the order to its arrival at the logistics station.</summary>
        public double LeadSeconds { get; }
        /// <summary>Seconds after placing during which it can still be cancelled at no cost (then it is in transit).</summary>
        public double CancelWindowSeconds { get; }

        public ResupplyManifest(string key, IEnumerable<ManifestItem> items, double leadSeconds, double cancelWindowSeconds)
        {
            Key = StableKey.Require(key, nameof(key));
            Items = ManifestItems.Copy(items, nameof(items), false);
            LeadSeconds = OutpostGuard.Positive(leadSeconds, nameof(leadSeconds));
            CancelWindowSeconds = OutpostGuard.NonNegative(cancelWindowSeconds, nameof(cancelWindowSeconds));
            if (cancelWindowSeconds >= leadSeconds) throw new ArgumentException("The cancel window closes before arrival.", nameof(cancelWindowSeconds));
        }
    }

    /// <summary>Logistics limits of the scenario (configuration, immutable).</summary>
    public sealed class LogisticsRules
    {
        /// <summary>Station where orders are placed, cancelled and deliveries unloaded.</summary>
        public StationId Console { get; }
        public int MaxOrdersInFlight { get; }
        public double MinSecondsBetweenOrders { get; }

        public LogisticsRules(StationId console, int maxOrdersInFlight, double minSecondsBetweenOrders)
        {
            if (!console.IsValid) throw new ArgumentException("Console station required.", nameof(console));
            Console = console;
            if (maxOrdersInFlight < 1 || maxOrdersInFlight > BaseOperationsLimits.MaxOrders) throw new ArgumentOutOfRangeException(nameof(maxOrdersInFlight));
            MaxOrdersInFlight = maxOrdersInFlight;
            MinSecondsBetweenOrders = OutpostGuard.NonNegative(minSecondsBetweenOrders, nameof(minSecondsBetweenOrders));
        }
    }

    /// <summary>Order life cycle: Pending (cancellable) → InTransit → Arrived (in the logistics station, waiting to be unloaded)
    /// → Unloaded; or Pending → Cancelled. An arrived order can be unloaded in several steps if storage is full.</summary>
    public enum OrderState { Pending, InTransit, Arrived, Unloaded, Cancelled }

    /// <summary>One resupply order as published (immutable). <see cref="Remaining"/> is what has not entered the stocks yet.</summary>
    public sealed class ResupplyOrder
    {
        public long Sequence { get; }
        public string ManifestKey { get; }
        public PlayerId PlacedBy { get; }
        public double PlacedAtSeconds { get; }
        public double CommitAtSeconds { get; }
        public double ArriveAtSeconds { get; }
        public OrderState State { get; }
        public IReadOnlyList<ManifestItem> Remaining { get; }
        public double? ClosedAtSeconds { get; }

        public ResupplyOrder(long sequence, string manifestKey, PlayerId placedBy, double placedAtSeconds, double commitAtSeconds,
            double arriveAtSeconds, OrderState state, IEnumerable<ManifestItem> remaining, double? closedAtSeconds)
        {
            if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
            Sequence = sequence;
            ManifestKey = StableKey.Require(manifestKey, nameof(manifestKey));
            PlacedBy = placedBy;
            PlacedAtSeconds = OutpostGuard.NonNegative(placedAtSeconds, nameof(placedAtSeconds));
            CommitAtSeconds = OutpostGuard.NonNegative(commitAtSeconds, nameof(commitAtSeconds));
            ArriveAtSeconds = OutpostGuard.NonNegative(arriveAtSeconds, nameof(arriveAtSeconds));
            if (commitAtSeconds < placedAtSeconds || arriveAtSeconds <= commitAtSeconds)
                throw new ArgumentException("placed <= commit < arrival.", nameof(arriveAtSeconds));
            State = OutpostGuard.Defined(state, nameof(state));
            Remaining = ManifestItems.Copy(remaining, nameof(remaining), true);
            if (state == OrderState.Unloaded && Remaining.Count > 0) throw new ArgumentException("Unloaded means nothing remains.", nameof(remaining));
            if (state != OrderState.Unloaded && state != OrderState.Cancelled && Remaining.Count == 0)
                throw new ArgumentException("An open order still carries items.", nameof(remaining));
            bool closed = state == OrderState.Unloaded || state == OrderState.Cancelled;
            if (closedAtSeconds.HasValue != closed) throw new ArgumentException("Closed time exactly for unloaded or cancelled orders.", nameof(closedAtSeconds));
            if (closedAtSeconds.HasValue) OutpostGuard.NonNegative(closedAtSeconds.Value, nameof(closedAtSeconds));
            if (state == OrderState.Cancelled && closedAtSeconds.Value > commitAtSeconds + 1e-9)
                throw new ArgumentException("Only a pending order can be cancelled.", nameof(state));
            ClosedAtSeconds = closedAtSeconds;
        }

        public bool InFlight => State == OrderState.Pending || State == OrderState.InTransit || State == OrderState.Arrived;
    }
}
