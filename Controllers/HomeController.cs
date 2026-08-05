using autodealer.dev.Models;
using autodealer.dev.Services;
using System;
using System.Linq;
using System.Web.Mvc;

namespace autodealer.dev.Controllers {
    public class HomeController : Controller {
        private readonly IContactInquiryService contactInquiryService;

        public HomeController(IContactInquiryService contactInquiryService) {
            this.contactInquiryService = contactInquiryService;
        }

        public ActionResult Index() { return View(); }
        public ActionResult About() { return View(); }
        public ActionResult Legal() { return View(); }

        [HttpGet]
        public ActionResult Contact() { return View(new ContactInquiryViewModel()); }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Contact(ContactInquiryViewModel model) {
            if (!string.IsNullOrWhiteSpace(model.CompanyWebsite)) return Json(new { success = true });
            if (!string.Equals(model.InquiryType, "website", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(model.InquiryType, "api", StringComparison.OrdinalIgnoreCase))
                ModelState.AddModelError("InquiryType", "Please choose an inquiry type.");

            if (!ModelState.IsValid) {
                var errors = ModelState.Where(x => x.Value.Errors.Count > 0).Select(x => new {
                    field = x.Key,
                    messages = x.Value.Errors.Select(error => string.IsNullOrWhiteSpace(error.ErrorMessage) ? "Please check this field." : error.ErrorMessage).ToArray()
                }).ToArray();
                return Json(new { success = false, errors = errors });
            }

            try {
                contactInquiryService.Send(model);
                return Json(new { success = true });
            }
            catch (Exception ex) {
                System.Diagnostics.Trace.TraceError("Contact inquiry delivery failed: {0}", ex);
                var message = HttpContext.IsDebuggingEnabled && Request.IsLocal
                    ? "Message error: " + ex.GetBaseException().Message
                    : "Your message could not be delivered right now. Please try again shortly.";
                return Json(new { success = false, errors = new[] { new { field = "", messages = new[] { message } } } });
            }
        }
    }
}
