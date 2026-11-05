using autodealer.dev.Models;
using autodealer.dev.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System;
using System.Text;
using System.Web.Mvc;

namespace autodealer.dev.Controllers {
    public abstract class ResearchWorkspaceController : Controller {
        private readonly Func<AuctionBidService> auctionBidServiceFactory;
        private static readonly JsonSerializerSettings BidJsonSettings = new JsonSerializerSettings {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        protected ResearchWorkspaceController(Func<AuctionBidService> auctionBidServiceFactory) {
            if (auctionBidServiceFactory == null) throw new ArgumentNullException("auctionBidServiceFactory");
            this.auctionBidServiceFactory = auctionBidServiceFactory;
        }

        protected abstract bool CanUseWorkspace();

        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Bid(AuctionBidRequest request) {
            Response.Cache.SetCacheability(System.Web.HttpCacheability.NoCache);
            Response.Cache.SetNoStore();
            if (!CanUseWorkspace()) return BidJson(new { error = "Your client account is not available." }, 403);
            if (!ModelState.IsValid)
                return BidJson(new { error = "Enter an expected selling price greater than $0 and no more than $2,000,000, and an optional current hammer bid between $0 and $2,000,000." }, 400);
            try {
                // Local arithmetic only: this does not make another provider request.
                var result = auctionBidServiceFactory().Calculate(request);
                return BidJson(result, 200);
            } catch (ArgumentException ex) {
                return BidJson(new { error = ex.Message }, 400);
            } catch (AuctionBidConfigurationException ex) {
                return BidJson(new { error = ex.Message }, 503);
            }
        }

        private ActionResult BidJson(object value, int statusCode) {
            Response.StatusCode = statusCode;
            Response.TrySkipIisCustomErrors = true;
            return Content(JsonConvert.SerializeObject(value, BidJsonSettings), "application/json", Encoding.UTF8);
        }
    }

    [AdminAuthorize]
    public sealed class PriceResearchController : ResearchWorkspaceController {
        [Unity.InjectionConstructor]
        public PriceResearchController() : this(() => new AuctionBidService()) { }
        public PriceResearchController(Func<AuctionBidService> factory) : base(factory) { }
        protected override bool CanUseWorkspace() { return true; }

        [HttpGet]
        public ActionResult Index() {
            var canonicalUrl = Url.RouteUrl("PriceResearch");
            if (!string.Equals(Request.Path.TrimEnd('/'), canonicalUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase)) return Redirect(canonicalUrl);
            ViewBag.Title="Price research";
            ViewBag.Description="Research a vehicle's MarketCheck price and comparable listings by VIN, mileage and location.";
            ViewBag.Robots="noindex,nofollow";
            ViewBag.ResearchDefaultApiKeyEnabled=ResearchDefaultApiKey.IsConfigured;
            Response.Cache.SetCacheability(System.Web.HttpCacheability.NoCache);
            Response.Cache.SetNoStore();
            return View();
        }
    }
}
