using System.Net.Http.Formatting;
using System.Net.Http.Headers;
using System.Web.Http;
using autodealer.dev.Models;
using System;
using System.Configuration;

public static class WebApiConfig {
    public static void Register(HttpConfiguration config) {
        if (string.Equals(ConfigurationManager.AppSettings["ApiSecurity:Enabled"], "true", StringComparison.OrdinalIgnoreCase))
            config.MessageHandlers.Add(new ApiKeyHandler());

        config.MapHttpAttributeRoutes();   // <— required for the attributes above

        // (Optional) keep the conventional route too
        config.Routes.MapHttpRoute(
            name: "DefaultApi",
            routeTemplate: "api/{controller}/{id}",
            defaults: new { id = RouteParameter.Optional }
        );
    }
}
