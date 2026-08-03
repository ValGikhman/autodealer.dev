using System.Collections.Generic;

namespace autodealer.dev.Models {
    public sealed class PricingPageViewModel {
        public IList<PricingPlanViewModel> Plans { get; set; }
    }

    public sealed class PricingPlanViewModel {
        public string PlanCode { get; set; }
        public string DisplayName { get; set; }
        public decimal? MonthlyPrice { get; set; }
        public int MonthlyRequestQuota { get; set; }
        public short MaxApiKeys { get; set; }
        public bool IsRecommended { get; set; }
    }
}
