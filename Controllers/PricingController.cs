using autodealer.dev.Models;
using autodealer.dev.Services;
using System.Web.Mvc;

namespace autodealer.dev.Controllers {
    public class PricingController : Controller {
        private readonly IPlanService planService;

        public PricingController(IPlanService planService) {
            this.planService = planService;
        }

        public ActionResult Index() {
            return View(new PricingPageViewModel { Plans = planService.GetActivePlans() });
        }
    }
}
