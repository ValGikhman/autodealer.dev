using autodealer.dev.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
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

        public static Task<JObject> MarketAppraisalAsync(PriceResearchRequest scope, JObject input, CancellationToken cancellationToken) {
            return MarketAppraisalAsync(scope, input, cancellationToken, null);
        }

        internal static async Task<JObject> MarketAppraisalAsync(PriceResearchRequest scope, JObject input,
            CancellationToken cancellationToken, Func<string, JObject, CancellationToken, Task<JObject>> call) {
            if (scope == null || input == null) throw new ArgumentException("Enter a VIN or vehicle year, make and model.");
            var countToken = input["comparableCount"];
            if (countToken != null && (countToken.Type != JTokenType.Integer || (long)countToken < 1 || (long)countToken > 10))
                throw new ArgumentException("Choose between one and ten comparables.");
            var count = countToken == null ? 10 : (int)countToken;
            var reference = string.IsNullOrWhiteSpace(scope.Vin);
            if (reference) scope.NormalizeSearchScope(); else scope.NormalizeAndValidate();
            var namedSearch = reference ? NamedVehicle(input, scope) : null;

            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)) {
                deadline.CancelAfter(TimeSpan.FromSeconds(30));
                if (call != null) return await MarketAppraisalCoreAsync(scope, namedSearch, count, call, deadline.Token);
                var mcp = new MarketCheckMcpClient();
                try {
                    await mcp.InitializeAsync(deadline.Token);
                    return await MarketAppraisalCoreAsync(scope, namedSearch, count, mcp.CallAsync, deadline.Token);
                } finally { await mcp.CloseAsync(); }
            }
        }

        private static async Task<JObject> MarketAppraisalCoreAsync(PriceResearchRequest scope, JObject namedSearch, int count,
            Func<string, JObject, CancellationToken, Task<JObject>> call, CancellationToken token) {
            var reference = namedSearch != null;
            string vin;
            JObject search;
            if (reference) {
                search = (JObject)namedSearch.DeepClone();
                var seedSearch = (JObject)search.DeepClone();
                seedSearch["rows"] = 5;
                seedSearch["include_build_object"] = true;
                var candidates = await call("search_active_cars", seedSearch, token);
                var seed = (candidates["listings"] as JArray ?? new JArray()).OfType<JObject>().Take(5)
                    .FirstOrDefault(row => ValidVin(StringValue(row["vin"], 17)) && MatchesVehicle(row, search));
                if (seed == null)
                    throw new PriceResearchException(HttpStatusCode.NotFound, "No matching vehicle with a usable reference VIN was found.");
                vin = StringValue(seed["vin"], 17).ToUpperInvariant();
            } else {
                vin = scope.Vin;
                var decoded = await call("decode_vin_neovin", new JObject { ["vin"] = vin }, token);
                search = VehicleSearch(NeoVinResult.Create(vin, decoded.ToString()), scope, invariantNumbers: true);
            }

            var title = string.Join(" ", new[] { (string)search["year"], (string)search["make"], (string)search["model"], (string)search["trim"] }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
            search["rows"] = 50;
            search["miles_range"] = string.Format(CultureInfo.InvariantCulture, "{0}-{1}",
                Math.Max(0, scope.Miles.Value - 10000), Math.Min(2000000, scope.Miles.Value + 10000));
            search["sort_by"] = "dist";
            search["sort_order"] = "asc";
            search["include_dealer_object"] = true;

            // The official MCP tool strips build data by default. Vehicle validation
            // requires those returned specifications for both seed and comparable rows.
            search["include_build_object"] = true;

            var predictionTask = call("predict_price_with_comparables", new JObject {
                ["vin"] = vin, ["miles"] = scope.Miles.Value, ["zip"] = scope.Zip, ["dealer_type"] = "independent"
            }, token);
            var listingsTask = call("search_active_cars", search, token);
            await Task.WhenAll(predictionTask, listingsTask);
            var prediction = await predictionTask;
            var market = await listingsTask;
            var estimate = RequireRetailEstimate(AppraisalNumber(prediction["marketcheck_price"]));
            var matched = market["num_found"];
            if (matched?.Type != JTokenType.Integer || (long)matched < 0 || !(market["listings"] is JArray))
                throw new PriceResearchException(HttpStatusCode.BadGateway, "MarketCheck did not return a valid comparable search.");

            var sample = ((JArray)market["listings"]).OfType<JObject>().Take(50)
                .Where(row => ValidVin(StringValue(row["vin"], 17)) && MatchesVehicle(row, search)
                    && (reference || !string.Equals(StringValue(row["vin"], 17), vin, StringComparison.OrdinalIgnoreCase))
                    && InRange(AppraisalNumber(row["dist"]), 0, scope.Radius)
                    && InRange(AppraisalNumber(row["miles"]), Math.Max(0, scope.Miles.Value - 10000), Math.Min(2000000, scope.Miles.Value + 10000)))
                .GroupBy(row => StringValue(row["vin"], 17), StringComparer.OrdinalIgnoreCase).Select(group => group.First())
                .Select(row => AppraisalListing(row, title))
                .OrderBy(row => Math.Abs((decimal)row["miles"] - scope.Miles.Value))
                .ThenBy(row => (decimal)row["distanceMiles"]).ToList();
            return new JObject {
                ["vehicle"] = title, ["vin"] = vin, ["usedReferenceVin"] = reference,
                ["miles"] = scope.Miles.Value, ["zip"] = scope.Zip, ["radius"] = scope.Radius,
                ["marketCheckRetailUsd"] = estimate,
                ["msrpUsd"] = OptionalNumber(prediction["msrp"] ?? prediction["original_msrp"], 1, 2000000),
                ["listingCount"] = (long)matched,
                ["statistics"] = SampleStatistics(sample),
                ["comparables"] = new JArray(sample.Take(count))
            };
        }

        private static JObject AppraisalListing(JObject row, string title) {
            var dealer = row["dealer"] as JObject;
            var url = SafeListingUrl(row["vdp_url"]);
            return new JObject {
                ["vin"] = StringValue(row["vin"], 17).ToUpperInvariant(), ["vehicle"] = title,
                ["miles"] = AppraisalNumber(row["miles"]),
                ["askingPriceUsd"] = OptionalNumber(row["price"], 1, 2000000),
                ["daysListed"] = OptionalNumber(row["dom"] ?? row["days_on_market"] ?? row["dos_active"], 0, 100000),
                ["dealerName"] = StringValue(dealer?["name"] ?? row["dealer_name"], 200),
                ["city"] = StringValue(dealer?["city"] ?? row["city"], 80),
                ["state"] = StringValue(dealer?["state"] ?? row["state"], 40),
                ["distanceMiles"] = AppraisalNumber(row["dist"]), ["url"] = url == null ? JValue.CreateNull() : new JValue(url)
            };
        }

        private static JObject SampleStatistics(IList<JObject> sample) {
            var prices = sample.Select(row => (decimal?)row["askingPriceUsd"]).Where(value => value.HasValue).Select(value => value.Value).OrderBy(value => value).ToList();
            var miles = sample.Select(row => (decimal)row["miles"]).OrderBy(value => value).ToList();
            var days = sample.Select(row => (decimal?)row["daysListed"]).Where(value => value.HasValue).Select(value => value.Value).OrderBy(value => value).ToList();
            return JObject.FromObject(new {
                sampledListingCount = sample.Count, pricedSampleCount = prices.Count,
                minPriceUsd = prices.Count == 0 ? (decimal?)null : prices.First(),
                maxPriceUsd = prices.Count == 0 ? (decimal?)null : prices.Last(),
                meanPriceUsd = prices.Count == 0 ? (decimal?)null : prices.Average(), medianPriceUsd = Percentile(prices, .5m),
                p25PriceUsd = Percentile(prices, .25m), p75PriceUsd = Percentile(prices, .75m),
                meanMiles = miles.Count == 0 ? (decimal?)null : miles.Average(), medianMiles = Percentile(miles, .5m),
                medianDaysListed = Percentile(days, .5m)
            });
        }

        private static decimal? Percentile(IList<decimal> sorted, decimal fraction) {
            if (sorted.Count == 0) return null;
            var position = (sorted.Count - 1) * fraction;
            var lower = (int)decimal.Floor(position);
            var upper = (int)decimal.Ceiling(position);
            return sorted[lower] + (sorted[upper] - sorted[lower]) * (position - lower);
        }

        private static bool MatchesVehicle(JObject row, JObject search) {
            var build = row["build"] as JObject;
            if (AppraisalNumber(build?["year"] ?? row["year"]) != (decimal?)search["year"]) return false;
            foreach (var key in new[] { "make", "model", "trim" }) {
                var expected = StringValue(search[key], 120);
                if (expected != null && !string.Equals(expected, StringValue(build?[key] ?? row[key], 120), StringComparison.OrdinalIgnoreCase)) return false;
            }
            return true;
        }

        private static bool ValidVin(string vin) => vin != null && Regex.IsMatch(vin, "^[A-HJ-NPR-Z0-9]{17}$", RegexOptions.IgnoreCase);
        private static bool InRange(decimal? value, decimal minimum, decimal maximum) => value.HasValue && value >= minimum && value <= maximum;
        private static decimal? AppraisalNumber(JToken token) {
            if (token == null || (token.Type != JTokenType.String && token.Type != JTokenType.Integer && token.Type != JTokenType.Float)) return null;
            // JValue.ToString() uses the request culture for typed numbers. Read the
            // scalar with invariant culture before parsing so 9.5 never becomes 95.
            var text = token.Type == JTokenType.String ? (string)token : Convert.ToString(((JValue)token).Value, CultureInfo.InvariantCulture);
            decimal number;
            return decimal.TryParse(text, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out number) ? (decimal?)number : null;
        }
        private static JToken OptionalNumber(JToken value, decimal minimum, decimal maximum) {
            var number = AppraisalNumber(value);
            return InRange(number, minimum, maximum) ? new JValue(number.Value) : JValue.CreateNull();
        }
        private static string StringValue(JToken value, int maximum) {
            if (value?.Type != JTokenType.String) return null;
            var text = ((string)value).Trim();
            return text.Length == 0 || text.Length > maximum ? null : text;
        }
        private static string SafeListingUrl(JToken token) {
            var text = StringValue(token, 2048);
            if (!Uri.TryCreate(text, UriKind.Absolute, out var url) || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp)
                || !string.IsNullOrEmpty(url.UserInfo) || Regex.IsMatch(url.Query, @"(?:^|[?&])(?:api_key|access_token|token|key)=", RegexOptions.IgnoreCase)) return null;
            return url.AbsoluteUri;
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

        private static JObject VehicleSearch(NeoVinResult vehicle, PriceResearchRequest input, bool invariantNumbers = false) {
            var year = invariantNumbers ? AppraisalNumber(vehicle.Data["year"]) : PriceResearchResult.Number(vehicle.Data["year"]);
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
