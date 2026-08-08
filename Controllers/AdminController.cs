using autodealer.dev.Models;
using autodealer.dev.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data.Linq;
using System.Data.SqlClient;
using System.Linq;
using System.Security.Principal;
using System.Web;
using System.Web.Mvc;
using System.Web.Security;

namespace autodealer.dev.Controllers {
    public sealed class AdminController : Controller {
        private const string AdminRole = "Admin";
        private readonly IAdminService adminService;
        private readonly IApiKeyIssuanceService apiKeyIssuanceService;
        private readonly IClientAccountService clientAccountService;

        public AdminController(IAdminService adminService, IApiKeyIssuanceService apiKeyIssuanceService, IClientAccountService clientAccountService) {
            this.adminService = adminService;
            this.apiKeyIssuanceService = apiKeyIssuanceService;
            this.clientAccountService = clientAccountService;
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
        public ActionResult Opportunities() {
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
                SubscriptionBadgeClass = SubscriptionBadgeClass(customer.SubscriptionStatus),
                customer.ActiveApiKeyCount,
                customer.ApiKeyCount,
                customer.SubscriptionCount,
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
        public ActionResult CustomerAccountDetailData(long clientId) {
            var details = adminService.GetClientAccountDetails(clientId);
            var apiKeys = details.ApiKeys.Select(key => new {
                key.ApiKeyId,
                key.Name,
                key.KeyPrefix,
                key.Scopes,
                key.Status,
                Created = key.CreatedUtc.ToString("MMM d, yyyy HH:mm 'UTC'"),
                CreatedSort = key.CreatedUtc.ToString("o"),
                LastUsed = key.LastUsedUtc.HasValue ? key.LastUsedUtc.Value.ToString("MMM d, yyyy HH:mm 'UTC'") : "Never",
                Expires = key.ExpiresUtc.HasValue ? key.ExpiresUtc.Value.ToString("MMM d, yyyy HH:mm 'UTC'") : "No expiration",
                Revoked = key.RevokedUtc.HasValue ? key.RevokedUtc.Value.ToString("MMM d, yyyy HH:mm 'UTC'") : string.Empty
            });
            var subscriptions = details.Subscriptions.Select(subscription => new {
                subscription.SubscriptionId,
                subscription.PlanName,
                subscription.PlanCode,
                subscription.Status,
                StatusBadgeClass = SubscriptionBadgeClass(subscription.Status),
                Quota = subscription.MonthlyRequestQuota,
                subscription.MaxApiKeys,
                PeriodStart = subscription.CurrentPeriodStartUtc.ToString("MMM d, yyyy HH:mm 'UTC'"),
                PeriodEnd = subscription.CurrentPeriodEndUtc.ToString("MMM d, yyyy HH:mm 'UTC'"),
                PeriodEndSort = subscription.CurrentPeriodEndUtc.ToString("o"),
                CancelAtPeriodEnd = subscription.CancelAtPeriodEnd ? "Yes" : "No",
                ProviderSubscription = string.IsNullOrWhiteSpace(subscription.ProviderSubscriptionId) ? "Not connected" : subscription.ProviderSubscriptionId,
                Created = subscription.CreatedUtc.ToString("MMM d, yyyy HH:mm 'UTC'")
            });
            return Json(new { ApiKeys = apiKeys, Subscriptions = subscriptions }, JsonRequestBehavior.AllowGet);
        }

        [AdminAuthorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult IssueApiKey(long clientId, string name) {
            Response.Cache.SetCacheability(HttpCacheability.NoCache);
            Response.Cache.SetNoStore();
            try {
                var result = apiKeyIssuanceService.Issue(clientId, name);
                return Json(new {
                    Ok = true,
                    result.ApiKeyId,
                    result.Name,
                    ApiKey = result.FullApiKey,
                    result.RecipientEmail,
                    result.EmailSent,
                    Message = result.EmailSent
                        ? "The key was created and emailed to the customer."
                        : "The key was created, but email delivery failed. Copy the credential now."
                });
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) {
                Response.StatusCode = 400;
                return Json(new { Ok = false, Message = ex.Message });
            }
            catch (SqlException) {
                Response.StatusCode = 503;
                return Json(new { Ok = false, Message = "The API key could not be issued because the database is temporarily unavailable." });
            }
        }

        [AdminAuthorize]
        [HttpGet]
        public ActionResult NewClient() {
            Response.Cache.SetCacheability(HttpCacheability.NoCache);
            Response.Cache.SetNoStore();
            try {
                var model = adminService.GetNewClientDefaults();
                return Json(new {
                    Ok = true,
                    Entity = "clientNew",
                    model.ClientNumber,
                    model.TemporaryPassword,
                    model.PlanCode,
                    model.PlanOptions
                }, JsonRequestBehavior.AllowGet);
            }
            catch (InvalidOperationException ex) {
                Response.StatusCode = 400;
                return Json(new { Ok = false, Message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [AdminAuthorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult NewClient(AccountRegistrationViewModel model, string clientNumber) {
            Response.Cache.SetCacheability(HttpCacheability.NoCache);
            Response.Cache.SetNoStore();
            if (!ModelState.IsValid) return EditValidationFailure();
            try {
                var result = clientAccountService.Create(model, clientNumber, true);
                return Json(new {
                    Ok = true,
                    result.ClientNumber,
                    result.Email,
                    result.VerificationEmailSent,
                    Message = result.VerificationEmailSent
                        ? "Customer saved. A confirmation email is waiting in their inbox."
                        : "Customer saved, but the confirmation email could not be delivered."
                });
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) {
                Response.StatusCode = 400;
                return Json(new { Ok = false, Message = ex.Message });
            }
            catch (SqlException) {
                Response.StatusCode = 503;
                return Json(new { Ok = false, Message = "The customer could not be created because the database is temporarily unavailable." });
            }
        }

        [AdminAuthorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult CheckClientEmailAvailability(string email, long? clientId) {
            Response.Cache.SetCacheability(HttpCacheability.NoCache);
            Response.Cache.SetNoStore();
            var normalizedEmail = (email ?? string.Empty).Trim();
            if (!new EmailAddressAttribute().IsValid(normalizedEmail))
                return Json(new { Available = false, Message = "Enter a valid email address first." });
            try {
                var available = clientAccountService.IsEmailAvailable(normalizedEmail, clientId);
                return Json(new {
                    Available = available,
                    Message = available ? "Email is available." : "Another account already uses this email address."
                });
            }
            catch (SqlException) {
                Response.StatusCode = 503;
                return Json(new { Available = false, Message = "Email availability cannot be checked right now." });
            }
        }

        [AdminAuthorize]
        [HttpGet]
        public ActionResult EditClient(long id) {
            Response.Cache.SetCacheability(HttpCacheability.NoCache);
            Response.Cache.SetNoStore();
            try {
                return Json(ClientEditResponse(adminService.GetClientForEdit(id)), JsonRequestBehavior.AllowGet);
            }
            catch (KeyNotFoundException ex) {
                Response.StatusCode = 404;
                return Json(new { Ok = false, Message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [AdminAuthorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EditClient(AdminClientEditViewModel model) {
            Response.Cache.SetCacheability(HttpCacheability.NoCache);
            Response.Cache.SetNoStore();
            if (!ModelState.IsValid) return EditValidationFailure();
            try {
                return Json(ClientEditResponse(adminService.UpdateClient(model)));
            }
            catch (KeyNotFoundException ex) {
                Response.StatusCode = 404;
                return Json(new { Ok = false, Message = ex.Message });
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) {
                Response.StatusCode = 400;
                return Json(new { Ok = false, Message = ex.Message });
            }
            catch (SqlException) {
                Response.StatusCode = 503;
                return Json(new { Ok = false, Message = "The dealer account could not be updated because the database is temporarily unavailable." });
            }
        }

        [AdminAuthorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteClient(long clientId, bool confirmDelete = false) {
            Response.Cache.SetCacheability(HttpCacheability.NoCache);
            Response.Cache.SetNoStore();
            if (!confirmDelete) {
                Response.StatusCode = 400;
                return Json(new { Ok = false, Message = "Check the confirmation box before deleting this customer." });
            }
            try {
                var businessName = adminService.DeleteClient(clientId);
                return Json(new {
                    Ok = true,
                    Message = businessName + " and all related records were permanently deleted."
                });
            }
            catch (KeyNotFoundException ex) {
                Response.StatusCode = 404;
                return Json(new { Ok = false, Message = ex.Message });
            }
            catch (ArgumentException ex) {
                Response.StatusCode = 400;
                return Json(new { Ok = false, Message = ex.Message });
            }
            catch (SqlException ex) {
                Response.StatusCode = 503;
                var message = ex.Number == 229
                    ? "The application database user cannot delete client records. Run Database/005_AdminClientDeletionPermissions.sql against the production database."
                        : ex.Number == 547
                            ? "A related database table currently prevents this client from being deleted. No records were removed."
                            : ex.Number == -2
                                ? "Client deletion timed out. No records were removed; try again after database activity has settled."
                                : "The customer could not be deleted because the database operation failed (SQL " + ex.Number + "). No records were removed.";
                return Json(new { Ok = false, Message = message });
            }
        }

        [AdminAuthorize]
        [HttpGet]
        public ActionResult EditApiKey(long id) {
            Response.Cache.SetCacheability(HttpCacheability.NoCache);
            Response.Cache.SetNoStore();
            try {
                return Json(ApiKeyEditResponse(adminService.GetApiKeyForEdit(id)), JsonRequestBehavior.AllowGet);
            }
            catch (KeyNotFoundException ex) {
                Response.StatusCode = 404;
                return Json(new { Ok = false, Message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [AdminAuthorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EditApiKey(AdminApiKeyEditViewModel model) {
            Response.Cache.SetCacheability(HttpCacheability.NoCache);
            Response.Cache.SetNoStore();
            if (!ModelState.IsValid) return EditValidationFailure();
            try {
                return Json(ApiKeyEditResponse(adminService.UpdateApiKey(model)));
            }
            catch (KeyNotFoundException ex) {
                Response.StatusCode = 404;
                return Json(new { Ok = false, Message = ex.Message });
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) {
                Response.StatusCode = 400;
                return Json(new { Ok = false, Message = ex.Message });
            }
            catch (SqlException) {
                Response.StatusCode = 503;
                return Json(new { Ok = false, Message = "The API key could not be updated because the database is temporarily unavailable." });
            }
        }

        [AdminAuthorize]
        [HttpGet]
        public ActionResult NewSubscription(long clientId) {
            Response.Cache.SetCacheability(HttpCacheability.NoCache);
            Response.Cache.SetNoStore();
            try {
                return Json(SubscriptionEditResponse(adminService.GetNewSubscriptionDefaults(clientId)), JsonRequestBehavior.AllowGet);
            }
            catch (KeyNotFoundException ex) {
                Response.StatusCode = 404;
                return Json(new { Ok = false, Message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
            catch (InvalidOperationException ex) {
                Response.StatusCode = 400;
                return Json(new { Ok = false, Message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [AdminAuthorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult NewSubscription(AdminSubscriptionEditViewModel model) {
            Response.Cache.SetCacheability(HttpCacheability.NoCache);
            Response.Cache.SetNoStore();
            if (!ModelState.IsValid) return EditValidationFailure();
            try {
                return Json(SubscriptionEditResponse(adminService.CreateSubscription(model)));
            }
            catch (KeyNotFoundException ex) {
                Response.StatusCode = 404;
                return Json(new { Ok = false, Message = ex.Message });
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) {
                Response.StatusCode = 400;
                return Json(new { Ok = false, Message = ex.Message });
            }
            catch (SqlException) {
                Response.StatusCode = 503;
                return Json(new { Ok = false, Message = "The subscription could not be created because the database is temporarily unavailable." });
            }
        }

        [AdminAuthorize]
        [HttpGet]
        public ActionResult EditSubscription(long id) {
            Response.Cache.SetCacheability(HttpCacheability.NoCache);
            Response.Cache.SetNoStore();
            try {
                return Json(SubscriptionEditResponse(adminService.GetSubscriptionForEdit(id)), JsonRequestBehavior.AllowGet);
            }
            catch (KeyNotFoundException ex) {
                Response.StatusCode = 404;
                return Json(new { Ok = false, Message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [AdminAuthorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EditSubscription(AdminSubscriptionEditViewModel model) {
            Response.Cache.SetCacheability(HttpCacheability.NoCache);
            Response.Cache.SetNoStore();
            if (!ModelState.IsValid) return EditValidationFailure();
            try {
                return Json(SubscriptionEditResponse(adminService.UpdateSubscription(model)));
            }
            catch (KeyNotFoundException ex) {
                Response.StatusCode = 404;
                return Json(new { Ok = false, Message = ex.Message });
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) {
                Response.StatusCode = 400;
                return Json(new { Ok = false, Message = ex.Message });
            }
            catch (SqlException) {
                Response.StatusCode = 503;
                return Json(new { Ok = false, Message = "The subscription could not be updated because the database is temporarily unavailable." });
            }
        }

        private object ApiKeyEditResponse(AdminApiKeyEditViewModel model) {
            return new {
                Ok = true,
                Entity = "apiKey",
                model.ApiKeyId,
                model.ClientId,
                model.Name,
                model.KeyPrefix,
                model.Scopes,
                model.Status,
                model.SubscriptionId,
                ExpiresUtc = ToDateTimeLocal(model.ExpiresUtc),
                model.SubscriptionOptions,
                StatusOptions = new[] {
                    new AdminEditOptionViewModel { Value = "active", Text = "Active" },
                    new AdminEditOptionViewModel { Value = "revoked", Text = "Revoked" },
                    new AdminEditOptionViewModel { Value = "expired", Text = "Expired" }
                },
                ScopeOptions = new[] {
                    new AdminEditOptionViewModel { Value = "vin:read", Text = "VIN decode — vin:read" }
                }
            };
        }

        private object ClientEditResponse(AdminClientEditViewModel model) {
            return new {
                Ok = true,
                Entity = "client",
                model.ClientId,
                model.ClientNumber,
                model.BusinessName,
                model.FirstName,
                model.LastName,
                model.Email,
                model.Phone,
                model.Status,
                EmailVerifiedUtc = ToDateTimeLocal(model.EmailVerifiedUtc),
                CreatedUtc = model.CreatedUtc.ToString("MMM d, yyyy HH:mm 'UTC'"),
                StatusOptions = new[] {
                    new AdminEditOptionViewModel { Value = "pending", Text = "Pending" },
                    new AdminEditOptionViewModel { Value = "active", Text = "Active" },
                    new AdminEditOptionViewModel { Value = "suspended", Text = "Suspended" },
                    new AdminEditOptionViewModel { Value = "closed", Text = "Closed" }
                }
            };
        }

        private object SubscriptionEditResponse(AdminSubscriptionEditViewModel model) {
            return new {
                Ok = true,
                Entity = "subscription",
                model.SubscriptionId,
                model.ClientId,
                model.PlanId,
                model.Status,
                CurrentPeriodStartUtc = ToDateTimeLocal(model.CurrentPeriodStartUtc),
                CurrentPeriodEndUtc = ToDateTimeLocal(model.CurrentPeriodEndUtc),
                model.CancelAtPeriodEnd,
                model.ProviderSubscriptionId,
                model.PlanOptions,
                StatusOptions = new[] {
                    new AdminEditOptionViewModel { Value = "trialing", Text = "Trialing" },
                    new AdminEditOptionViewModel { Value = "active", Text = "Active" },
                    new AdminEditOptionViewModel { Value = "past_due", Text = "Past due" },
                    new AdminEditOptionViewModel { Value = "paused", Text = "Paused" },
                    new AdminEditOptionViewModel { Value = "canceled", Text = "Canceled" }
                }
            };
        }

        private object DemoRequestEditResponse(AdminDemoRequestEditViewModel model, bool isNew) {
            return new {
                Ok = true,
                IsNew = isNew,
                model.RequestId,
                model.BusinessName,
                model.ContactName,
                model.Email,
                model.Phone,
                model.CurrentWebsite,
                model.LocationCount,
                model.InventorySize,
                model.PrimaryGoal,
                model.PreferredContact,
                model.Message,
                model.Status,
                CreatedUtc = model.CreatedUtc.ToString("MMM d, yyyy HH:mm 'UTC'"),
                StatusOptions = new[] {
                    new AdminEditOptionViewModel { Value = "new", Text = "New" },
                    new AdminEditOptionViewModel { Value = "active", Text = "Active" },
                    new AdminEditOptionViewModel { Value = "postponed", Text = "Postponed" },
                    new AdminEditOptionViewModel { Value = "closed", Text = "Closed" }
                },
                ContactOptions = new[] {
                    new AdminEditOptionViewModel { Value = "Email", Text = "Email" },
                    new AdminEditOptionViewModel { Value = "Phone", Text = "Phone" }
                }
            };
        }

        private ActionResult EditValidationFailure() {
            Response.StatusCode = 400;
            var message = ModelState.Values.SelectMany(x => x.Errors)
                .Select(x => string.IsNullOrWhiteSpace(x.ErrorMessage) && x.Exception != null ? x.Exception.Message : x.ErrorMessage)
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));
            return Json(new { Ok = false, Message = message ?? "Review the highlighted values and try again." });
        }

        private static string ToDateTimeLocal(DateTime? value) {
            return value.HasValue ? value.Value.ToString("yyyy-MM-dd'T'HH:mm") : string.Empty;
        }

        private static string ToDateTimeLocal(DateTime value) {
            return value.ToString("yyyy-MM-dd'T'HH:mm");
        }

        private static string SubscriptionBadgeClass(string status) {
            if (string.Equals(status, "trialing", StringComparison.OrdinalIgnoreCase)) return "info";
            if (string.Equals(status, "active", StringComparison.OrdinalIgnoreCase)) return "success";
            if (string.Equals(status, "paused", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, "inactive", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, "past_due", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(status, "canceled", StringComparison.OrdinalIgnoreCase)) return "danger";
            return "secondary";
        }

        private static string OpportunityStatusBadgeClass(string status) {
            if (string.Equals(status, "new", StringComparison.OrdinalIgnoreCase)) return "info";
            if (string.Equals(status, "active", StringComparison.OrdinalIgnoreCase)) return "success";
            if (string.Equals(status, "postponed", StringComparison.OrdinalIgnoreCase)) return "warning";
            return "secondary";
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
                StatusBadgeClass = OpportunityStatusBadgeClass(request.Status),
                Received = request.CreatedUtc.ToString("MMM d, yyyy HH:mm 'UTC'"),
                CreatedSort = request.CreatedUtc.ToString("o"),
                request.ContactHref,
                request.ContactAction
            });
            return Json(new { Data = rows }, JsonRequestBehavior.AllowGet);
        }

        [AdminAuthorize]
        [HttpGet]
        public ActionResult NewDemoRequest() {
            return Json(DemoRequestEditResponse(adminService.GetNewDemoRequestDefaults(), true), JsonRequestBehavior.AllowGet);
        }

        [AdminAuthorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult NewDemoRequest(AdminDemoRequestEditViewModel model) {
            if (!ModelState.IsValid) return EditValidationFailure();
            try {
                return Json(DemoRequestEditResponse(adminService.SaveDemoRequest(model, true), false));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) {
                Response.StatusCode = 400;
                return Json(new { Ok = false, Message = ex.Message });
            }
            catch (SqlException) {
                Response.StatusCode = 503;
                return Json(new { Ok = false, Message = "The opportunity could not be created because the database is temporarily unavailable." });
            }
        }

        [AdminAuthorize]
        [HttpGet]
        public ActionResult EditDemoRequest(Guid id) {
            try {
                return Json(DemoRequestEditResponse(adminService.GetDemoRequestForEdit(id), false), JsonRequestBehavior.AllowGet);
            }
            catch (KeyNotFoundException ex) {
                Response.StatusCode = 404;
                return Json(new { Ok = false, Message = ex.Message }, JsonRequestBehavior.AllowGet);
            }
        }

        [AdminAuthorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EditDemoRequest(AdminDemoRequestEditViewModel model) {
            if (!ModelState.IsValid) return EditValidationFailure();
            try {
                return Json(DemoRequestEditResponse(adminService.SaveDemoRequest(model, false), false));
            }
            catch (KeyNotFoundException ex) {
                Response.StatusCode = 404;
                return Json(new { Ok = false, Message = ex.Message });
            }
            catch (ChangeConflictException) {
                Response.StatusCode = 409;
                return Json(new { Ok = false, Message = "This opportunity changed while it was being edited. Refresh the grid and try again." });
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException) {
                Response.StatusCode = 400;
                return Json(new { Ok = false, Message = ex.Message });
            }
            catch (SqlException ex) {
                Response.StatusCode = 503;
                var message = ex.Number == 547
                    ? "The production opportunity-status constraint is out of date. Run the latest Database/005_AdminClientDeletionPermissions.sql, then try again."
                    : ex.Number == 229
                        ? "The AUTODEALER database user cannot update opportunity records. Grant UPDATE on dbo.DealerDemoRequests to the production user."
                        : ex.Number == 8152 || ex.Number == 2628
                            ? "One of the opportunity values is longer than the database column allows."
                            : "The opportunity could not be updated because the database operation failed (SQL " + ex.Number + ").";
                return Json(new { Ok = false, Message = message });
            }
        }

        [AdminAuthorize]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteDemoRequest(Guid requestId, bool confirmDelete = false) {
            Response.Cache.SetCacheability(HttpCacheability.NoCache);
            Response.Cache.SetNoStore();
            if (!confirmDelete) {
                Response.StatusCode = 400;
                return Json(new { Ok = false, Message = "Check the confirmation box before deleting this opportunity." });
            }
            try {
                var businessName = adminService.DeleteDemoRequest(requestId);
                return Json(new { Ok = true, Message = businessName + " was permanently removed from new opportunities." });
            }
            catch (KeyNotFoundException ex) {
                Response.StatusCode = 404;
                return Json(new { Ok = false, Message = ex.Message });
            }
            catch (ArgumentException ex) {
                Response.StatusCode = 400;
                return Json(new { Ok = false, Message = ex.Message });
            }
            catch (SqlException ex) {
                Response.StatusCode = 503;
                var message = ex.Number == 229
                    ? "The application database user cannot delete opportunity records. Run Database/005_AdminClientDeletionPermissions.sql against the production database."
                    : ex.Number == 547
                        ? "A related database record currently prevents this opportunity from being deleted. No records were removed."
                        : "The opportunity could not be deleted because the database operation failed (SQL " + ex.Number + "). No records were removed.";
                return Json(new { Ok = false, Message = message });
            }
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
