using autodealer.dev.Models;
using autodealer.dev.Services;
using System;
using System.Linq;
using System.Security.Principal;
using System.Web;
using System.Web.Mvc;
using System.Web.Security;

namespace autodealer.dev.Controllers {
    public sealed class AdminController : Controller {
        private const string AdminRole = "Admin";
        private readonly IAdminService adminService;

        public AdminController(IAdminService adminService) {
            this.adminService = adminService;
        }

        [AllowAnonymous]
        [HttpGet]
        public ActionResult Login(string returnUrl) {
            if (User.IsInRole(AdminRole)) return RedirectToAction("Dashboard");
            return View(new AdminLoginViewModel { ReturnUrl = returnUrl });
        }

        [AllowAnonymous]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Login(AdminLoginViewModel model) {
            if (!ModelState.IsValid) return View(model);

            try {
                if (!adminService.Authenticate(model.UserId, model.Password)) {
                    ModelState.AddModelError("", "The administrator user ID or password is incorrect.");
                    model.Password = null;
                    return View(model);
                }

                IssueAdminTicket(model.UserId.Trim(), model.RememberMe);
                if (Url.IsLocalUrl(model.ReturnUrl)) return Redirect(model.ReturnUrl);
                return RedirectToAction("Dashboard");
            }
            catch (InvalidOperationException ex) {
                ModelState.AddModelError("", ex.Message);
                model.Password = null;
                return View(model);
            }
        }

        [AdminAuthorize]
        [HttpGet]
        public ActionResult Dashboard() {
            return View(adminService.GetDashboard());
        }

        [AdminAuthorize]
        [HttpGet]
        public ActionResult Customers() {
            return View(adminService.GetDashboard());
        }

        [AdminAuthorize]
        [HttpGet]
        public ActionResult CustomerGridData() {
            var rows = adminService.GetDashboard().Customers.Select(customer => new {
                customer.ClientId,
                customer.BusinessName,
                customer.ClientNumber,
                customer.ContactName,
                customer.Email,
                customer.PlanName,
                customer.SubscriptionStatus,
                customer.ActiveApiKeyCount,
                customer.EmailCount,
                PeriodEnd = customer.PeriodEndUtc.HasValue ? customer.PeriodEndUtc.Value.ToString("MMM d, yyyy") : "N/A",
                Created = customer.CreatedUtc.ToString("MMM d, yyyy"),
                CreatedSort = customer.CreatedUtc.ToString("o"),
                ContactHref = "mailto:" + customer.Email + "?subject=" + Uri.EscapeDataString("Your AutoDealer.dev account")
            });
            return Json(new { Data = rows }, JsonRequestBehavior.AllowGet);
        }

        [AdminAuthorize]
        [HttpGet]
        public ActionResult CustomerEmailGridData(long clientId) {
            var rows = adminService.GetClientEmails(clientId).Select(email => new {
                email.ClientEmailHistoryId,
                email.ClientId,
                Sent = email.SentUtc.ToString("MMM d, yyyy HH:mm:ss 'UTC'"),
                SentSort = email.SentUtc.ToString("o"),
                email.ToEmail,
                email.Subject,
                email.HtmlBody,
                View = true
            });
            return new JsonResult {
                Data = new { Data = rows },
                JsonRequestBehavior = JsonRequestBehavior.AllowGet,
                MaxJsonLength = int.MaxValue
            };
        }

        [AdminAuthorize]
        [HttpGet]
        public ActionResult DemoRequestGridData() {
            var rows = adminService.GetDashboard().DemoRequests.Select(request => new {
                request.RequestId,
                request.BusinessName,
                request.CurrentWebsite,
                request.WebsiteHref,
                request.ContactName,
                request.Email,
                request.Phone,
                request.PrimaryGoal,
                request.LocationCount,
                request.InventorySize,
                Inventory = request.InventorySize + (request.LocationCount.HasValue ? " / " + request.LocationCount.Value + " location" + (request.LocationCount.Value == 1 ? "" : "s") : string.Empty),
                request.Message,
                request.PreferredContact,
                request.Status,
                Received = request.CreatedUtc.ToString("MMM d, yyyy HH:mm 'UTC'"),
                CreatedSort = request.CreatedUtc.ToString("o"),
                request.ContactHref,
                request.ContactAction
            });
            return Json(new { Data = rows }, JsonRequestBehavior.AllowGet);
        }

        [AdminAuthorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Logout() {
            FormsAuthentication.SignOut();
            return RedirectToAction("Login");
        }

        private void IssueAdminTicket(string userId, bool rememberMe) {
            var issuedUtc = DateTime.UtcNow;
            var expiresUtc = rememberMe ? issuedUtc.AddDays(30) : issuedUtc.AddHours(8);
            var ticket = new FormsAuthenticationTicket(
                1,
                userId,
                issuedUtc.ToLocalTime(),
                expiresUtc.ToLocalTime(),
                rememberMe,
                AdminRole,
                FormsAuthentication.FormsCookiePath);
            var cookie = new HttpCookie(FormsAuthentication.FormsCookieName, FormsAuthentication.Encrypt(ticket)) {
                HttpOnly = true,
                Secure = FormsAuthentication.RequireSSL,
                SameSite = SameSiteMode.Lax,
                Expires = rememberMe ? expiresUtc.ToLocalTime() : DateTime.MinValue
            };
            Response.Cookies.Add(cookie);
            HttpContext.User = new GenericPrincipal(new FormsIdentity(ticket), new[] { AdminRole });
        }
    }
}
