using autodealer.dev.Data;
using autodealer.dev.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace autodealer.dev.Services {
    public sealed class PlanService : IPlanService {
        private readonly string connectionString;

        public PlanService() {
            connectionString = AutoDealerConnectionString.Resolve();
        }

        public IList<PricingPlanViewModel> GetActivePlans() {
            return GetActivePlans(null);
        }

        public IList<PricingPlanViewModel> GetActivePlans(string customerEmail) {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("The AutoDealer.dev database connection is not configured.");

            using (var context = new AutoDealerDataContext(connectionString)) {
                long? currentPlanId = null;
                var normalizedEmail = (customerEmail ?? string.Empty).Trim().ToLowerInvariant();
                if (normalizedEmail.Length > 0) {
                    currentPlanId = context.Subscriptions
                        .Where(x => x.Client.Email == normalizedEmail && x.Client.Status == "active")
                        .OrderByDescending(x => x.CurrentPeriodEndUtc)
                        .Select(x => (long?)x.PlanId)
                        .FirstOrDefault();
                }

                var hasCurrentPlan = currentPlanId.HasValue;
                var currentPlanValue = currentPlanId.GetValueOrDefault();

                var plans = context.Plans
                    .Where(x => x.IsActive)
                    .Select(x => new PricingPlanViewModel {
                        PlanCode = x.PlanCode,
                        DisplayName = x.DisplayName,
                        MonthlyPrice = x.MonthlyPrice,
                        MonthlyRequestQuota = x.MonthlyRequestQuota,
                        MaxApiKeys = x.MaxApiKeys,
                        IsCurrentPlan = hasCurrentPlan && x.PlanId == currentPlanValue
                    })
                    .ToList()
                    .OrderBy(x => x.MonthlyPrice.HasValue ? 0 : 1)
                    .ThenBy(x => x.MonthlyPrice)
                    .ThenBy(x => x.DisplayName)
                    .ToList();

                if (plans.Count > 1) plans[plans.Count / 2].IsRecommended = true;
                return plans;
            }
        }
    }
}
