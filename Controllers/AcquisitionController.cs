using autodealer.dev.Services;
using autodealer.dev.Models;
using Newtonsoft.Json.Linq;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;

namespace autodealer.dev.Controllers {
    [RoutePrefix("api/service/acquisition")]
    public sealed class AcquisitionController : ApiController {
        /// <summary>Uses caller-observed comparables without fetching arbitrary URLs or requiring a market-data subscription.</summary>
        [HttpPost, Route("{vin}/html")]
        public async Task<IHttpActionResult> FromComparables(string vin, [FromBody] AcquisitionComparableRequest input, CancellationToken cancellationToken) {
            if (!string.Equals(ConfigurationManager.AppSettings["ApiSecurity:Enabled"], "true", StringComparison.OrdinalIgnoreCase))
                return Content(HttpStatusCode.ServiceUnavailable, new { message = "API authentication must be enabled for acquisition research." });
            var normalizedVin = (vin ?? "").Trim().ToUpperInvariant();
            if (!Regex.IsMatch(normalizedVin, "^[A-HJ-NPR-Z0-9]{17}$")) return BadRequest("A valid 17-character VIN is required.");
            if (input == null || input.Comparables == null || input.Comparables.Count == 0 || input.Comparables.Count > 30 || input.Comparables.Any(x => x == null) || !ModelState.IsValid) return BadRequest("Supply mileage and 1–30 comparable listings with valid prices, dates, distances, dealer details and HTTP(S) listing links.");
            var urls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var vins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var listings = new JArray();
            foreach (var item in input.Comparables) {
                Uri link;
                if (!Uri.TryCreate(item.ListingUrl, UriKind.Absolute, out link) || (link.Scheme != "https" && link.Scheme != "http") || !string.IsNullOrEmpty(link.UserInfo))
                    return BadRequest("Every listing must have an absolute HTTP(S) URL without embedded credentials.");
                if (!item.ObservedAtUtc.HasValue || item.ObservedAtUtc.Value.ToUniversalTime() > DateTime.UtcNow.AddMinutes(5) || item.ObservedAtUtc.Value.ToUniversalTime() < DateTime.UtcNow.AddDays(-30))
                    return BadRequest("Each listing must have been observed in the past 30 days; use an ISO-8601 UTC timestamp.");
                var listingKey = link.GetLeftPart(UriPartial.Path).TrimEnd('/') + link.Query;
                if (urls.Contains(listingKey) || (!string.IsNullOrWhiteSpace(item.Vin) && vins.Contains(item.Vin)) || string.Equals(item.Vin, normalizedVin, StringComparison.OrdinalIgnoreCase)) continue;
                urls.Add(listingKey); if (!string.IsNullOrWhiteSpace(item.Vin)) vins.Add(item.Vin);
                listings.Add(new JObject {
                    ["dealer"] = item.Dealer, ["listingVin"] = item.Vin ?? "", ["location"] = item.Location,
                    ["distance"] = item.DistanceMiles.ToString(CultureInfo.InvariantCulture) + " miles",
                    ["year"] = item.Year.HasValue ? (JToken)new JValue(item.Year.Value) : JValue.CreateNull(),
                    ["make"] = item.Make ?? "", ["model"] = item.Model ?? "", ["seriesTrim"] = item.SeriesTrim ?? "", ["priceUsd"] = item.AskingPrice,
                    ["vehicle"] = item.Vehicle, ["mileage"] = item.Mileage.ToString(CultureInfo.InvariantCulture),
                    ["askingPrice"] = item.AskingPrice.ToString(CultureInfo.InvariantCulture),
                    ["titleAndPriceConditions"] = (item.TitleStatus == "clean" ? "Advertised clean title" : item.TitleStatus) + "; caller-observed " + item.ObservedAtUtc.Value.ToUniversalTime().ToString("yyyy-MM-dd") + "; " + (item.PriceConditions ?? "Conditions and fees unverified"),
                    ["url"] = item.ListingUrl
                });
            }
            if (listings.Count == 0) return BadRequest("No distinct comparable listings remain after removing duplicates and the subject vehicle.");
            try {
                var vehicle = await AcquisitionReportService.DecodeAsync(normalizedVin, cancellationToken);
                var report = new JObject {
                    ["recommendation"] = "PASS", ["summary"] = "Caller-supplied comparables provide an indicative asking-price range. PASS pending verification of vehicle condition, title, accident history and auction costs. No auction bid is supported by asking prices alone.",
                    ["underwriting"] = input.Underwriting, ["comparables"] = listings, ["sections"] = new JArray(new JObject {
                        ["heading"] = "Auction bid and missing information", ["paragraphs"] = new JArray(
                            "Desired profit: " + input.DesiredProfit.ToString("C0", CultureInfo.GetCultureInfo("en-US")) + ". Maximum bid B = expected selling price − fixed acquisition and selling costs − bid-dependent auction fee F(B) − desired profit.",
                            "Negotiated selling price, quick-sale price, repairs, transport, inspection, detailing, acquisition tax/title, holding and selling expenses require explicit estimates or quotes. They have not been invented from listing prices. Verify all inputs before bidding.")
                    })
                };
                var evidence = new JObject { ["output"] = new JArray(new JObject { ["sources"] = new JArray(listings.Select(x => new JObject { ["url"] = (string)x["url"] })) }) };
                var article = AcquisitionReportDocument.ToArticle(report, evidence, input.Mileage, vehicle, true, input.DesiredProfit);
                return HtmlResponse(AcquisitionReportService.Render(article, normalizedVin, input.Mileage));
            }
            catch (OperationCanceledException) {
                if (cancellationToken.IsCancellationRequested) throw;
                return Content(HttpStatusCode.GatewayTimeout, new { message = "VIN lookup timed out. Please retry." });
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is IOException || ex is Newtonsoft.Json.JsonException) {
                return Content(HttpStatusCode.BadGateway, new { message = "VIN data is unavailable. Please retry." });
            }
        }

