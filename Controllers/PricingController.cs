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
            var plans = Request.IsAuthenticated && !User.IsInRole("Admin")
                ? planService.GetActivePlans(User.Identity.Name)
                : planService.GetActivePlans();
            return View(new PricingPageViewModel { Plans = plans });
        }
    }
}
