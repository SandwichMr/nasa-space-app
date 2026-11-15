using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Trainer.Simulation.Eva
{
    /// <summary>
    /// Conservative return plan of ONE sealed crew member, built by the session authority from confirmed state
    /// (immutable). Seconds are simulated seconds. Walking = path to the airlock entry at the suited speed; waiting =
    /// time until the airlock can open its outer hatch for the member; ingress = the repressurization during which
    /// the member is still on suit supply. Not reachable = the airlock cannot complete an ingress (for example no
    /// make-up gas): the margin is then not valid.
    /// </summary>
    public sealed class ReturnPlan
    {
        public double PathMeters { get; }
        public double WalkSeconds { get; }
        public double AirlockWaitSeconds { get; }
        public double IngressSeconds { get; }
        public bool Reachable { get; }
        public double TotalSeconds => WalkSeconds + AirlockWaitSeconds + IngressSeconds;

        public ReturnPlan(double pathMeters, double walkSeconds, double airlockWaitSeconds, double ingressSeconds, bool reachable)
        {
            PathMeters = EvaGuard.NonNegative(pathMeters, nameof(pathMeters));
            WalkSeconds = EvaGuard.NonNegative(walkSeconds, nameof(walkSeconds));
            AirlockWaitSeconds = EvaGuard.NonNegative(airlockWaitSeconds, nameof(airlockWaitSeconds));
            IngressSeconds = EvaGuard.NonNegative(ingressSeconds, nameof(ingressSeconds));
            Reachable = reachable;
        }
    }

    public enum MarginStatus
    {
        /// <summary>The member is not on suit supply (inside the pressurized base): no return to plan.</summary>
        NotApplicable,
        /// <summary>Every service can cover the return and keep its reserve (margin ≥ 0, inclusive).</summary>
        Sufficient,
        /// <summary>At least one service cannot cover the return without using its reserve (margin &lt; 0).</summary>
        Insufficient,
        /// <summary>The plan is not reachable (airlock cannot complete an ingress); no margin is claimed.</summary>
        ReturnUnavailable
    }

    /// <summary>Margin of one service (immutable, service unit: kg or J; seconds).</summary>
    public sealed class ServiceMargin
    {
        public SuitService Service { get; }
        /// <summary>Level minus reserve; negative when already inside the reserve.</summary>
        public double Usable { get; }
        /// <summary>Consumption of the return plan (walk rate while walking, rest rate while waiting and in ingress).</summary>
        public double RequiredForReturn { get; }
        /// <summary>Consumption per second of what the member does now (activity rate, plus tool watts on the battery).</summary>
        public double CurrentRate { get; }
        /// <summary>(Usable − RequiredForReturn) / CurrentRate: seconds the member may keep doing what they do and
        /// still return with the reserve intact. Negative = already short.</summary>
        public double MarginSeconds { get; }

        public ServiceMargin(SuitService service, double usable, double requiredForReturn, double currentRate, double marginSeconds)
        {
            Service = EvaGuard.Defined(service, nameof(service));
            Usable = EvaGuard.Finite(usable, nameof(usable));
            RequiredForReturn = EvaGuard.NonNegative(requiredForReturn, nameof(requiredForReturn));
            CurrentRate = EvaGuard.Positive(currentRate, nameof(currentRate));
            MarginSeconds = EvaGuard.Finite(marginSeconds, nameof(marginSeconds));
        }
    }

    /// <summary>
    /// Return margin of one member at <see cref="ComputedAtSeconds"/> (immutable). The member's margin is the
    /// SMALLEST service margin (<see cref="LimitingService"/>), never an average: the first service that runs short
    /// limits the return. Valid only for the confirmed state at that time (position, levels, airlock state); a
    /// client must not extrapolate it. The team value is the worst member margin, computed by the authority.
    /// </summary>
    public sealed class ReturnMarginEstimate
    {
        public MarginStatus Status { get; }
        public double ComputedAtSeconds { get; }
        /// <summary>Limiting margin in seconds; 0 for NotApplicable.</summary>
        public double MarginSeconds { get; }
        /// <summary>The service with the smallest margin (declared order breaks ties). Meaningless for NotApplicable.</summary>
        public SuitService LimitingService { get; }
        /// <summary>One entry per service in enum order; empty for NotApplicable.</summary>
        public IReadOnlyList<ServiceMargin> Services { get; }
        /// <summary>The plan used; null for NotApplicable.</summary>
        public ReturnPlan Plan { get; }

        public ReturnMarginEstimate(MarginStatus status, double computedAtSeconds, double marginSeconds,
            SuitService limitingService, IEnumerable<ServiceMargin> services, ReturnPlan plan)
        {
            Status = EvaGuard.Defined(status, nameof(status));
            ComputedAtSeconds = EvaGuard.NonNegative(computedAtSeconds, nameof(computedAtSeconds));
            MarginSeconds = EvaGuard.Finite(marginSeconds, nameof(marginSeconds));
            LimitingService = EvaGuard.Defined(limitingService, nameof(limitingService));
            if (services == null) throw new ArgumentNullException(nameof(services));
            var copy = new List<ServiceMargin>();
            foreach (ServiceMargin service in services)
            {
                if (service == null) throw new ArgumentException("Null service margin.", nameof(services));
                if ((int)service.Service != copy.Count) throw new ArgumentException("One margin per service, in enum order.", nameof(services));
                copy.Add(service);
            }
            if (status == MarginStatus.NotApplicable)
            {
                if (copy.Count != 0 || plan != null || marginSeconds != 0d)
                    throw new ArgumentException("A not-applicable margin carries no services, plan or seconds.");
            }
            else
            {
                if (copy.Count != EvaGuard.ServiceCount) throw new ArgumentException("Every service needs a margin.", nameof(services));
                if (plan == null) throw new ArgumentNullException(nameof(plan));
                double min = double.PositiveInfinity;
                SuitService limiting = SuitService.Oxygen;
                foreach (ServiceMargin service in copy)
                    if (service.MarginSeconds < min) { min = service.MarginSeconds; limiting = service.Service; }
                if (!min.Equals(marginSeconds) || limiting != limitingService)
                    throw new ArgumentException("The member margin must be the smallest service margin.", nameof(marginSeconds));
                bool reachableStatus = status == MarginStatus.Sufficient || status == MarginStatus.Insufficient;
                if (reachableStatus != plan.Reachable)
                    throw new ArgumentException("Status does not match plan reachability.", nameof(status));
                if (status == MarginStatus.Sufficient && marginSeconds < 0d)
                    throw new ArgumentException("A sufficient margin cannot be negative.", nameof(status));
                if (status == MarginStatus.Insufficient && marginSeconds >= 0d)
                    throw new ArgumentException("An insufficient margin must be negative.", nameof(status));
            }
            Services = new ReadOnlyCollection<ServiceMargin>(copy.ToArray());
            Plan = plan;
        }

        public static ReturnMarginEstimate NotApplicable(double computedAtSeconds) =>
            new ReturnMarginEstimate(MarginStatus.NotApplicable, computedAtSeconds, 0d, SuitService.Oxygen,
                Array.Empty<ServiceMargin>(), null);
    }
}
