using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Web;
using System.Web.Http;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;
using System.Web.Security;

namespace autodealer.dev {
    public class MvcApplication : System.Web.HttpApplication {
        protected void Application_Start() {
            GlobalConfiguration.Configure(WebApiConfig.Register); // <— must be here, and BEFORE MVC routes
            UnityConfig.RegisterComponents();
            RegisterSanitizingModelBinders();
            AreaRegistration.RegisterAllAreas();
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            BundleConfig.RegisterBundles(BundleTable.Bundles);
        }

        private static void RegisterSanitizingModelBinders() {
            ModelBinders.Binders.DefaultBinder = new Models.SanitizingModelBinder();
        }

        protected void Application_AuthenticateRequest(object sender, EventArgs e) {
            var cookie = Request.Cookies[FormsAuthentication.FormsCookieName];
            if (cookie == null || string.IsNullOrWhiteSpace(cookie.Value)) return;

            try {
                var ticket = FormsAuthentication.Decrypt(cookie.Value);
                if (ticket == null || ticket.Expired || !string.Equals(ticket.UserData, "Admin", StringComparison.Ordinal)) return;
                Context.User = new GenericPrincipal(new FormsIdentity(ticket), new[] { "Admin" });
            }
            catch (CryptographicException) {
                // Ignore an invalid or stale authentication cookie.
            }
        }
    }
}
