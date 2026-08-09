using autodealer.dev.Models;
using autodealer.dev.Services;
using System;
using System.Data.Linq;
using System.Data.SqlClient;
using System.ComponentModel.DataAnnotations;
using System.Web;
using System.Web.Security;
using System.Web.Mvc;

namespace autodealer.dev.Controllers {
    public class AccountController : Controller {
        private readonly IClientAccountService accountService;
        private readonly IPlanService planService;

        public AccountController(IClientAccountService accountService, IPlanService planService) {
            this.accountService = accountService;
            this.planService = planService;
        }

        [HttpGet]
        public ActionResult Register(string plan) {
            if (User.IsInRole("Admin")) return RedirectToAction("Dashboard", "Admin");
            if (Request.IsAuthenticated) return RedirectToAction("Dashboard");
            var model = new AccountRegistrationViewModel {
                PlanCode = (plan ?? string.Empty).Trim().ToUpperInvariant()
            };
            planService.PopulatePlanOptions(model);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Register(AccountRegistrationViewModel model) {
            if (!ModelState.IsValid) {
                planService.PopulatePlanOptions(model);
                return View(model);
            }
            try {
                TempData["CreatedAccount"] = accountService.Create(model);
                return RedirectToAction("Success");
            }
            catch (SqlException ex) when (ex.Number == 2601 || ex.Number == 2627) {
                ModelState.AddModelError("Email", "An account already exists for this email address.");
            }
            catch (SqlException) {
                ModelState.AddModelError("", "Account creation is temporarily unavailable. Please try again shortly.");
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException) {
                ModelState.AddModelError("", ex.Message);
            }
            model.Password = null;
            model.ConfirmPassword = null;
            planService.PopulatePlanOptions(model);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult CheckEmailAvailability(string email) {
            Response.Cache.SetCacheability(HttpCacheability.NoCache);
            Response.Cache.SetNoStore();
            var normalizedEmail = (email ?? string.Empty).Trim();
            if (!new EmailAddressAttribute().IsValid(normalizedEmail))
                return Json(new { Available = false, Message = "Enter a valid email address first." });
            try {
                var available = accountService.IsEmailAvailable(normalizedEmail, null);
                return Json(new {
                    Available = available,
                    Message = available ? "Email is available." : "An account already uses this email address."
                });
            }
            catch (SqlException) {
                Response.StatusCode = 503;
                return Json(new { Available = false, Message = "Email availability cannot be checked right now." });
            }
        }

        [HttpGet]
        public ActionResult Success() {
            var result = TempData["CreatedAccount"] as AccountCreatedViewModel;
            if (result == null) return RedirectToAction("Register");
            return View(result);
        }

        [HttpGet]
        public ActionResult VerifyEmail(string token) {
            Response.Cache.SetCacheability(HttpCacheability.NoCache);
            Response.Cache.SetNoStore();
            try {
                var result = accountService.VerifyEmail(token);
                if (result.Status == EmailVerificationStatus.DeliveryFailed)
                    result.RetryUrl = Url.Action("VerifyEmail", "Account", new { token = token });
                return View(result);
            }
            catch (SqlException) {
                return View(new EmailVerificationViewModel {
                    Status = EmailVerificationStatus.DeliveryFailed,
                    RetryUrl = Url.Action("VerifyEmail", "Account", new { token })
                });
            }
            catch (ChangeConflictException) {
                return View(new EmailVerificationViewModel {
                    Status = EmailVerificationStatus.DeliveryFailed,
                    RetryUrl = Url.Action("VerifyEmail", "Account", new { token })
                });
            }
            catch (InvalidOperationException) {
                return View(new EmailVerificationViewModel {
                    Status = EmailVerificationStatus.DeliveryFailed,
                    RetryUrl = Url.Action("VerifyEmail", "Account", new { token })
                });
            }
        }

        [HttpGet]
        public ActionResult Login(string returnUrl) {
            if (User.IsInRole("Admin")) return RedirectToAction("Dashboard", "Admin");
            if (Request.IsAuthenticated) return RedirectToAction("Dashboard");
            return View(new AccountLoginViewModel { ReturnUrl = returnUrl });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Login(AccountLoginViewModel model) {
            if (!ModelState.IsValid) return View(model);
            try {
                var account = accountService.Authenticate(model.Email, model.Password);
                if (account == null) {
                    ModelState.AddModelError(
                        "",
                        "The email or password is incorrect, or the account is temporarily locked.");
                    model.Password = null;
                    return View(model);
                }

                FormsAuthentication.SetAuthCookie(account.Email, model.RememberMe);
                if (Url.IsLocalUrl(model.ReturnUrl)) return Redirect(model.ReturnUrl);
                return RedirectToAction("Dashboard");
            }
            catch (SqlException) {
                ModelState.AddModelError("", "Sign in is temporarily unavailable. Please try again shortly.");
                model.Password = null;
                return View(model);
            }
            catch (InvalidOperationException ex) {
                ModelState.AddModelError("", ex.Message);
                model.Password = null;
                return View(model);
            }
        }

        [Authorize]
        [HttpGet]
        public ActionResult Dashboard() {
            if (User.IsInRole("Admin")) return RedirectToAction("Dashboard", "Admin");
            var account = accountService.GetDashboard(User.Identity.Name);
            if (account == null) {
                FormsAuthentication.SignOut();
                return RedirectToAction("Login");
            }
            return View(account);
        }

        [Authorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Logout() {
            FormsAuthentication.SignOut();
            return RedirectToAction("Index", "Home");
        }

    }
}
