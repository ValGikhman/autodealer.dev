using autodealer.dev.Controllers;
using autodealer.dev.Models;
using autodealer.dev.Services;
using Newtonsoft.Json.Linq;
using Services;
using System;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Dispatcher;

internal static class VinReportRegression {
    private const string Vin = "1HGCM82633A004352";
    private const string AccountKey = "ad_reportfixture.account-secret";
    private const string Xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<decoded_data><decoder_messages><service_provider>Fixture provider</service_provider></decoder_messages><query_responses><query_response identifier=\"1HGCM82633A004352\"><us_market_data><us_styles><style id=\"fixture\" name=\"LE\"><basic_data><year>2003</year><make>Test &amp; Co</make><model>Fixture</model></basic_data><extra><![CDATA[<original> retained]]></extra></style></us_styles></us_market_data></query_response></query_responses></decoded_data>";

    private static int Main() {
        try { Run().GetAwaiter().GetResult(); return 0; }
        catch (Exception ex) { Console.Error.WriteLine(ex.GetBaseException().Message); return 1; }
    }

    private static async Task Run() {
        var decoder = new Decoder();
        var access = new Access();
        using (var configuration = new HttpConfiguration()) {
            configuration.Services.Replace(typeof(IHttpControllerActivator), new Activator(decoder));
            configuration.MessageHandlers.Add(new ApiKeyHandler(access, request => null, request => null));
            configuration.MapHttpAttributeRoutes();
            configuration.EnsureInitialized();
            using (var server = new HttpServer(configuration))
            using (var client = new HttpClient(server)) {
                using (var response = await client.GetAsync(Url("report")))
                    Check(response.StatusCode == HttpStatusCode.Unauthorized && decoder.Calls == 0, "report requires API authentication before provider access");

                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AccountKey);
                access.Code = "scope_denied";
                using (var response = await client.GetAsync(Url("report")))
                    Check(response.StatusCode == HttpStatusCode.Forbidden && decoder.Calls == 0, "report preserves vin:read access denial before provider access");
                access.Code = "ok";
                var completed = access.Completes;
                using (var response = await client.GetAsync(Url("report", Vin.ToLowerInvariant()))) {
                    var text = await response.Content.ReadAsStringAsync();
                    var report = JObject.Parse(text);
                    Check(response.StatusCode == HttpStatusCode.OK && decoder.Calls == 1 && decoder.LastVin == Vin, "one report request makes exactly one provider decode with a normalized VIN");
                    Check((string)report["vin"] == Vin && (string)report["rawXml"] == Xml, "report preserves the complete original XML string including declaration, whitespace and CDATA");
                    Check((string)report["html"] == VinDecodeHtmlRenderer.Render(Xml, Vin), "report HTML matches the existing renderer without another provider request");
                    var timestamp = report["decodedAtUtc"].Value<DateTime>();
                    Check(timestamp.Kind == DateTimeKind.Utc && (DateTime.UtcNow - timestamp).Duration() < TimeSpan.FromMinutes(1), "report carries the UTC decode timestamp");
                    Check(response.Content.Headers.ContentType.MediaType == "application/json" && response.Headers.CacheControl.NoStore, "report is JSON and cannot be stored in HTTP caches");
                    Check(!text.Contains(AccountKey) && access.Last.RequiredScope == "vin:read" && access.Last.Endpoint.EndsWith("/report")
                        && access.Completes == completed + 1 && response.Headers.Contains("X-Request-Id"), "report keeps ordinary API metering and never returns its caller credential");
                }

                var calls = decoder.Calls;
                foreach (var invalid in new[] { "SHORT", "1HGCM82633A00435I", "1HGCM82633A00435!" }) {
                    using (var response = await client.GetAsync(Url("report", invalid))) {
                        Check(response.StatusCode == HttpStatusCode.BadRequest && decoder.Calls == calls, "invalid report VIN is rejected before any provider decode");
                        Check(response.Content.Headers.ContentType.MediaType == "application/json" && response.Headers.CacheControl.NoStore, "invalid report VIN uses JSON errors and no-store");
                    }
                }

                // The legacy JSON converter expects XML without its declaration.
                // The report assertion above deliberately retains the full original string.
                decoder.Xml = Xml.Substring(Xml.IndexOf("<decoded_data>", StringComparison.Ordinal));
                foreach (var format in new[] { "html", "json", "xml" }) {
                    using (var response = await client.GetAsync(Url(format))) {
                        var body = await response.Content.ReadAsStringAsync();
                        Check(response.StatusCode == HttpStatusCode.OK, "existing " + format + " endpoint remains available");
                        if (format == "html") Check(body == VinDecodeHtmlRenderer.Render(Xml, Vin), "existing HTML output remains unchanged");
                        if (format == "json") Check(JObject.Parse(body)["data"] != null && JObject.Parse(body)["rawXml"] == null, "existing JSON response keeps its original shape");
                        if (format == "xml") Check(System.Xml.Linq.XDocument.Parse(body).Root.Name.LocalName == "vin_decode_response", "existing XML response keeps its original wrapper");
                    }
                }

                decoder.Xml = "<decoded_data><decoder_messages><decoder_errors><error><code>FAIL</code><message>Fixture decoder rejection</message></error></decoder_errors></decoder_messages></decoded_data>";
                using (var response = await client.GetAsync(Url("report"))) {
                    var failure = JObject.Parse(await response.Content.ReadAsStringAsync());
                    Check((int)response.StatusCode == 422 && failure["message"] != null && failure["rawXml"] == null && response.Headers.CacheControl.NoStore, "decoder errors do not become saveable reports");
                }
                decoder.Xml = "<invalid";
                using (var response = await client.GetAsync(Url("report")))
                    Check((int)response.StatusCode == 422 && response.Content.Headers.ContentType.MediaType == "application/json", "malformed provider XML returns a JSON decoder error");
                decoder.Throws = true;
                using (var response = await client.GetAsync(Url("report"))) {
                    var body = await response.Content.ReadAsStringAsync();
                    Check(response.StatusCode == HttpStatusCode.BadGateway && !body.Contains("private fixture failure") && response.Headers.CacheControl.NoStore, "provider failures return sanitized JSON without a saved payload");
                }
                decoder.Throws = false;
                decoder.Xml = Xml;

                var demoKey = ConfigurationManager.AppSettings["DemoApi:Key"];
                Check(!string.IsNullOrWhiteSpace(demoKey), "offline report fixture supplies its own demo credential");
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", demoKey);
                var begins = access.Begins;
                calls = decoder.Calls;
                for (var index = 0; index < 6; index++) {
                    using (var response = await client.GetAsync(Url("report", Vin.Substring(0, 16) + index))) {
                        Check(response.StatusCode == (index < 5 ? HttpStatusCode.OK : (HttpStatusCode)429), "report preserves the demo distinct-VIN quota at slot " + (index + 1));
                        Check(response.Headers.GetValues("X-Demo-Limit").Single() == "5" && response.Headers.CacheControl.NoStore, "report preserves demo limit headers and no-store");
                    }
                }
                Check(decoder.Calls == calls + 5 && access.Begins == begins, "rejected demo report consumes neither provider calls nor registered-key metering");
                using (var response = await client.GetAsync(Url("report", Vin.Substring(0, 16) + "0")))
                    Check(response.StatusCode == HttpStatusCode.OK, "report can repeat a VIN already allowed by the demo quota");
            }
        }
    }

    private static string Url(string format, string vin = Vin) { return "http://localhost/api/service/vin/" + Uri.EscapeDataString(vin) + "/" + format; }
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS " + message); }
    private sealed class Decoder : IVinDecoderService {
        public int Calls; public string LastVin; public string Xml = VinReportRegression.Xml; public bool Throws;
        public string DecodeVin(string vin, string accessKey, string secretKey) { Calls++; LastVin = vin; if (Throws) throw new InvalidOperationException("private fixture failure"); return Xml; }
        public void DeleteDataOneDetails(string stock) { throw new InvalidOperationException("No fixture database access is allowed."); }
    }
    private sealed class Access : IApiAccessService {
        public string Code = "ok"; public int Begins, Completes; public ApiAccessRequest Last;
        public ApiAccessResult BeginRequest(ApiAccessRequest request) { Begins++; Last = request; return new ApiAccessResult { ResultCode = Code, RequestId = Guid.NewGuid(), ClientNumber = "report-fixture", Scopes = "vin:read", MonthlyQuota = 100, MonthlyUsage = 1 }; }
        public void CompleteRequest(Guid requestId, int statusCode, long durationMs) { Completes++; }
    }
    private sealed class Activator : IHttpControllerActivator {
        private readonly Decoder decoder;
        public Activator(Decoder decoder) { this.decoder = decoder; }
        public IHttpController Create(HttpRequestMessage request, HttpControllerDescriptor descriptor, Type controllerType) {
            if (controllerType != typeof(DecoderController)) throw new InvalidOperationException("Unexpected fixture controller.");
            return new DecoderController(decoder);
        }
    }
}
