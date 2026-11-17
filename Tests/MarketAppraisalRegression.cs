using autodealer.dev.Controllers;
using autodealer.dev.Models;
using autodealer.dev.Services;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;

class MarketAppraisalRegression {
    const string Subject = "1HGCV1F53MA093993";
    static readonly Type Service = typeof(PriceResearchRequest).Assembly.GetType("autodealer.dev.Services.AuctionAppraisalService", true);
    static int checks;
    static void Check(bool value, string label) { if (!value) throw new Exception(label); checks++; Console.WriteLine("PASS " + label); }
    static int Main() { try { Run().GetAwaiter().GetResult(); Console.WriteLine(checks + " market appraisal checks passed."); return 0; } catch (Exception ex) { Console.Error.WriteLine(ex.GetBaseException()); return 1; } }
    static string Vin(int number) => "1HGCV1F5" + number.ToString("D9");
    static JObject Named(int? count = null) {
        var input = new JObject { ["year"] = 2021, ["make"] = "Honda", ["model"] = "Accord", ["trim"] = "EX-L", ["miles"] = 42000 };
        if (count.HasValue) input["comparableCount"] = count.Value;
        return input;
    }
    static PriceResearchRequest Scope(string vin = null) => new PriceResearchRequest { Vin = vin, Miles = 42000, Zip = "45040", Radius = 100 };
    static JObject Listing(string vin, int miles, decimal? price, int days = 10) => new JObject {
        ["vin"] = vin, ["miles"] = miles, ["price"] = price, ["dist"] = 10, ["dom"] = days,
        ["build"] = new JObject { ["year"] = 2021, ["make"] = "Honda", ["model"] = "Accord", ["trim"] = "EX-L" },
        ["dealer"] = new JObject { ["name"] = "Fixture dealer", ["city"] = "Mason", ["state"] = "OH" },
        ["vdp_url"] = "https://dealer.example/vehicle/" + vin
    };
    static JObject SearchData() {
        var rows = new JArray(Listing(Vin(1), 41000, 20000, 10), Listing(Vin(2), 42000, 24000, 20),
            Listing(Vin(3), 43000, 26000, 30), Listing(Vin(4), 44000, 30000, 40), Listing(Vin(5), 45000, null, 50));
        var duplicate = (JObject)rows[1].DeepClone(); duplicate["vin"] = " " + Vin(2).ToLowerInvariant() + " "; rows.Add(duplicate);
        var wrongTrim = Listing(Vin(6), 42000, 100); wrongTrim["build"]["trim"] = "Sport"; rows.Add(wrongTrim);
        var far = Listing(Vin(7), 42000, 100); far["dist"] = 101; rows.Add(far);
        rows.Add(Listing(Vin(8), 100000, 100));
        var unsafeUrl = (JObject)rows[4]; unsafeUrl["vdp_url"] = "javascript:alert(1)";
        return new JObject { ["num_found"] = 96, ["listings"] = rows };
    }
    static JObject McpSearchData(JObject data, JObject arguments) {
        if ((bool?)arguments["include_build_object"] == true) return data;
        // The official MCP tool removes build specifications unless requested.
        var stripped = (JObject)data.DeepClone();
        foreach (var listing in ((JArray)stripped["listings"]).OfType<JObject>()) listing.Remove("build");
        return stripped;
    }
    static async Task<JObject> Invoke(PriceResearchRequest scope, JObject input, Func<string, JObject, CancellationToken, Task<JObject>> call, CancellationToken token = default(CancellationToken)) {
        var method = Service.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(candidate => candidate.Name == "MarketAppraisalAsync" && candidate.GetParameters().Length == 4);
        return await (Task<JObject>)method.Invoke(null, new object[] { scope, input, token, call });
    }
    static async Task ExpectFailure<T>(Func<Task> action, string label) where T : Exception {
        try { await action(); } catch (T) { Check(true, label); return; }
        throw new Exception(label);
    }
    static async Task Run() {
        var calls = new List<Tuple<string, JObject>>();
        var sample = SearchData(); var originalSample = sample.DeepClone();
        Func<string, JObject, CancellationToken, Task<JObject>> provider = (name, arguments, token) => {
            calls.Add(Tuple.Create(name, (JObject)arguments.DeepClone()));
            if (name == "predict_price_with_comparables") return Task.FromResult(new JObject { ["marketcheck_price"] = 25444, ["msrp"] = 32305, ["unneeded_payload"] = new string('x', 20000) });
            if (name == "decode_vin_neovin") return Task.FromResult(new JObject { ["vin"] = Subject, ["year"] = 2021, ["make"] = "Honda", ["model"] = "Accord", ["trim"] = "EX-L" });
            if ((int?)arguments["rows"] == 5) {
                var wrong = Listing(Vin(6), 42000, 1); wrong["build"]["trim"] = "Sport";
                return Task.FromResult(McpSearchData(new JObject { ["listings"] = new JArray(wrong, Listing(Vin(1), 42000, 1)) }, arguments));
            }
            return Task.FromResult(McpSearchData(sample, arguments));
        };
        var input = Named(); var originalInput = input.DeepClone(); var scope = Scope();
        var result = await Invoke(scope, input, provider);
        Check(calls.Count == 3 && calls.Count(call => call.Item1 == "search_active_cars") == 2, "named appraisal uses one reference search, prediction and one bounded comparable search");
        Check(calls.Where(call => call.Item1 == "search_active_cars").All(call => (bool?)call.Item2["include_build_object"] == true),
            "reference and comparable searches request the build specifications needed to validate year, make, model and trim");
        Check((bool)result["usedReferenceVin"] && (string)result["vin"] == Vin(1) && scope.Vin == null, "reference VIN matches the requested trim and is explicitly marked without becoming the user's subject VIN");
        Check((string)result["vehicle"] == "2021 Honda Accord EX-L" && (decimal)result["marketCheckRetailUsd"] == 25444 && (decimal)result["msrpUsd"] == 32305, "vehicle identity and provider price/MSRP remain distinct facts");
        Check(((JArray)result["comparables"]).Count == 5 && (long)result["listingCount"] == 96, "returned sample size is separate from the provider's full matched count");
        var listings = ((JArray)result["comparables"]).OfType<JObject>().ToList();
        Check((string)listings[0]["vin"] == Vin(2) && listings.Any(row => row["askingPriceUsd"].Type == JTokenType.Null), "comparables rank by target mileage and keep missing asking prices");
        Check(listings.All(row => (string)row["dealerName"] == "Fixture dealer" && (string)row["city"] == "Mason" && (string)row["state"] == "OH"), "rich public dealer fields survive formatting");
        Check(listings.Any(row => row["url"].Type == JTokenType.Null), "unsafe listing links are discarded");
        var stats = (JObject)result["statistics"];
        Check((int)stats["sampledListingCount"] == 5 && (int)stats["pricedSampleCount"] == 4 && (decimal)stats["meanPriceUsd"] == 25000 && (decimal)stats["medianPriceUsd"] == 25000, "price statistics use only valid unique fetched listings with prices");
        Check((decimal)stats["p25PriceUsd"] == 23000 && (decimal)stats["p75PriceUsd"] == 27000 && (decimal)stats["medianDaysListed"] == 30, "sample quartiles and median listing days are calculated consistently");
        var activeArguments = calls.Single(call => call.Item1 == "search_active_cars" && (int?)call.Item2["rows"] == 50).Item2;
        Check((string)activeArguments["miles_range"] == "32000-52000" && (int)activeArguments["radius"] == 100 && (string)activeArguments["zip"] == "45040", "active sample carries explicit mileage window, ZIP and radius");
        Check(JToken.DeepEquals(originalInput, input) && JToken.DeepEquals(originalSample, sample) && result.ToString().Length < 10000, "request/provider data remain unchanged and unused provider payload never reaches chat");

        calls.Clear(); var bounded = await Invoke(Scope(), Named(3), provider);
        Check(((JArray)bounded["comparables"]).Count == 3 && (int)bounded["statistics"]["sampledListingCount"] == 5, "requested row limit does not mislabel the statistics sample");
        calls.Clear(); var actual = await Invoke(Scope(Subject), new JObject { ["vin"] = Subject }, provider);
        Check(!(bool)actual["usedReferenceVin"] && (string)actual["vin"] == Subject && calls[0].Item1 == "decode_vin_neovin", "actual VIN appraisals use MCP decoding and never claim a reference VIN");
        var subjectSample = SearchData(); var subjectRow = Listing(Subject, 42000, 25000); subjectRow["vin"] = " " + Subject.ToLowerInvariant() + " "; ((JArray)subjectSample["listings"]).Add(subjectRow);
        var excluded = await Invoke(Scope(Subject), new JObject { ["vin"] = Subject }, (name, args, token) => name == "search_active_cars" ? Task.FromResult(subjectSample) : provider(name, args, token));
        Check(!((JArray)excluded["comparables"]).Any(row => (string)row["vin"] == Subject), "the actual subject listing is excluded from its own comparables");

        foreach (var invalid in new JToken[] { new JValue(0), new JValue(11), new JValue("10"), new JValue(2.5), JValue.CreateNull() }) {
            var bad = Named(); bad["comparableCount"] = invalid; var before = calls.Count;
            await ExpectFailure<ArgumentException>(() => Invoke(Scope(), bad, provider), "invalid comparable count is rejected before provider work: " + invalid.Type);
            Check(calls.Count == before, "invalid count consumes no MCP calls");
        }
        var missingVehicle = Named(); missingVehicle["make"] = "";
        await ExpectFailure<ArgumentException>(() => Invoke(Scope(), missingVehicle, provider), "invalid named vehicle is rejected before provider work");
        try {
            await Invoke(Scope(), Named(), (name, args, token) => Task.FromResult(new JObject { ["listings"] = new JArray() }));
            throw new Exception("missing reference VIN must fail");
        } catch (PriceResearchException ex) { Check(ex.StatusCode == HttpStatusCode.NotFound, "no usable reference VIN produces a clear no-match response"); }
        try {
            await Invoke(Scope(), Named(), (name, args, token) => { throw new PriceResearchException((HttpStatusCode)429, "quota fixture"); });
            throw new Exception("quota must propagate");
        } catch (PriceResearchException ex) { Check((int)ex.StatusCode == 429, "MCP quota errors propagate without retry or direct-API fallback"); }

        var large = new JObject { ["num_found"] = 1000, ["listings"] = new JArray(Enumerable.Range(1, 100).Select(number => Listing(Vin(number), 42000 + number, 10000 + number))) };
        var capped = await Invoke(Scope(), Named(), (name, args, token) => name == "search_active_cars" && (int?)args["rows"] == 50 ? Task.FromResult(large) : provider(name, args, token));
        Check((int)capped["statistics"]["sampledListingCount"] == 50 && ((JArray)capped["comparables"]).Count == 10, "oversized provider arrays are capped before statistics and rendering");
        foreach (var link in new[] { "javascript:alert(1)", "https://user:secret@example.test/car", "https://example.test/car?api_key=secret", "https://example.test/car?token=secret" }) {
            var method = Service.GetMethod("SafeListingUrl", BindingFlags.Static | BindingFlags.NonPublic);
            Check(method.Invoke(null, new object[] { new JValue(link) }) == null, "unsafe or credential-bearing listing link is rejected");
        }
        await CultureNumbers();
        await EndpointChecks();
    }

