using System.Web.Http;

public static class WebApiConfig {
    public static void Register(HttpConfiguration config) {
        config.MapHttpAttributeRoutes();   // <— required for the attributes above

        // (Optional) keep the conventional route too
        config.Routes.MapHttpRoute(
            name: "DefaultApi",
            routeTemplate: "api/{controller}/{id}",
            defaults: new { id = RouteParameter.Optional }
        );
    }
}