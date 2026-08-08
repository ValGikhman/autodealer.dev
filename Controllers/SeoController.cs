using autodealer.dev.Services;
using System;
using System.Configuration;
using System.Text;
using System.Web.Mvc;

namespace autodealer.dev.Controllers {
    public sealed class SeoController : Controller {
        private static readonly string[] SitemapPaths = {
            "/", "/solutions", "/solutions/vin-decoder", "/solutions/digital-showroom", "/pricing", "/request-a-dealer-demo",
            "/documentation", "/documentation/vin-html", "/documentation/authentication", "/documentation/errors-and-limits",
            "/account/register", "/about", "/contact", "/terms-and-privacy"
        };

        [HttpGet]
        public ActionResult Robots() {
            Response.Cache.SetCacheability(System.Web.HttpCacheability.Public);
            Response.Cache.SetMaxAge(TimeSpan.FromHours(12));
            var indexingEnabled = string.Equals(ConfigurationManager.AppSettings["Seo:AllowIndexing"], "true", StringComparison.OrdinalIgnoreCase);
            var body = indexingEnabled
                ? "User-agent: *\nAllow: /\nDisallow: /api/\nDisallow: /majordome/\nDisallow: /account/dashboard\nDisallow: /account/success\n\nSitemap: " + SeoUrl.Absolute("/sitemap.xml") + "\n"
                : "User-agent: *\nDisallow: /\n";
            return Content(body, "text/plain", Encoding.UTF8);
        }

        [HttpGet]
        public ActionResult Sitemap() {
            Response.Cache.SetCacheability(System.Web.HttpCacheability.Public);
            Response.Cache.SetMaxAge(TimeSpan.FromHours(12));
            var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">\n");
            foreach (var path in SitemapPaths) {
                var location = SeoUrl.Absolute(path);
                xml.Append("  <url><loc>").Append(System.Security.SecurityElement.Escape(location)).Append("</loc></url>\n");
            }
            xml.Append("</urlset>");
            return Content(xml.ToString(), "application/xml", Encoding.UTF8);
        }
    }
}
