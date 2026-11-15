using System;
using Trainer.Simulation.LifeSupport;

namespace Trainer.Simulation.BaseOperations
{
    /// <summary>
    /// Builders for the phase 2 <see cref="ResourceTransfer"/> half of every phase 4 mass movement (D1, plain C#). The
    /// authority (D2) decides WHEN; these fix WHAT, so every pair balances and crosses the base boundary in the declared
    /// direction. Positive boundary outflow = mass leaves the base stocks (into the processor or out of the model);
    /// negative = boundary inflow (from the processor or from an external delivery).
    ///
    /// Pairing (same id in both models, see SESSION_CONTRACT §3–4):
    /// <list type="bullet">
    /// <item>Batch: <see cref="BatchIntake"/> on the base, then <see cref="EquipmentEffect.ReceiveBatch"/>.</item>
    /// <item>Route: <see cref="EquipmentEffect.ReleaseToPotable"/> / <see cref="EquipmentEffect.ReleaseForReprocess"/>,
    /// then <see cref="ProductToPotable"/> / <see cref="ProductToUntreated"/>. Check the destination room with
    /// <see cref="Fit"/> BEFORE the release: once released, the product only exists in the base transfer.</item>
    /// <item>Recheck: <see cref="RecheckPotable"/> (zero balance, no equipment effect).</item>
    /// <item>Unload: <see cref="DeliveryInflow"/> plus one <see cref="EquipmentEffect.ReceiveSpares"/> per spare line,
    /// ids from <see cref="DeriveId"/>; what does not fit stays in the order (<see cref="Fit"/>, <see cref="SpareRoom"/>).
    /// A delivery is an external, identified, finite source: nothing is created at a station.</item>
    /// </list>
    /// </summary>
    public static class BaseOperationsTransfers
    {
        /// <summary>Untreated water leaves the base stocks into the processor (boundary outflow +kg).</summary>
        public static ResourceTransfer BatchIntake(Guid id, double kilograms) =>
            Single(id, ResourceKind.UntreatedWater, -Positive(kilograms, nameof(kilograms)));

        /// <summary>Released product enters the potable tank (boundary inflow).</summary>
        public static ResourceTransfer ProductToPotable(Guid id, double kilograms) =>
            Single(id, ResourceKind.PotableWater, Positive(kilograms, nameof(kilograms)));

        /// <summary>Released product returns to the untreated stock (boundary inflow).</summary>
        public static ResourceTransfer ProductToUntreated(Guid id, double kilograms) =>
            Single(id, ResourceKind.UntreatedWater, Positive(kilograms, nameof(kilograms)));

        /// <summary>Suspect potable water goes back to untreated (zero balance, nothing crosses the boundary).</summary>
        public static ResourceTransfer RecheckPotable(Guid id, double kilograms)
        {
            double kg = Positive(kilograms, nameof(kilograms));
            return new ResourceTransfer(id, new ResourceEffect(new[]
            {
                new ResourceChange(ResourceKind.PotableWater, -kg),
                new ResourceChange(ResourceKind.UntreatedWater, kg)
            }, 0d));
        }

        /// <summary>External delivery of potable water and food (boundary inflow). Amounts are what fits; zero lines
        /// are omitted. Returns null when nothing is delivered (the authority reports NoStorageCapacity).</summary>
        public static ResourceTransfer DeliveryInflow(Guid id, double potableKilograms, double foodKilograms)
        {
            NonNegative(potableKilograms, nameof(potableKilograms));
            NonNegative(foodKilograms, nameof(foodKilograms));
            if (id == Guid.Empty) throw new ArgumentException("Transfer id must not be empty.", nameof(id));
            if (potableKilograms == 0d && foodKilograms == 0d) return null;
            var changes = new System.Collections.Generic.List<ResourceChange>(2);
            if (potableKilograms > 0d) changes.Add(new ResourceChange(ResourceKind.PotableWater, potableKilograms));
            if (foodKilograms > 0d) changes.Add(new ResourceChange(ResourceKind.StoredFood, foodKilograms));
            return new ResourceTransfer(id, new ResourceEffect(changes, -(potableKilograms + foodKilograms)));
        }

        /// <summary>Part of <paramref name="requestedKilograms"/> that fits: min(requested, capacity − stock), never negative.</summary>
        public static double Fit(double requestedKilograms, double stockKilograms, double capacityKilograms)
        {
            NonNegative(requestedKilograms, nameof(requestedKilograms));
            NonNegative(stockKilograms, nameof(stockKilograms));
            NonNegative(capacityKilograms, nameof(capacityKilograms));
            return Math.Max(0d, Math.Min(requestedKilograms, capacityKilograms - stockKilograms));
        }

        /// <summary>Spares of <paramref name="key"/> that still fit (0 for an unknown key).</summary>
        public static int SpareRoom(BaseEquipmentSnapshot snapshot, string key)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            return snapshot.TryGetSpare(key, out SpareStock spare) ? spare.Capacity - spare.Count : 0;
        }

        /// <summary>
        /// Deterministic, non-empty id for one effect of a command (FNV-1a of <paramref name="purpose"/> mixed into the
        /// command id). The same command and purpose always give the same id, so a replayed command maps onto the
        /// already applied effect instead of delivering twice.
        /// </summary>
        public static Guid DeriveId(Guid commandId, string purpose)
        {
            if (commandId == Guid.Empty) throw new ArgumentException("Command id must not be empty.", nameof(commandId));
            if (string.IsNullOrEmpty(purpose)) throw new ArgumentException("Purpose is required.", nameof(purpose));
            byte[] bytes = commandId.ToByteArray();
            ulong h1 = 14695981039346656037UL, h2 = 1099511628211UL ^ 0x9E3779B97F4A7C15UL;
            foreach (char c in purpose)
            {
                h1 = (h1 ^ c) * 1099511628211UL;
                h2 = (h2 ^ c) * 1099511628211UL + 0x632BE59BD9B4E019UL;
            }
            for (int i = 0; i < 8; i++)
            {
                bytes[i] ^= (byte)(h1 >> (8 * i));
                bytes[8 + i] ^= (byte)(h2 >> (8 * i));
            }
            var id = new Guid(bytes);
            if (id == Guid.Empty || id == commandId) bytes[15] ^= 0x5A;
            return new Guid(bytes);
        }

        private static ResourceTransfer Single(Guid id, ResourceKind kind, double delta) =>
            new ResourceTransfer(id, new ResourceEffect(new[] { new ResourceChange(kind, delta) }, -delta));

        private static double Positive(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0d)
                throw new ArgumentOutOfRangeException(name, value, "Must be finite and positive.");
            return value;
        }

        private static double NonNegative(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0d)
                throw new ArgumentOutOfRangeException(name, value, "Must be finite and nonnegative.");
            return value;
        }
    }
}
