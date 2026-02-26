using System.Net.Http.Formatting;
using System.Net.Http.Headers;
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
        // /api/inventory?format=json
        config.Formatters.JsonFormatter.MediaTypeMappings.Add(
            new QueryStringMapping("format", "json", new MediaTypeHeaderValue("application/json"))
        );

        // /api/inventory?format=xml
        config.Formatters.XmlFormatter.MediaTypeMappings.Add(
            new QueryStringMapping("format", "xml", new MediaTypeHeaderValue("application/xml"))
        );
    }
}