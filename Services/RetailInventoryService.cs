using autodealer.dev.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Configuration;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace autodealer.dev.Services {
    internal static class RetailInventoryService {
        private static readonly HttpClient Http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) {
            Timeout = TimeSpan.FromSeconds(20),
            MaxResponseContentBufferSize = 2 * 1024 * 1024
        };
        private static readonly string[] Filters = {
            "make", "model", "year", "trim", "drivetrain", "miles_range", "price_range", "zip", "radius"
        };

        public static async Task<JObject> SearchAsync(JObject input, CancellationToken token) {
            if (input == null || !ValidText(input["make"]) || !ValidText(input["model"]))
                throw new PriceResearchException(HttpStatusCode.BadRequest, "Enter a vehicle make and model.");

            var source = input["source"]?.Type == JTokenType.String ? (string)input["source"] : null;
            if (input["source"] != null && source != "carmax.com" && source != "carvana.com" && source != "cars.com")
                throw new PriceResearchException(HttpStatusCode.BadRequest, "Choose a supported retail source.");

            var query = "append_api_key=false&car_type=used&rows=10";
            foreach (var filter in Filters) {
                var value = input[filter];
                if (value == null) continue;
                if (!ValidText(value) && value.Type != JTokenType.Integer)
                    throw new PriceResearchException(HttpStatusCode.BadRequest, "Enter valid retail search filters.");
                query += "&" + filter + "=" + Uri.EscapeDataString(value.ToString());
            }
            if (source != null) query += "&source=" + source;
            if (input["zip"] != null) query += "&sort_by=dist";

            var key = ConfigurationManager.AppSettings["MarketCheck:ApiKey"];
            if (string.IsNullOrWhiteSpace(key)) key = Environment.GetEnvironmentVariable("MARKETCHECK_API_KEY");
            if (string.IsNullOrWhiteSpace(key))
                throw new PriceResearchException(HttpStatusCode.ServiceUnavailable, "MarketCheck retail search is not configured.");
            key = key.Trim();

            var path = source == null ? "search/car/active" : "dealerships/inventory";
            using (var request = new HttpRequestMessage(HttpMethod.Get,
                "https://api.marketcheck.com/v2/" + path + "?" + query + "&api_key=" + Uri.EscapeDataString(key)))
            using (var response = await Http.SendAsync(request, token)) {
                MarketCheckQuota.Capture(response.Headers);
                if ((int)response.StatusCode == 429)
                    throw new PriceResearchException((HttpStatusCode)429, "MarketCheck request limit reached.");
                if (!response.IsSuccessStatusCode)
                    throw new PriceResearchException(HttpStatusCode.BadGateway, "MarketCheck could not complete the retail search.");

                var raw = await response.Content.ReadAsStringAsync();
                var data = JObject.Parse(raw.Replace(key, "[redacted]").Replace(Uri.EscapeDataString(key), "[redacted]"));
                var listings = data["listings"] as JArray;
                if (listings == null)
                    throw new PriceResearchException(HttpStatusCode.BadGateway, "MarketCheck returned an unreadable retail search response.");

                return new JObject { ["listings"] = new JArray(listings.OfType<JObject>().Select(row => new JObject {
                    ["vin"] = row["vin"], ["heading"] = row["heading"], ["vdp_url"] = row["vdp_url"],
                    ["price"] = row["price"], ["miles"] = row["miles"], ["dist"] = row["dist"],
                    ["build"] = Pick(row["build"] as JObject, "year", "make", "model", "trim"),
                    ["dealer"] = Pick(row["dealer"] as JObject, "name", "city", "state")
                })) };
            }
        }

        private static bool ValidText(JToken value) => value?.Type == JTokenType.String
            && !string.IsNullOrWhiteSpace((string)value) && ((string)value).Length <= 80;

        private static JObject Pick(JObject row, params string[] names) =>
            row == null ? null : new JObject(names.Select(name => new JProperty(name, row[name])));
    }
}
