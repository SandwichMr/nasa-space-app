using System;

namespace Trainer.Simulation.Eva
{
    /// <summary>
    /// The single return-margin rule of phase 3 (D1). Pure: it reads a suit's levels, the declared model and a
    /// <see cref="ReturnPlan"/> that the authority built from confirmed state, and never changes anything.
    ///
    /// For each service: usable = level − reserve; needed for the return = walk rate × walking seconds + rest rate ×
    /// (airlock wait + ingress) (no tool load on the way back); margin = (usable − needed) / current rate, where the
    /// current rate is what the member does now (activity rate, plus the tool load on the battery). The member's margin
    /// is the SMALLEST service margin and that service is the limiting one (declared order breaks ties): the first
    /// service that runs short limits the return, never an average of services or of crew members.
    ///
    /// Conservative by construction: the reserve is never counted as usable, waiting and ingress are included, and the
    /// route length comes from the authority's grid route (never shorter than the straight line). It is not a safety
    /// standard, a clinical limit or a universal EVA radius: values are the scenario's declared synthetic inputs.
    /// </summary>
    public static class ReturnMarginCalculator
    {
        public static ReturnMarginEstimate Estimate(SuitModelConfiguration model, SuitLevels levels, SuitActivity currentActivity,
            double currentToolWatts, ReturnPlan plan, double nowSeconds)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (levels == null) throw new ArgumentNullException(nameof(levels));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            EvaGuard.Defined(currentActivity, nameof(currentActivity));
            EvaGuard.NonNegative(currentToolWatts, nameof(currentToolWatts));

            var services = new ServiceMargin[model.Services.Count];
            double min = double.PositiveInfinity;
            SuitService limiting = SuitService.Oxygen;
            double standing = plan.AirlockWaitSeconds + plan.IngressSeconds;
            for (int i = 0; i < services.Length; i++)
            {
                SuitServiceModel service = model.Services[i];
                double usable = levels.Get(service.Service) - service.Reserve;
                double needed = service.WalkRate * plan.WalkSeconds + service.RestRate * standing;
                double rate = EvaSuitSimulation.Rate(service, currentActivity, currentToolWatts);
                double margin = (usable - needed) / rate;
                services[i] = new ServiceMargin(service.Service, usable, needed, rate, margin);
                if (margin < min)
                {
                    min = margin;
                    limiting = service.Service;
                }
            }
            MarginStatus status = !plan.Reachable ? MarginStatus.ReturnUnavailable
                : min < 0d ? MarginStatus.Insufficient : MarginStatus.Sufficient;
            return new ReturnMarginEstimate(status, nowSeconds, min, limiting, services, plan);
        }
    }
}
