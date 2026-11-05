using System;
using System.Web;
using System.Web.Mvc;
using autodealer.dev.Services;

namespace autodealer.dev.Controllers {
    [AdminAuthorize]
    public sealed class NeoVinController : Controller {
        [HttpGet]
        public ActionResult Index() {
            var canonicalUrl = Url.RouteUrl("NeoVin");
            if (!string.Equals(Request.Path.TrimEnd('/'), canonicalUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
                return Redirect(canonicalUrl);

            ViewBag.Title="MarketCheck VIN decoder";
            ViewBag.Description="Decode a VIN into complete MarketCheck NeoVIN specifications, equipment, options and warranty details.";
            ViewBag.Robots="noindex,nofollow";
            ViewBag.ResearchDefaultApiKeyEnabled = ResearchDefaultApiKey.IsConfigured;
            Response.Cache.SetCacheability(HttpCacheability.NoCache);
            Response.Cache.SetNoStore();
            return View();
        }
    }
}
