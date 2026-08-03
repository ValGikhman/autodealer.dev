using autodealer.dev.Models;
using autodealer.dev.Services;
using System.Web.Mvc;

namespace autodealer.dev.Controllers {
    public class SolutionsController : Controller {
        private readonly IDealerDemoRequestService demoRequestService;

        public SolutionsController(IDealerDemoRequestService demoRequestService) {
            this.demoRequestService = demoRequestService;
        }

        public ActionResult Index() { return View(); }
        public ActionResult VinDecoder() { return View(); }
        public ActionResult DigitalShowroom() { return View(); }

        [HttpGet]
        public ActionResult RequestDemo() {
            return View(new DealerDemoRequestViewModel {
                LocationCount = 1,
                PreferredContact = "Email"
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult RequestDemo(DealerDemoRequestViewModel model) {
            if (!string.IsNullOrWhiteSpace(model.CompanyWebsite)) return View("RequestDemoSuccess", model);
            if (!ModelState.IsValid) return View(model);

            try {
                if (demoRequestService.Send(model)) return View("RequestDemoSuccess", model);
            }
            catch (System.Exception ex) {
                var detail = ex.GetBaseException().Message;
                var error = HttpContext.IsDebuggingEnabled && Request.IsLocal
                    ? "IONOS SMTP error: " + detail
                    : "Your request could not be delivered right now. Please try again shortly.";
                ModelState.AddModelError("", error);
                return View(model);
            }

            ModelState.AddModelError("", "Your request could not be delivered right now. Please try again shortly.");
            return View(model);
        }
    }
}
