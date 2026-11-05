using autodealer.dev;
using autodealer.dev.Controllers;
using System;
using System.Collections;
using System.Collections.Specialized;
using System.Linq;
using System.Security.Principal;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;

class ResearchMajordomeRegression {
    static void Check(bool value, string label) {
        if (!value) throw new Exception(label);
        Console.WriteLine("PASS " + label);
    }

    static int Main() {
        try {
            var routes = new RouteCollection();
            RouteConfig.RegisterRoutes(routes);
            VerifyPage(routes, "PriceResearch", "PriceResearch", typeof(PriceResearchController), "/majordome/priceResearch",
                new[] { "/priceResearch", "/PriceResearch/Index" });
            VerifyPage(routes, "NeoVin", "NeoVin", typeof(NeoVinController), "/majordome/vinDecoder",
                new[] { "/vinDecoder", "/NeoVin", "/NeoVin/Index" });
            return 0;
        } catch (Exception ex) {
            Console.Error.WriteLine(ex.GetBaseException());
            return 1;
        }
    }

    static void VerifyPage(RouteCollection routes, string routeName, string controllerName, Type controllerType, string canonical, string[] aliases) {
        var descriptor = new ReflectedActionDescriptor(controllerType.GetMethod("Index"), "Index", new ReflectedControllerDescriptor(controllerType));
        var authorization = controllerType.GetCustomAttributes(typeof(AdminAuthorizeAttribute), true).Cast<AdminAuthorizeAttribute>().Single();
        Check(!descriptor.IsDefined(typeof(AllowAnonymousAttribute), true) && !descriptor.ControllerDescriptor.IsDefined(typeof(AllowAnonymousAttribute), true), controllerName + " cannot bypass Majordome authorization");

        foreach (var path in new[] { canonical }.Concat(aliases)) {
            var anonymous = new FakeContext(path + "?source=route-test", Principal(false, false));
            var route = routes.GetRouteData(anonymous);
            Check(route != null && string.Equals((string)route.Values["controller"], controllerName, StringComparison.OrdinalIgnoreCase)
                && string.Equals((string)route.Values["action"], "Index", StringComparison.OrdinalIgnoreCase), path + " resolves to the research controller before the Admin catch-all");

            var controller = (Controller)Activator.CreateInstance(controllerType);
            var context = new ControllerContext(new RequestContext(anonymous, route), controller);
            var url = new UrlHelper(context.RequestContext, routes);
            Check(url.RouteUrl(routeName) == canonical, path + " generates its canonical Majordome URL");

            var denied = new AuthorizationContext(context, descriptor);
            authorization.OnAuthorization(denied);
            var login = denied.Result as RedirectToRouteResult;
            Check(login != null && (string)login.RouteValues["controller"] == "Admin" && (string)login.RouteValues["action"] == "Login"
                && (string)login.RouteValues["returnUrl"] == anonymous.Request.RawUrl, path + " redirects anonymous visitors to Majordome login with the complete return URL");

            var ordinary = new FakeContext(path, Principal(true, false));
            var forbidden = new AuthorizationContext(new ControllerContext(new RequestContext(ordinary, route), controller), descriptor);
            authorization.OnAuthorization(forbidden);
            Check(forbidden.Result is HttpStatusCodeResult && ((HttpStatusCodeResult)forbidden.Result).StatusCode == 403, path + " forbids signed-in non-admin accounts");

            var admin = new FakeContext(path, Principal(true, true));
            var adminController = (Controller)Activator.CreateInstance(controllerType);
            var adminContext = new ControllerContext(new RequestContext(admin, route), adminController);
            adminController.ControllerContext = adminContext;
            adminController.Url = new UrlHelper(adminContext.RequestContext, routes);
            var permitted = new AuthorizationContext(adminContext, descriptor);
            authorization.OnAuthorization(permitted);
            Check(permitted.Result == null && admin.Policy.ValidationCallbacks == 1 && admin.Policy.ProxyMaxAge == TimeSpan.Zero, path + " permits Admin and applies the authorization cache validation callback");

            var result = (ActionResult)controllerType.GetMethod("Index").Invoke(adminController, null);
            if (path == canonical) {
                Check(result is ViewResult && admin.Policy.NoStore && admin.Policy.Cacheability == HttpCacheability.NoCache,
                    path + " renders only its canonical Admin view without caching");
            } else {
                Check(result is RedirectResult && ((RedirectResult)result).Url == canonical,
                    path + " redirects an authorized legacy URL to the canonical Majordome page");
            }
        }
    }

    static IPrincipal Principal(bool authenticated, bool admin) {
        return new GenericPrincipal(new GenericIdentity(authenticated ? "research-fixture" : "", authenticated ? "fixture" : ""), admin ? new[] { "Admin" } : new string[0]);
    }

    sealed class FakeContext : HttpContextBase {
        readonly FakeRequest request;
        readonly FakeResponse response;
        readonly IDictionary items = new Hashtable();
        IPrincipal user;
        public readonly FakeCachePolicy Policy = new FakeCachePolicy();
        public FakeContext(string path, IPrincipal principal) { user = principal; request = new FakeRequest(path, principal.Identity.IsAuthenticated); response = new FakeResponse(Policy); }
        public override HttpRequestBase Request { get { return request; } }
        public override HttpResponseBase Response { get { return response; } }
        public override IPrincipal User { get { return user; } set { user = value; } }
        public override IDictionary Items { get { return items; } }
        public override object GetService(Type serviceType) { return null; }
    }

    sealed class FakeRequest : HttpRequestBase {
        readonly string rawUrl;
        readonly bool authenticated;
        readonly NameValueCollection serverVariables = new NameValueCollection();
        public FakeRequest(string value, bool signedIn) { rawUrl = value; authenticated = signedIn; }
        public override string RawUrl { get { return rawUrl; } }
        public override string Path { get { return rawUrl.Split('?')[0]; } }
        public override string AppRelativeCurrentExecutionFilePath { get { return "~" + Path; } }
        public override string ApplicationPath { get { return "/"; } }
        public override string PathInfo { get { return ""; } }
        public override string HttpMethod { get { return "GET"; } }
        public override bool IsAuthenticated { get { return authenticated; } }
        public override Uri Url { get { return new Uri("https://localhost" + rawUrl); } }
        public override NameValueCollection ServerVariables { get { return serverVariables; } }
    }

    sealed class FakeResponse : HttpResponseBase {
        readonly FakeCachePolicy policy;
        public FakeResponse(FakeCachePolicy cache) { policy = cache; }
        public override HttpCachePolicyBase Cache { get { return policy; } }
        public override string ApplyAppPathModifier(string path) { return path; }
    }

    sealed class FakeCachePolicy : HttpCachePolicyBase {
        public int ValidationCallbacks;
        public TimeSpan? ProxyMaxAge;
        public bool NoStore;
        public HttpCacheability? Cacheability;
        public override void SetProxyMaxAge(TimeSpan value) { ProxyMaxAge = value; }
        public override void AddValidationCallback(HttpCacheValidateHandler handler, object data) { ValidationCallbacks++; }
        public override void SetNoStore() { NoStore = true; }
        public override void SetCacheability(HttpCacheability value) { Cacheability = value; }
    }
}
