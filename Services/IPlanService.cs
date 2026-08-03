using autodealer.dev.Models;
using System.Collections.Generic;

namespace autodealer.dev.Services {
    public interface IPlanService {
        IList<PricingPlanViewModel> GetActivePlans();
    }
}
