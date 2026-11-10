using autodealer.dev.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace autodealer.dev.Services {
    internal static class AuctionAppraisalService {
        public static async Task<JObject> GetReferenceCandidatesAsync(PriceResearchRequest scope, CancellationToken cancellationToken) {
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)) {
                deadline.CancelAfter(TimeSpan.FromSeconds(25));
                var mcp = new MarketCheckMcpClient();
                try {
                    await mcp.InitializeAsync(deadline.Token);
                    var vehicle = await DecodeAsync(mcp, scope, deadline.Token);
                    var search = VehicleSearch(vehicle, scope);
                    search["radius"] = 5000;
                    search["rows"] = 50;
                    search["sort_by"] = "dist";
                    search["sort_order"] = "asc";
                    search["include_dealer_object"] = true;
                    JObject current;
                    var radiusLimit = 5000;
                    try {
                        current = await mcp.CallAsync("search_active_cars", search, deadline.Token);
                    } catch (HttpRequestException) {
                        // Some subscriptions restrict active searches to 100 miles.
                        radiusLimit = 100;
                        search["radius"] = radiusLimit;
                        current = await mcp.CallAsync("search_active_cars", search, deadline.Token);
                    }
                    JObject recent = null;
                    try {
                        search["radius"] = Math.Min(scope.Radius, 100);
                        recent = await mcp.CallAsync("search_past_90_days", search, deadline.Token);
                    } catch (Exception ex) when (ex is HttpRequestException || ex is Newtonsoft.Json.JsonException
                        || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)) {
                        // Preserve current candidates if recent inventory is unavailable.
                    }
                    return new JObject {
                        ["vehicle"] = vehicle.VehicleTitle,
                        ["currentRadiusLimit"] = radiusLimit,
                        ["current"] = ReferenceListings(current),
                        ["recent"] = recent == null ? null : ReferenceListings(recent)
                    };
                } finally {
                    await mcp.CloseAsync();
                }
            }
        }

        private static JArray ReferenceListings(JObject result) {
            return new JArray((result["listings"] as JArray ?? new JArray()).OfType<JObject>().Select(row => {
                var listing = (JObject)row.DeepClone();
                listing["dealer_name"] = DealerLabel(row);
                return listing;
            }));
        }

        public static async Task<JObject> CountMarketInventoryAsync(PriceResearchRequest input, CancellationToken cancellationToken) {
            const int mileageWindow = 10000;
            var target = input.Miles.Value;
            var minimumMiles = Math.Max(0, target - mileageWindow);
            var maximumMiles = Math.Min(2000000, target + mileageWindow);
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)) {
                deadline.CancelAfter(TimeSpan.FromSeconds(30));
                var token = deadline.Token;
                var mcp = new MarketCheckMcpClient();
                try {
                    await mcp.InitializeAsync(token);
                    var vehicle = await DecodeAsync(mcp, input, token);
                    var search = VehicleSearch(vehicle, input);
                    search["miles_range"] = string.Format(CultureInfo.InvariantCulture, "{0}-{1}", minimumMiles, maximumMiles);
                    search["rows"] = 50;
                    search["sort_by"] = "dist";
                    search["include_dealer_object"] = true;
                    var result = await FindDealersAsync(mcp, "search_active_cars", search, token);
                    result["vehicle"] = vehicle.VehicleTitle;
                    result["zip"] = input.Zip;
                    result["radius"] = input.Radius;
                    result["minimumMiles"] = minimumMiles;
                    result["maximumMiles"] = maximumMiles;
                    return result;
                } finally {
                    await mcp.CloseAsync();
                }
            }
        }

        public static async Task<JObject> ListMarketAsync(PriceResearchRequest scope, JObject input, CancellationToken cancellationToken) {
            var minimumMiles = Math.Max(0, scope.Miles.Value - 10000);
            var maximumMiles = Math.Min(2000000, scope.Miles.Value + 10000);
            var recentRadius = Math.Min(scope.Radius, 100);
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)) {
                deadline.CancelAfter(TimeSpan.FromSeconds(30));
                var token = deadline.Token;
                var mcp = new MarketCheckMcpClient();
                try {
                    var search = string.IsNullOrEmpty(scope.Vin) ? NamedVehicle(input, scope) : null;
                    await mcp.InitializeAsync(token);
                    if (search == null) search = VehicleSearch(await DecodeAsync(mcp, scope, token), scope);
                    var title = string.Join(" ", new[] { (string)search["year"], (string)search["make"], (string)search["model"], (string)search["trim"] }
                        .Where(value => !string.IsNullOrWhiteSpace(value)));
                    search["miles_range"] = string.Format(CultureInfo.InvariantCulture, "{0}-{1}", minimumMiles, maximumMiles);
                    search["rows"] = 50;
                    search["sort_by"] = "dist";
                    search["include_dealer_object"] = true;
                    var current = await FindDealersAsync(mcp, "search_active_cars", search, token);
                    search["radius"] = recentRadius;
                    search["sort_by"] = "last_seen";
                    search["sort_order"] = "desc";
                    JObject recent = null;
                    try {
                        recent = await FindDealersAsync(mcp, "search_past_90_days", search, token);
                    } catch (Exception ex) when (ex is HttpRequestException || ex is Newtonsoft.Json.JsonException || ex is PriceResearchException
                        || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested)) {
                        // A recent-inventory failure does not discard the current search.
                    }
                    return JObject.FromObject(new {
                        vehicle = title, zip = scope.Zip, radius = scope.Radius, recentRadius,
                        minimumMiles, maximumMiles, current, recent
                    });
                } finally {
                    await mcp.CloseAsync();
                }
            }
        }

        private static JObject NamedVehicle(JObject input, PriceResearchRequest scope) {
            var year = input["year"];
            var make = input["make"]?.Type == JTokenType.String ? ((string)input["make"]).Trim() : "";
            var model = input["model"]?.Type == JTokenType.String ? ((string)input["model"]).Trim() : "";
            var trim = input["trim"]?.Type == JTokenType.String ? ((string)input["trim"]).Trim() : "";
            if (year?.Type != JTokenType.Integer || (long)year < 1886 || (long)year > DateTime.UtcNow.Year + 2
                || make.Length == 0 || make.Length > 80 || model.Length == 0 || model.Length > 80 || trim.Length > 120
                || (input["trim"] != null && input["trim"].Type != JTokenType.String))
                throw new ArgumentException("Enter a VIN or a valid vehicle year, make, and model.");

            return VehicleSearch(scope, (int)year, make, model, trim);
        }

        private static async Task<JObject> FindDealersAsync(MarketCheckMcpClient mcp, string tool, JObject search, CancellationToken token) {
            return FormatDealerResults(await mcp.CallAsync(tool, search, token));
        }

        private static JObject FormatDealerResults(JObject result) {
            var count = result["num_found"];
            if (count?.Type != JTokenType.Integer || (long)count < 0)
                throw new PriceResearchException(HttpStatusCode.BadGateway, "MarketCheck did not return a valid listing count.");

            var listings = result["listings"] as JArray ?? new JArray();
            var dealers = listings.OfType<JObject>()
                .Select(listing => new { label = DealerLabel(listing), listing })
                .Where(row => row.label != null)
                .GroupBy(row => row.label, StringComparer.OrdinalIgnoreCase)
                .Select(group => new {
                    label = group.Key,
                    listingCount = group.Count(),
                    listings = group.Select(row => {
                        var price = PriceResearchResult.Number(row.listing["price"]);
                        var miles = PriceResearchResult.Number(row.listing["miles"]);
                        return new { askingPriceUsd = price > 0 ? price : null, miles = miles >= 0 ? miles : null };
                    }).ToArray()
                })
                .ToArray();
            return JObject.FromObject(new { listingCount = (long)count, dealers });
        }

        private static string DealerLabel(JObject listing) {
            var dealer = listing["dealer"] as JObject;
            var name = ((string)dealer?["name"] ?? (string)listing["dealer_name"])?.Trim();
            if (string.IsNullOrWhiteSpace(name)) return null;

            var location = string.Join(", ", new[] { ((string)dealer?["city"])?.Trim(), ((string)dealer?["state"])?.Trim() }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            return location.Length == 0 ? name : name + " (" + location + ")";
        }

        public static async Task<JObject> GetMarketPriceAsync(PriceResearchRequest input, CancellationToken cancellationToken) {
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)) {
                deadline.CancelAfter(TimeSpan.FromSeconds(30));
                var research = await new PriceResearchService().ResearchAsync(input, deadline.Token);
                var estimate = RequireRetailEstimate(research.PredictedPrice);
                return JObject.FromObject(new { vin = input.Vin, miles = input.Miles.Value, zip = input.Zip, marketCheckRetailUsd = estimate });
            }
        }

        public static async Task<JObject> AppraiseAsync(PriceResearchRequest input, decimal? resaleUsd, decimal? hammerBidUsd, CancellationToken cancellationToken) {
            var bidCalculator = new AuctionBidService();
            var bid = new AuctionBidRequest { ExpectedSellingPriceUsd = resaleUsd ?? 1m, CurrentHammerBidUsd = hammerBidUsd };
            bid.Validate();

            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)) {
                deadline.CancelAfter(TimeSpan.FromSeconds(30));
                var token = deadline.Token;
                string title;
                decimal estimate;
                bool trimMatched;
                JArray comparables;
                try {
                    var mcp = new MarketCheckMcpClient();
                    try {
                        await mcp.InitializeAsync(token);
                        var vehicle = await DecodeAsync(mcp, input, token);
                        var search = VehicleSearch(vehicle, input);
                        title = vehicle.VehicleTitle;
                        trimMatched = search["trim"] != null;
                        estimate = await PredictAsync(mcp, input, token);
                        search["rows"] = 20;
                        search["sort_by"] = "dist";
                        search["sort_order"] = "asc";
                        comparables = await GetComparablesAsync(mcp, search, input, token);
                    } finally {
                        await mcp.CloseAsync();
                    }
                } catch (Exception ex) when (ex is HttpRequestException || ex is NeoVinException
                    || ex is Newtonsoft.Json.JsonException || ex is System.Configuration.ConfigurationErrorsException) {
                    // The Appraisal report uses MarketCheck directly and supplies its own comparable listings.
                    var research = await new PriceResearchService().ResearchAsync(input, token);
                    estimate = RequireRetailEstimate(research.PredictedPrice);
                    var listings = (research.Data["comparables"] as JObject)?["listings"] as JArray;
                    var subject = listings?.OfType<JObject>().FirstOrDefault(research.IsSubject);
                    title = subject == null ? input.Vin : string.Join(" ", new[] {
                        (string)subject["year"], (string)subject["make"], (string)subject["model"], (string)subject["trim"]
                    }.Where(value => !string.IsNullOrWhiteSpace(value)));
                    if (string.IsNullOrWhiteSpace(title)) title = input.Vin;
                    trimMatched = false;
                    comparables = FormatAppraisalComparables(listings, input);
                }

                bid.ExpectedSellingPriceUsd = resaleUsd ?? estimate;
                return JObject.FromObject(new {
                    vin = input.Vin, miles = input.Miles.Value, zip = input.Zip, radius = input.Radius,
                    vehicle = title, marketCheckRetailUsd = estimate, comparableSearchAvailable = comparables != null,
                    trimMatched, comparables, bid = bidCalculator.Calculate(bid)
                });
            }
        }

        private static async Task<NeoVinResult> DecodeAsync(MarketCheckMcpClient mcp, PriceResearchRequest input, CancellationToken token) {
            var decode = await mcp.CallAsync("decode_vin_neovin", new JObject { ["vin"] = input.Vin }, token);
            return NeoVinResult.Create(input.Vin, decode.ToString());
        }

        private static JObject VehicleSearch(NeoVinResult vehicle, PriceResearchRequest input) {
            var year = PriceResearchResult.Number(vehicle.Data["year"]);
            var make = (string)vehicle.Data["make"];
            var model = (string)vehicle.Data["model"];
            var trim = (string)vehicle.Data["trim"];
            if (!year.HasValue || year < 1886 || year > DateTime.UtcNow.Year + 2 || decimal.Truncate(year.Value) != year
                || string.IsNullOrWhiteSpace(make) || string.IsNullOrWhiteSpace(model))
                throw new HttpRequestException("MarketCheck did not identify the vehicle.");

            return VehicleSearch(input, (int)year.Value, make, model, trim);
        }

        private static JObject VehicleSearch(PriceResearchRequest input, int year, string make, string model, string trim) {
            var search = new JObject {
                ["year"] = year, ["make"] = make, ["model"] = model,
                ["zip"] = input.Zip, ["radius"] = input.Radius, ["car_type"] = "used"
            };
            if (!string.IsNullOrWhiteSpace(trim)) search["trim"] = trim;
            return search;
        }

        private static async Task<decimal> PredictAsync(MarketCheckMcpClient mcp, PriceResearchRequest input, CancellationToken token) {
            var prediction = await mcp.CallAsync("predict_price_with_comparables", new JObject {
                ["vin"] = input.Vin, ["miles"] = input.Miles.Value, ["zip"] = input.Zip, ["dealer_type"] = "independent"
            }, token);
            return RequireRetailEstimate(PriceResearchResult.Number(prediction["marketcheck_price"]));
        }

        private static decimal RequireRetailEstimate(decimal? estimate) {
            if (!estimate.HasValue || estimate <= 0 || estimate > 2000000m)
                throw new PriceResearchException(HttpStatusCode.BadGateway, "MarketCheck returned no usable retail estimate.");
            return estimate.Value;
        }

        private static async Task<JArray> GetComparablesAsync(MarketCheckMcpClient mcp, JObject search, PriceResearchRequest input, CancellationToken token) {
            try {
                var result = await mcp.CallAsync("search_active_cars", search, token);
                return FormatAppraisalComparables(result["listings"] as JArray, input);
            } catch (HttpRequestException) { return null; }
            catch (Newtonsoft.Json.JsonException) { return null; }
        }

        private static JArray FormatAppraisalComparables(JArray listings, PriceResearchRequest input) {
            if (listings == null) return null;
            return new JArray(listings.OfType<JObject>()
                .Where(row => {
                    var vin = (string)row["vin"];
                    var price = PriceResearchResult.Number(row["price"]);
                    var distance = PriceResearchResult.Distance(row);
                    return vin != null && Regex.IsMatch(vin, "^[A-HJ-NPR-Z0-9]{17}$", RegexOptions.IgnoreCase)
                        && !vin.Equals(input.Vin, StringComparison.OrdinalIgnoreCase)
                        && price > 0 && price <= 2000000m
                        && distance >= 0 && distance <= input.Radius;
                })
                .GroupBy(row => (string)row["vin"], StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Select(row => JObject.FromObject(new {
                    askingPriceUsd = PriceResearchResult.Number(row["price"]),
                    miles = PriceResearchResult.Number(row["miles"]),
                    distanceMiles = PriceResearchResult.Distance(row)
                }))
                .OrderBy(row => {
                    var miles = (decimal?)row["miles"];
                    return miles >= 0 && miles <= 2000000m ? Math.Abs(miles.Value - input.Miles.Value) : decimal.MaxValue;
                }));
        }
    }
}
