using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;

namespace autodealer.dev {
    public class RouteConfig {
        public static void RegisterRoutes(RouteCollection routes) {
            routes.IgnoreRoute("{resource}.axd/{*pathInfo}");
            routes.IgnoreRoute("api/{*pathInfo}"); // <— important in mixed MVC + Web API apps

            routes.MapRoute("Admin", "majordome/{action}", new { controller = "Admin", action = "Login" });
            routes.MapRoute("Account", "account/{action}", new { controller = "Account", action = "Register" });
            routes.MapRoute("Documentation", "documentation/{action}", new { controller = "Documentation", action = "Index" });
            routes.MapRoute("DealerDemo", "request-a-dealer-demo", new { controller = "Solutions", action = "RequestDemo" });
            routes.MapRoute("VinSolution", "solutions/vin-decoder", new { controller = "Solutions", action = "VinDecoder" });
            routes.MapRoute("ShowroomSolution", "solutions/digital-showroom", new { controller = "Solutions", action = "DigitalShowroom" });
            routes.MapRoute("Solutions", "solutions", new { controller = "Solutions", action = "Index" });
            routes.MapRoute("Pricing", "pricing", new { controller = "Pricing", action = "Index" });

            routes.MapRoute(
                name: "Default",
                url: "{controller}/{action}/{id}",
                defaults: new { controller = "Home", action = "Index", id = UrlParameter.Optional }
            );
        }
    }
}
