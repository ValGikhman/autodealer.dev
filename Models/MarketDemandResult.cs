using System;
using Newtonsoft.Json.Linq;

namespace autodealer.dev.Models {
    public sealed class MarketDemandResult {
        public string Status { get; set; }
        public decimal? DaysSupply { get; set; }
        public int? ActiveCount { get; set; }
        public int? Sales45Days { get; set; }
        public DateTime CheckedAtUtc { get; set; }
        public string Message { get; set; }
        public static MarketDemandResult Unavailable(string message) {
            return new MarketDemandResult { Status = "unavailable", Message = message, CheckedAtUtc = DateTime.UtcNow };
        }
        public static MarketDemandResult Parse(string json) {
            var data = JObject.Parse(json);
            var active = Count(data["total_active_cars_for_ymmt"]);
            var sold = Count(data["total_cars_sold_in_last_45_days"]);
            var days = PriceResearchResult.Number(data["mds"]);
            if (!active.HasValue || !sold.HasValue || (days.HasValue && days < 0) || (sold > 0 && !days.HasValue))
                throw new FormatException("Invalid market demand response.");
            return new MarketDemandResult {
                Status = sold == 0 ? "insufficient_data" : "ready", DaysSupply = sold == 0 ? null : days,
                ActiveCount = active, Sales45Days = sold, CheckedAtUtc = DateTime.UtcNow,
                Message = sold == 0 ? "No recent sales activity was observed for this exact build in this area; days supply cannot be calculated." : "Market supply at the recent sales pace; this is not a prediction of your vehicle's selling time."
            };
        }
        private static int? Count(JToken token) {
            var value = PriceResearchResult.Number(token);
            return value.HasValue && value >= 0 && value <= int.MaxValue && decimal.Truncate(value.Value) == value ? (int?)value.Value : null;
        }
    }
}
