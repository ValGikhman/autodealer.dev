using autodealer.dev;
using autodealer.dev.Controllers;
using autodealer.dev.Models;
using autodealer.dev.Services;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Specialized;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Routing;
using Unity;

class AuctionBidRegression {
    static void Check(bool condition, string label) {
        if (!condition) throw new Exception(label);
        Console.WriteLine("PASS " + label);
    }

    static AuctionBidRequest Request(decimal? sale = 20000m, decimal? bid = null) {
        return new AuctionBidRequest { ExpectedSellingPriceUsd = sale, CurrentHammerBidUsd = bid };
    }

    static NameValueCollection Settings() {
        return new NameValueCollection {
            { "AuctionBid:AuctionFeeUsd", "450" },
            { "AuctionBid:TransportUsd", "350" },
            { "AuctionBid:MinimumProfitUsd", "2000" }
        };
    }

    static bool InvalidConfiguration(Action action) {
        try { action(); return false; } catch (AuctionBidConfigurationException) { return true; }
    }

    static bool InvalidRequest(AuctionBidService service, AuctionBidRequest request) {
        try { service.Calculate(request); return false; } catch (ArgumentException) { return true; }
    }

    static int Main() {
        try {
            Arithmetic();
            Configuration();
            Validation();
            MvcEndpoint();
            return 0;
        } catch (Exception ex) {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    static void Arithmetic() {
        var service = new AuctionBidService(AuctionBidService.ReadSettings(Settings()));
        var result = service.Calculate(Request(20000m, 17000m));
        Check(result.Status == "ready" && result.MaximumHammerBidUsd == 17200m && result.MaximumTotalInvestmentUsd == 18000m && result.ProfitAtMaximumUsd == 2000m,
            "$20,000 selling assumption with $800 known costs and $2,000 target allows $17,200 hammer bid");
        Check(result.CurrentTotalInvestmentUsd == 17800m && result.CurrentProfitUsd == 2200m && result.CurrentBidWithinLimit == true,
            "current $17,000 hammer bid includes the fee and transport and clears the $2,000 target");
        Check(result.AuctionFeeUsd == 450m && result.TransportUsd == 350m && result.TotalKnownCostsUsd == 800m && result.MinimumProfitUsd == 2000m
            && result.BreakEvenHammerBidUsd == 19200m && result.RoundingIncrementUsd == 100m && result.CostScope == AuctionBidService.CostScope,
            "budget contains exactly the configured fee and transport; excluded cost categories and bid percentage add nothing");

        var higherTarget = new AuctionBidService(new AuctionBidSettings(450m, 350m, 3000m));
        var higher = higherTarget.Calculate(Request(20000m, 17000m));
        Check(higher.MaximumHammerBidUsd == 16200m && higher.MaximumTotalInvestmentUsd == 17000m && higher.ProfitAtMaximumUsd == 3000m
            && higher.CurrentProfitUsd == 2200m && higher.CurrentBidWithinLimit == false,
            "configuring $3,000 target lowers the ceiling and evaluates an above-limit current bid without clipping it");

        var fractional = higherTarget.Calculate(Request(20099.99m));
        Check(fractional.MaximumHammerBidUsd == 16200m && fractional.ProfitAtMaximumUsd == 3099.99m && fractional.BreakEvenHammerBidUsd == 19299.99m,
            "maximum rounds down to $100 while break-even and profit retain cents");
        var betweenIncrements = service.Calculate(Request(20099.99m, 17250m));
        Check(betweenIncrements.MaximumHammerBidUsd == 17200m && betweenIncrements.CurrentProfitUsd == 2049.99m && betweenIncrements.CurrentBidWithinLimit == false,
            "current bid above the rounded ceiling remains outside the limit even when its profit exceeds the target");
        Check(!fractional.CurrentHammerBidUsd.HasValue && !fractional.CurrentTotalInvestmentUsd.HasValue && !fractional.CurrentProfitUsd.HasValue && !fractional.CurrentBidWithinLimit.HasValue,
            "omitted current bid stays absent rather than becoming a zero bid");
        var zeroBid = higherTarget.Calculate(Request(20000m, 0m));
        Check(zeroBid.CurrentTotalInvestmentUsd == 800m && zeroBid.CurrentProfitUsd == 19200m && zeroBid.CurrentBidWithinLimit == true,
            "explicit current bid of zero retains fixed acquisition costs");

        foreach (var sale in new[] { 500m, 3799m, 3800m, 3800.01m, 3899.99m }) {
            var unavailable = higherTarget.Calculate(Request(sale, 0m));
            Check(unavailable.Status == "no_bid" && !unavailable.MaximumHammerBidUsd.HasValue && !unavailable.MaximumTotalInvestmentUsd.HasValue
                && !unavailable.ProfitAtMaximumUsd.HasValue && unavailable.CurrentBidWithinLimit == false,
                "no feasible positive rounded bid is reported for selling assumption " + sale);
        }
        var belowCosts = higherTarget.Calculate(Request(500m, 17000m));
        Check(belowCosts.BreakEvenHammerBidUsd == 0m && belowCosts.CurrentProfitUsd == -17300m && belowCosts.CurrentTotalInvestmentUsd == 17800m,
            "negative break-even is floored at zero and losing current bids still show their full loss");
        Check(higherTarget.Calculate(Request(3900m)).MaximumHammerBidUsd == 100m,
            "first feasible $100 bid at the exact profit threshold remains available");

        var custom = new AuctionBidService(new AuctionBidSettings(625.50m, 374.50m, 4500m)).Calculate(Request(20000m));
        Check(custom.MaximumHammerBidUsd == 14500m && custom.MaximumTotalInvestmentUsd == 15500m && custom.ProfitAtMaximumUsd == 4500m,
            "all three configured amounts change the result with no hidden defaults");

        foreach (var profile in new[] { new AuctionBidSettings(450m, 350m, 2000m), new AuctionBidSettings(625.50m, 374.50m, 4500m), new AuctionBidSettings(0m, 0m, 0m) }) {
            var calculator = new AuctionBidService(profile);
            foreach (var sale in new[] { 0.01m, 99.99m, 100m, 800m, 3800m, 3900m, 20099.99m, 2000000m }) {
                var row = calculator.Calculate(Request(sale));
                if (row.Status != "ready") continue;
                var bid = row.MaximumHammerBidUsd.Value;
                if (bid % 100m != 0m || row.ProfitAtMaximumUsd < profile.MinimumProfitUsd
                    || sale - (bid + 100m + profile.AuctionFeeUsd + profile.TransportUsd) >= profile.MinimumProfitUsd)
                    throw new Exception("The rounded ceiling failed profit safety or maximality.");
            }
        }
        Check(true, "rounded ceilings preserve the target and the next $100 bid would miss it across multiple cost profiles");
    }

    static void Configuration() {
        Check(InvalidConfiguration(() => AuctionBidService.ReadSettings(null)), "missing configuration has no fallback cost profile");
        foreach (var key in new[] { "AuctionBid:AuctionFeeUsd", "AuctionBid:TransportUsd", "AuctionBid:MinimumProfitUsd" }) {
            var missing = Settings(); missing.Remove(key);
            Check(InvalidConfiguration(() => AuctionBidService.ReadSettings(missing)), key + " is required");
            foreach (var value in new[] { "", "not-a-number", "-1", "2000001", "0.001", "450,00" }) {
                var invalid = Settings(); invalid[key] = value;
                if (!InvalidConfiguration(() => AuctionBidService.ReadSettings(invalid))) throw new Exception("Invalid configuration accepted for " + key);
            }
        }
        Check(true, "invalid, negative, oversized, subcent and ambiguous grouped config values fail clearly");
        var excessiveProfit = Settings(); excessiveProfit["AuctionBid:MinimumProfitUsd"] = "1000001";
        Check(InvalidConfiguration(() => AuctionBidService.ReadSettings(excessiveProfit))
            && InvalidConfiguration(() => new AuctionBidService(new AuctionBidSettings(-1m, 0m, 0m)))
            && InvalidConfiguration(() => new AuctionBidService((AuctionBidSettings)null)),
            "explicit test settings and file settings enforce the same calculator limits");
    }

    static void Validation() {
        var service = new AuctionBidService(new AuctionBidSettings(450m, 350m, 2000m));
        Check(InvalidRequest(service, null) && InvalidRequest(service, Request(null)), "missing request or selling price is rejected");
        foreach (var value in new[] { -1m, 0m, 2000000.01m })
            Check(InvalidRequest(service, Request(value)), "invalid selling assumption is rejected: " + value);
        Check(InvalidRequest(service, Request(20000m, -0.01m)) && InvalidRequest(service, Request(20000m, 2000000.01m)),
            "current bid outside the allowed range is rejected");
        var limit = service.Calculate(Request(2000000m, 2000000m));
        Check(limit.Status == "ready" && limit.CurrentProfitUsd == -800m && limit.CurrentBidWithinLimit == false,
            "upper input boundary is accepted and does not overflow or hide a loss");
    }

    static void MvcEndpoint() {
        using (var container = new UnityContainer()) {
            Check(container.Resolve<PriceResearchController>() != null,
                "Unity resolves the production controller constructor without reading auction settings before Index");
        }
        var controllerType = typeof(PriceResearchController);
        var action = controllerType.GetMethod("Bid");
        Check(controllerType.GetCustomAttributes(typeof(AdminAuthorizeAttribute), true).Cast<AdminAuthorizeAttribute>().Single().Roles == "Admin"
            && action.IsDefined(typeof(HttpPostAttribute), true) && action.IsDefined(typeof(ValidateAntiForgeryTokenAttribute), true)
            && !action.IsDefined(typeof(AllowAnonymousAttribute), true),
            "bid endpoint requires Majordome Admin, POST and anti-forgery validation");
        var routes = new RouteCollection(); RouteConfig.RegisterRoutes(routes);
        var routeContext = new FakeContext();
        var route = routes.GetRouteData(routeContext);
        Check(route != null && (string)route.Values["controller"] == "PriceResearch" && (string)route.Values["action"] == "Bid"
            && new UrlHelper(new RequestContext(routeContext, route), routes).RouteUrl("PriceResearchBid") == "/majordome/priceResearch/bid",
            "named bid route resolves before the Majordome action catch-all");

        var context = new FakeContext();
        var controller = Controller(context, () => new AuctionBidService(new AuctionBidSettings(450m, 350m, 2000m)));
        var content = (ContentResult)controller.Bid(Request(20000m, 17000m));
        var json = JObject.Parse(content.Content);
        Check(context.Response.StatusCode == 200 && content.ContentType == "application/json" && content.ContentEncoding.WebName == "utf-8"
            && (string)json["status"] == "ready" && (decimal)json["maximumHammerBidUsd"] == 17200m && (decimal)json["currentProfitUsd"] == 2200m
            && json["Status"] == null && json.Properties().Count() == 17,
            "MVC response uses the exact camelCase JSON contract");
        Check(context.Policy.NoStore && context.Policy.Cacheability == HttpCacheability.NoCache && context.Response.TrySkipIisCustomErrors,
            "bid responses are not cached and IIS preserves JSON errors");
        json = JObject.Parse(((ContentResult)controller.Bid(Request(1000m))).Content);
        Check((string)json["status"] == "no_bid" && json["maximumHammerBidUsd"].Type == JTokenType.Null && json["currentHammerBidUsd"].Type == JTokenType.Null,
            "no-bid and omitted optional inputs serialize as null without a valid zero ceiling");

        context = new FakeContext();
        controller = Controller(context, () => new AuctionBidService(AuctionBidService.ReadSettings(new NameValueCollection())));
        json = JObject.Parse(((ContentResult)controller.Bid(Request())).Content);
        Check(context.Response.StatusCode == 503 && ((string)json["error"]).Contains("AuctionBid:AuctionFeeUsd")
            && json.Properties().Count() == 1 && context.Policy.NoStore,
            "missing configured costs return a clear no-store HTTP 503 instead of invented costs");

        bool called = false;
        context = new FakeContext();
        controller = Controller(context, () => { called = true; return new AuctionBidService(new AuctionBidSettings(450m, 350m, 2000m)); });
        controller.ModelState.AddModelError("ExpectedSellingPriceUsd", "fixture-user-input-should-not-echo");
        content = (ContentResult)controller.Bid(Request());
        Check(context.Response.StatusCode == 400 && !called && !content.Content.Contains("fixture-user-input-should-not-echo") && context.Policy.NoStore,
            "model-binding errors return HTTP 400 without using or echoing invalid input");
        context = new FakeContext(); controller = Controller(context, () => new AuctionBidService(new AuctionBidSettings(450m, 350m, 2000m)));
        json = JObject.Parse(((ContentResult)controller.Bid(Request(0m))).Content);
        Check(context.Response.StatusCode == 400 && json["error"] != null, "service validation errors also return JSON HTTP 400");
    }

    static PriceResearchController Controller(FakeContext context, Func<AuctionBidService> factory) {
        var controller = new PriceResearchController(factory);
        controller.ControllerContext = new ControllerContext(new RequestContext(context, new RouteData()), controller);
        return controller;
    }

    sealed class FakeContext : HttpContextBase {
        readonly FakeRequest request = new FakeRequest();
        readonly FakeResponse response;
        readonly IDictionary items = new Hashtable();
        public readonly FakeCachePolicy Policy = new FakeCachePolicy();
        public FakeContext() { response = new FakeResponse(Policy); }
        public override HttpRequestBase Request { get { return request; } }
        public override HttpResponseBase Response { get { return response; } }
        public override IDictionary Items { get { return items; } }
        public override object GetService(Type serviceType) { return null; }
    }
    sealed class FakeRequest : HttpRequestBase {
        public override string AppRelativeCurrentExecutionFilePath { get { return "~/majordome/priceResearch/bid"; } }
        public override string PathInfo { get { return ""; } }
        public override string ApplicationPath { get { return "/"; } }
        public override string HttpMethod { get { return "POST"; } }
        public override NameValueCollection ServerVariables { get { return new NameValueCollection(); } }
    }
    sealed class FakeResponse : HttpResponseBase {
        readonly FakeCachePolicy policy;
        public FakeResponse(FakeCachePolicy policy) { this.policy = policy; }
        public override HttpCachePolicyBase Cache { get { return policy; } }
        public override int StatusCode { get; set; }
        public override bool TrySkipIisCustomErrors { get; set; }
        public override string ApplyAppPathModifier(string path) { return path; }
    }
    sealed class FakeCachePolicy : HttpCachePolicyBase {
        public bool NoStore;
        public HttpCacheability? Cacheability;
        public override void SetNoStore() { NoStore = true; }
        public override void SetCacheability(HttpCacheability value) { Cacheability = value; }
    }
}
