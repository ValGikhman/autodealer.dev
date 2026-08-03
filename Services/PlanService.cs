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
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("The AutoDealer.dev database connection is not configured.");

            using (var context = new AutoDealerDataContext(connectionString)) {
                var plans = context.Plans
                    .Where(x => x.IsActive)
                    .Select(x => new PricingPlanViewModel {
                        PlanCode = x.PlanCode,
                        DisplayName = x.DisplayName,
                        MonthlyPrice = x.MonthlyPrice,
                        MonthlyRequestQuota = x.MonthlyRequestQuota,
                        MaxApiKeys = x.MaxApiKeys
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