    static async Task CultureNumbers() {
        var originalCulture = Thread.CurrentThread.CurrentCulture;
        try {
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var near = Listing(Vin(1), 42000, 24000.50m); near["dist"] = 9.5m; near["dom"] = 12.5m;
            var far = Listing(Vin(2), 42000, 1m); far["dist"] = 10.25m;
            var closest = Listing(Vin(3), 42000, 26000.25m); closest["dist"] = .5m; closest["dom"] = 20.5m;
            var market = new JObject { ["num_found"] = 3, ["listings"] = new JArray(near, far, closest) };
            var original = market.DeepClone();
            Func<string, JObject, CancellationToken, Task<JObject>> provider = (name, args, token) => {
                if (name == "predict_price_with_comparables") return Task.FromResult(new JObject { ["marketcheck_price"] = 25444.5m, ["msrp"] = 32305.75m });
                if (name == "decode_vin_neovin") return Task.FromResult(new JObject { ["vin"] = Subject, ["year"] = 2021.0m, ["make"] = "Honda", ["model"] = "Accord", ["trim"] = "EX-L" });
                return Task.FromResult((int?)args["rows"] == 5 ? new JObject { ["listings"] = new JArray(near.DeepClone()) } : market);
            };
            var scope = Scope(); scope.Radius = 10;
            var result = await Invoke(scope, Named(), provider);
            Check((decimal)result["marketCheckRetailUsd"] == 25444.5m && (decimal)result["msrpUsd"] == 32305.75m, "typed fractional estimate and MSRP never inflate under a comma-decimal request culture");
            var rows = ((JArray)result["comparables"]).OfType<JObject>().ToList();
            Check(rows.Count == 2 && (decimal)rows[0]["distanceMiles"] == .5m && (decimal)rows[1]["distanceMiles"] == 9.5m
                && rows.All(row => (string)row["vin"] != Vin(2)), "fractional distances preserve exact values and the requested radius under fr-FR");
            Check((decimal)rows[0]["askingPriceUsd"] == 26000.25m && (decimal)rows[1]["askingPriceUsd"] == 24000.50m
                && (decimal)result["statistics"]["meanPriceUsd"] == 25000.375m && (decimal)result["statistics"]["medianDaysListed"] == 16.5m,
                "fractional asking prices, sample statistics and listing days remain exact under fr-FR");
            var actualScope = Scope(Subject); actualScope.Radius = 10;
            var actual = await Invoke(actualScope, new JObject { ["vin"] = Subject }, provider);
            Check(!(bool)actual["usedReferenceVin"] && (decimal)actual["marketCheckRetailUsd"] == 25444.5m
                && (string)actual["vehicle"] == "2021 Honda Accord EX-L" && ((JArray)actual["comparables"]).Count == 2,
                "actual VIN decoding and appraisal also keep invariant typed numeric values under fr-FR");
            Check(JToken.DeepEquals(original, market), "culture-safe numeric parsing leaves original provider tokens unchanged");
        } finally { Thread.CurrentThread.CurrentCulture = originalCulture; }
    }

