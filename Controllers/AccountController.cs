using autodealer.dev.Models;
using autodealer.dev.Services;
using System;
using System.Data.SqlClient;
using System.Web.Security;
using System.Web.Mvc;

namespace autodealer.dev.Controllers {
    public class AccountController : Controller {
        private readonly IClientAccountService accountService;

        public AccountController(IClientAccountService accountService) {
            this.accountService = accountService;
        }

        [HttpGet]
        public ActionResult Register(string plan) {
            if (Request.IsAuthenticated) return RedirectToAction("Dashboard");
            var selected = (plan ?? "STARTER").ToUpperInvariant();
            if (selected != "STARTER" && selected != "GROWTH" && selected != "PLATFORM") selected = "STARTER";
            return View(new AccountRegistrationViewModel { PlanCode = selected });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Register(AccountRegistrationViewModel model) {
            if (!ModelState.IsValid) return View(model);
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
