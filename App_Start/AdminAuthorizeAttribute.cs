using System.Web.Mvc;
using System.Web.Routing;

namespace autodealer.dev {
    public sealed class AdminAuthorizeAttribute : AuthorizeAttribute {
        public AdminAuthorizeAttribute() {
            Roles = "Admin";
        }

        protected override void HandleUnauthorizedRequest(AuthorizationContext filterContext) {
            if (filterContext.HttpContext.Request.IsAuthenticated) {
                filterContext.Result = new HttpStatusCodeResult(403);
                return;
            }

            filterContext.Result = new RedirectToRouteResult(
                new RouteValueDictionary(new {
                    controller = "Admin",
                    action = "Login",
                    returnUrl = filterContext.HttpContext.Request.RawUrl
                }));
        }
    }
}