        private IHttpActionResult HtmlResponse(string html) {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(html, Encoding.UTF8, "text/html") };
            response.Headers.CacheControl = new CacheControlHeaderValue { NoStore = true };
            response.Headers.TryAddWithoutValidation("Referrer-Policy", "no-referrer");
            response.Headers.TryAddWithoutValidation("X-Content-Type-Options", "nosniff");
            response.Headers.TryAddWithoutValidation("Content-Security-Policy", "default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'; frame-ancestors 'self'");
            return ResponseMessage(response);
        }

        [HttpGet, Route("{vin}/html")]
        public async Task<IHttpActionResult> GetHtml(string vin, int mileage, CancellationToken cancellationToken, decimal desiredProfit = 3000m) {
            if (!string.Equals(ConfigurationManager.AppSettings["ApiSecurity:Enabled"], "true", StringComparison.OrdinalIgnoreCase))
                return Content(HttpStatusCode.ServiceUnavailable, new { message = "API authentication must be enabled for acquisition research." });
            var normalizedVin = (vin ?? "").Trim().ToUpperInvariant();
            if (!Regex.IsMatch(normalizedVin, "^[A-HJ-NPR-Z0-9]{17}$")) return BadRequest("A valid 17-character VIN is required.");
            if (mileage < 0 || mileage > 2000000) return BadRequest("Mileage must be between 0 and 2,000,000 miles.");
            if (desiredProfit < 0 || desiredProfit > 1000000) return BadRequest("Desired profit must be between 0 and 1,000,000 USD.");
            try {
                var html = await new AcquisitionReportService().GenerateAsync(normalizedVin, mileage, desiredProfit, cancellationToken);
                return HtmlResponse(html);
            }
            catch (OperationCanceledException) {
                if (cancellationToken.IsCancellationRequested) throw;
                return Content(HttpStatusCode.GatewayTimeout, new { message = "Acquisition research timed out. Please retry." });
            }
            catch (AcquisitionResearchException ex) {
                return Content(HttpStatusCode.BadGateway, new { code = ex.Code, message = ex.Message });
            }
            catch (ConfigurationErrorsException) {
                return Content(HttpStatusCode.ServiceUnavailable, new { message = "Configure the GTX OpenAI settings on this deployment." });
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is IOException || ex is System.Xml.XmlException || ex is Newtonsoft.Json.JsonException) {
                Trace.TraceWarning("Acquisition research failed: {0}", ex.GetType().Name);
                return Content(HttpStatusCode.BadGateway, new { message = "Acquisition research could not produce a complete report. Please retry." });
            }
        }
    }
}