    static PriceResearchApiController Controller(bool staff = true, bool workspace = false) {
        var request = new HttpRequestMessage(HttpMethod.Post, "https://localhost/api/service/price-research/market-appraisal");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", staff ? "ad_marketfixture.secret" : "ad_other.secret");
        if (workspace) request.Headers.Add(ClientResearchSession.WorkspaceHeader, "account");
        var controller = new PriceResearchApiController { Request = request, Configuration = new HttpConfiguration() };
        controller.User = new GenericPrincipal(new GenericIdentity("fixture", "ApiKey"), new[] { "vin:read" });
        return controller;
    }
    static async Task<HttpResponseMessage> Post(PriceResearchApiController controller, JObject input, CancellationToken token = default(CancellationToken)) {
        return await (await controller.MarketAppraisal(input, token)).ExecuteAsync(CancellationToken.None);
    }
    static async Task EndpointChecks() {
        Check(typeof(PriceResearchApiController).GetMethod("MarketAppraisal").GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>().Single().Template == "market-appraisal"
            && typeof(PriceResearchApiController).GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>().Single().Roles == "vin:read", "new route stays under existing vin:read authorization");
        var clientType = Service.Assembly.GetType("autodealer.dev.Services.MarketCheckMcpClient", true);
        var field = clientType.GetField("Http", BindingFlags.Static | BindingFlags.NonPublic);
        var original = field.GetValue(null);
        var handler = new McpHandler();
        using (var client = new HttpClient(handler)) {
            field.SetValue(null, client);
            try {
                Check((await Post(Controller(false), Named())).StatusCode == HttpStatusCode.Forbidden
                    && (await Post(Controller(workspace: true), Named())).StatusCode == HttpStatusCode.Forbidden && handler.Calls == 0, "wrong registered key and customer workspace cannot start MCP work");
                var bad = Named(); bad["comparableCount"] = "10";
                Check((await Post(Controller(), bad)).StatusCode == HttpStatusCode.BadRequest && handler.Calls == 0, "invalid count reaches no MCP session even through the API");
                var defaults = Named(); defaults.Remove("miles");
                var response = await Post(Controller(), defaults);
                var body = JObject.Parse(await response.Content.ReadAsStringAsync());
                Check(response.StatusCode == HttpStatusCode.OK && (bool)body["usedDefaultMileage"] && (int)body["miles"] == 50000 && (string)body["zip"] == "45040", "retail endpoint uses existing configured defaults without auction budget settings");
                Check(handler.Initializations == 1 && handler.Closures == 1 && handler.Tools == 3, "successful MCP session initializes once, makes bounded calls and closes");
                Check(response.Headers.CacheControl.NoStore && body["comparables"] is JArray, "API report is structured and never cached");
                handler.Quota = true;
                var limited = await Post(Controller(), Named());
                Check((int)limited.StatusCode == 429 && handler.Closures == 2, "provider quota response is preserved and the MCP session still closes");
                handler.Quota = false; handler.Wait = true;
                using (var cancel = new CancellationTokenSource(40)) {
                    Check((await Post(Controller(), Named(), cancel.Token)).StatusCode == HttpStatusCode.GatewayTimeout && handler.Closures == 3, "cancelled appraisal returns timeout and closes its MCP session");
                }
            } finally { field.SetValue(null, original); }
        }
        foreach (var failure in new Exception[] {
            new HttpRequestException("private transport URL ?api_key=provider-secret", new SocketException((int)SocketError.ConnectionRefused)),
            new HttpRequestException("private transport URL ?api_key=provider-secret", new WebException("private connection detail", WebExceptionStatus.ConnectFailure))
        }) {
            var unavailable = new InitializationFailureHandler { Failure = failure };
            using (var client = new HttpClient(unavailable)) {
                field.SetValue(null, client);
                try {
                    var response = await Post(Controller(), Named());
                    var body = await response.Content.ReadAsStringAsync();
                    Check(response.StatusCode == HttpStatusCode.ServiceUnavailable && body.Contains("MarketCheck MCP service")
                        && body.Contains("Start the configured local MCP service"), "unreachable MCP initialization returns an actionable service-unavailable response");
                    Check(unavailable.Calls == 1 && !body.Contains("provider-secret") && !body.Contains("api_key")
                        && !body.Contains("private"), "missing MCP service makes no tool calls and exposes no transport details or credentials");
                } finally { field.SetValue(null, original); }
            }
        }
        foreach (var scenario in new[] {
            new InitializationFailureHandler { Failure = new HttpRequestException("generic transport error") },
            new InitializationFailureHandler { Status = HttpStatusCode.BadGateway, Body = "private upstream error" },
            new InitializationFailureHandler { Status = HttpStatusCode.OK, Body = "invalid MCP JSON" }
        }) {
            using (var client = new HttpClient(scenario)) {
                field.SetValue(null, client);
                try {
                    Check((await Post(Controller(), Named())).StatusCode == HttpStatusCode.BadGateway && scenario.Calls == 1,
                        "other initialization transport, HTTP and payload failures retain the bad-gateway response without retry");
                } finally { field.SetValue(null, original); }
            }
        }
        var cancelled = new InitializationFailureHandler { Failure = new OperationCanceledException("cancel fixture") };
        using (var client = new HttpClient(cancelled)) {
            field.SetValue(null, client);
            try {
                Check((await Post(Controller(), Named())).StatusCode == HttpStatusCode.GatewayTimeout && cancelled.Calls == 1,
                    "initialization cancellation keeps the gateway-timeout response");
            } finally { field.SetValue(null, original); }
        }
    }

