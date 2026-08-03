using autodealer.dev.Models;
using autodealer.dev.Services;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
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
            var model = new AccountRegistrationViewModel { PlanCode = (plan ?? string.Empty).Trim().ToUpperInvariant() };
            PopulatePlanOptions(model);
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Register(AccountRegistrationViewModel model) {
            if (!ModelState.IsValid) {
                PopulatePlanOptions(model);
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
            PopulatePlanOptions(model);
            return View(model);
        }

        [HttpGet]
        public ActionResult Success() {
            var result = TempData["CreatedAccount"] as AccountCreatedViewModel;
            if (result == null) return RedirectToAction("Register");
            return View(result);
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
                    ModelState.AddModelError("", "The email or password is incorrect, or the account is temporarily locked.");
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

        private void PopulatePlanOptions(AccountRegistrationViewModel model) {
            try {
                var plans = planService.GetActivePlans();
                if (!plans.Any(x => string.Equals(x.PlanCode, model.PlanCode, StringComparison.OrdinalIgnoreCase)))
                    model.PlanCode = plans.Select(x => x.PlanCode).FirstOrDefault();

                model.PlanOptions = plans.Select(x => new SelectListItem {
                    Value = x.PlanCode,
                    Text = x.DisplayName + (x.MonthlyPrice.HasValue ? " - " + x.MonthlyPrice.Value.ToString("$0.##") + "/mo" : " - Custom"),
                    Selected = string.Equals(x.PlanCode, model.PlanCode, StringComparison.OrdinalIgnoreCase)
                }).ToList();
            }
            catch (SqlException) {
                model.PlanOptions = new List<SelectListItem>();
            }
        }
    }
}
