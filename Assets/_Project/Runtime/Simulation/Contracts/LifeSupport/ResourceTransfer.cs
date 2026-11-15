using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Trainer.Simulation.LifeSupport
{
    /// <summary>Signed mass change of one stock in kilograms: negative consumes, positive produces.
    /// Finite and nonzero.</summary>
    public sealed class ResourceChange
    {
        public ResourceKind Kind { get; }
        public double DeltaKilograms { get; }

        public ResourceChange(ResourceKind kind, double deltaKilograms)
        {
            if (!Enum.IsDefined(typeof(ResourceKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            if (double.IsNaN(deltaKilograms) || double.IsInfinity(deltaKilograms) || deltaKilograms == 0d)
                throw new ArgumentOutOfRangeException(nameof(deltaKilograms), deltaKilograms, "Must be finite and nonzero.");
            Kind = kind;
            DeltaKilograms = deltaKilograms;
        }
    }

    /// <summary>
    /// Atomic set of stock changes with an explicit mass balance (configuration, immutable). At most one change
    /// per kind. <see cref="BoundaryOutflowKilograms"/> is the mass that leaves the modelled boundary (positive,
    /// for example process residue) or enters it (negative): the sum of all deltas plus the outflow must be zero.
    /// Mass is never created or lost silently. Contract values are scenario inputs: no recovery percentage is
    /// implied by this type.
    /// </summary>
    public sealed class ResourceEffect
    {
        /// <summary>Absolute tolerance, in kilograms, of the mass balance check.</summary>
        public const double BalanceToleranceKilograms = 1e-9;

        public static readonly ResourceEffect None = new ResourceEffect(Array.Empty<ResourceChange>(), 0d);

        public IReadOnlyList<ResourceChange> Changes { get; }
        public double BoundaryOutflowKilograms { get; }
        public bool IsEmpty => Changes.Count == 0;

        public ResourceEffect(IEnumerable<ResourceChange> changes, double boundaryOutflowKilograms)
        {
            if (changes == null) throw new ArgumentNullException(nameof(changes));
            if (double.IsNaN(boundaryOutflowKilograms) || double.IsInfinity(boundaryOutflowKilograms))
                throw new ArgumentOutOfRangeException(nameof(boundaryOutflowKilograms));
            var copy = new List<ResourceChange>();
            var seen = new HashSet<ResourceKind>();
            double sum = 0d;
            foreach (ResourceChange change in changes)
            {
                if (change == null) throw new ArgumentException("Null change.", nameof(changes));
                if (!seen.Add(change.Kind)) throw new ArgumentException("One change per resource kind.", nameof(changes));
                copy.Add(change);
                sum += change.DeltaKilograms;
            }
            if (Math.Abs(sum + boundaryOutflowKilograms) > BalanceToleranceKilograms)
                throw new ArgumentException("Mass balance: sum of deltas plus boundary outflow must be zero.",
                    nameof(boundaryOutflowKilograms));
            if (copy.Count == 0 && boundaryOutflowKilograms != 0d)
                throw new ArgumentException("An empty effect cannot move mass across the boundary.",
                    nameof(boundaryOutflowKilograms));
            Changes = new ReadOnlyCollection<ResourceChange>(copy.ToArray());
            BoundaryOutflowKilograms = boundaryOutflowKilograms;
        }

        /// <summary>Delta for <paramref name="kind"/>, zero when the effect does not touch it.</summary>
        public double DeltaFor(ResourceKind kind)
        {
            foreach (ResourceChange change in Changes)
                if (change.Kind == kind) return change.DeltaKilograms;
            return 0d;
        }
    }

    /// <summary>
    /// One identified application of a <see cref="ResourceEffect"/>. The id makes the application idempotent:
    /// the authority's <see cref="IOutpostSimulation.Apply"/> applies an id at most once per Reset. The task
    /// authority uses the id of the StartTask command that created the task instance.
    /// </summary>
    public sealed class ResourceTransfer
    {
        public Guid Id { get; }
        public ResourceEffect Effect { get; }

        public ResourceTransfer(Guid id, ResourceEffect effect)
        {
            if (id == Guid.Empty) throw new ArgumentException("Transfer id must not be empty.", nameof(id));
            Id = id;
            Effect = effect ?? throw new ArgumentNullException(nameof(effect));
        }
    }

    /// <summary>Outcome of <see cref="IOutpostSimulation.Apply"/>. Anything other than Applied leaves every
    /// stock unchanged (all or nothing).</summary>
    public enum ResourceTransferStatus
    {
        /// <summary>All changes applied at once.</summary>
        Applied,
        /// <summary>The same id and effect were already applied since the last Reset; nothing changes again.</summary>
        AlreadyApplied,
        /// <summary>The id was already used with a different effect; nothing changes.</summary>
        IdConflict,
        /// <summary>A stock would become negative; nothing changes.</summary>
        InsufficientStock,
        /// <summary>A stock would exceed its capacity; nothing changes.</summary>
        ExceedsCapacity
    }
}