    sealed class InitializationFailureHandler : HttpMessageHandler {
        public Exception Failure;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Body = "{}";
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
            Calls++;
            if (Failure != null) throw Failure;
            return Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Body) });
        }
    }

    sealed class McpHandler : HttpMessageHandler {
        public int Calls, Initializations, Closures, Tools;
        public bool Quota, Wait;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) {
            Calls++;
            if (!request.RequestUri.IsLoopback) throw new Exception("A fixture attempted to contact a non-loopback service.");
            if (request.Method == HttpMethod.Delete) { Closures++; return new HttpResponseMessage(HttpStatusCode.NoContent); }
            var message = JObject.Parse(await request.Content.ReadAsStringAsync());
            var method = (string)message["method"];
            if (method == "notifications/initialized") return new HttpResponseMessage(HttpStatusCode.Accepted);
            JObject result;
            if (method == "initialize") { Initializations++; result = new JObject { ["protocolVersion"] = "2025-06-18" }; }
            else {
                Tools++;
                Check(request.Headers.GetValues("MCP-Protocol-Version").Single() == "2025-06-18" && request.Headers.GetValues("Mcp-Session-Id").Single() == "fixture-session", "MCP calls carry negotiated protocol and session headers");
                if (Wait) await Task.Delay(Timeout.Infinite, token);
                var name = (string)message["params"]["name"];
                var arguments = (JObject)message["params"]["arguments"];
                JObject payload;
                if (Quota && name == "predict_price_with_comparables") payload = new JObject { ["status_code"] = 429 };
                else if (name == "predict_price_with_comparables") payload = new JObject { ["marketcheck_price"] = 25444, ["msrp"] = 32305 };
                else if (name == "decode_vin_neovin") payload = new JObject { ["vin"] = Subject, ["year"] = 2021, ["make"] = "Honda", ["model"] = "Accord", ["trim"] = "EX-L" };
                else payload = (int?)arguments["rows"] == 5 ? new JObject { ["listings"] = new JArray(Listing(Vin(1), 42000, 20000)) } : SearchData();
                result = new JObject { ["structuredContent"] = new JObject { ["result"] = payload } };
            }
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new JObject { ["jsonrpc"] = "2.0", ["id"] = message["id"], ["result"] = result }.ToString(), System.Text.Encoding.UTF8, "application/json") };
            if (method == "initialize") response.Headers.Add("Mcp-Session-Id", "fixture-session");
            return response;
        }
    }
}
