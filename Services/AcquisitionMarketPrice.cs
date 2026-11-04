using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace autodealer.dev.Services {
    public static class AcquisitionMarketPrice {
        private static bool Number(string value, string unit, out decimal number) {
            var text = (value ?? "").Trim();
            text = Regex.Replace(text, @"\s*(USD|miles|mi)\s*$", "", RegexOptions.IgnoreCase).Trim();
            if (!Regex.IsMatch(text, @"^\$?\d[\d,]*(\.\d{1,2})?$")) { number = 0; return false; }
            return decimal.TryParse(text.TrimStart('$').Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out number);
        }
        public static bool TryPrice(JObject item, out decimal price) {
            price = 0;
            var token = item["priceUsd"];
            if (token != null && (token.Type == JTokenType.Float || token.Type == JTokenType.Integer) &&
                decimal.TryParse(token.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out price) && price > 0 && price <= 2000000) return true;
            var advertised = (string)item["askingPrice"] ?? "";
            if (Number(advertised, "USD", out price) && price > 0 && price <= 2000000) return true;
            // Recover one unambiguous advertised cash price from prose; never average payments or choose between conflicting prices.
            if (Regex.IsMatch(advertised, @"\b(monthly|per month|payment|lease)\b|/mo\b", RegexOptions.IgnoreCase)) { price = 0; return false; }
            var prices = new List<decimal>();
            foreach (Match match in Regex.Matches(advertised, @"\$\s*(?:\d{1,3}(?:,\d{3})+|\d+)(?:\.\d{1,2})?")) {
                decimal candidate;
                if (Number(match.Value.Replace(" ", ""), "USD", out candidate) && candidate > 0 && candidate <= 2000000) prices.Add(candidate);
            }
            var distinct = prices.Distinct().ToList();
            price = distinct.Count == 1 ? distinct[0] : 0;
            return distinct.Count == 1;
        }
        public static bool TryDistance(JObject item, out decimal distance) {
            distance = 0;
            var text = (string)item["distance"] ?? "";
            var match = Regex.Match(text, @"^\s*(?:(?:approximately|approx\.?|estimated|about|~|≈)\s*)?(?<miles>\d+(?:\.\d+)?)\s*(?:miles?|mi)?(?:[\s,;—-]*(?:estimated|estimate|approx(?:imately)?\.?|straight[- ]line(?: distance)?|from (?:Mason(?:,? OH)?|45040)|\([^)]*\)))*\s*$", RegexOptions.IgnoreCase);
            return match.Success && decimal.TryParse(match.Groups["miles"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out distance);
        }
        public static bool WithinRadius(JObject item) {
            decimal distance;
            return TryDistance(item, out distance) && distance >= 0 && distance <= 250;
        }
        public static bool OutsideRadius(JObject item) {
            decimal distance;
            return TryDistance(item, out distance) && distance > 250;
        }
        public static List<JObject> AverageSample(IList<JObject> comparables, int mileage, JObject vehicle) {
            var eligible = new List<JObject>();
            Func<string, string> normalize = value => Regex.Replace(value ?? "", @"[^a-z0-9]", "", RegexOptions.IgnoreCase).ToLowerInvariant();
            if (vehicle == null) return eligible;
            foreach (var item in comparables) {
                decimal price, miles;
                if (!WithinRadius(item) || !TryPrice(item, out price) || !Number((string)item["mileage"], "miles", out miles) || Math.Abs(miles - mileage) > 40000) continue;
                var title = (string)item["titleAndPriceConditions"] ?? "";
                if (Regex.IsMatch(title, @"\b(salvage|rebuilt|reconstructed)\b", RegexOptions.IgnoreCase)) continue;
                var description = normalize((string)item["vehicle"] + " " + (string)item["year"] + " " + (string)item["make"] + " " + (string)item["model"]);
                if (new[] { "ModelYear", "Make", "Model" }.Any(field => string.IsNullOrWhiteSpace((string)vehicle[field]) || !description.Contains(normalize((string)vehicle[field])))) continue;
                eligible.Add(item);
            }
            var preferred = eligible.Where(item => {decimal miles; return Number((string)item["mileage"], "miles", out miles) && Math.Abs(miles - mileage) <= 20000;}).ToList();
            return preferred.Count >= 3 ? preferred : eligible;
        }
        public static decimal? Average(IList<JObject> sample) {
            if (sample.Count == 0) return null;
            return decimal.Round(sample.Average(item => {decimal price; TryPrice(item, out price); return price;}), 2, MidpointRounding.AwayFromZero);
        }
        public static XElement RenderAverage(IList<JObject> sample) {
            var section = new XElement("section", new XElement("h2", "250-mile YMMS average asking price"));
            var average = Average(sample);
            if (!average.HasValue) { section.Add(new XElement("p", "No matching year/make/model/series-trim listings with usable prices, mileage and distance were established within 250 miles. No average or bid baseline is invented.")); return section; }
            decimal total = 0;
            var table = new XElement("table", new XElement("thead", new XElement("tr", new XElement("th", "Matching YMMS / listing"), new XElement("th", "Dealer"), new XElement("th", "Asking price (USD)"))));
            var rows = new XElement("tbody");
            foreach (var item in sample) { decimal price; TryPrice(item, out price); total += price; rows.Add(new XElement("tr", new XElement("td", new XElement("a", new XAttribute("href", (string)item["url"]), (string)item["vehicle"])), new XElement("td", (string)item["dealer"]), new XElement("td", Money(price)))); }
            table.Add(rows); section.Add(table, new XElement("p", "Arithmetic average = " + Money(total) + " / " + sample.Count + " = " + Money(average.Value) + ". This average is the assumed selling-price baseline used in the auction-bid formula below, not a confirmed transaction price."),
                new XElement("p", "Matching YMMS only; prefer mileage within 20,000, expanding to 40,000 when fewer than three close matches are available. Subject assumed clean title and average condition. Title claims, availability and distances still require confirmation."));
            if (sample.Count < 3) section.Add(new XElement("p", "Limited sample: fewer than three listings. Treat this average and any bid as a low-confidence conditional estimate."));
            return section;
        }
        public static int UnderwritingEvidenceCount(IList<JObject> comparables, int subjectMileage, JObject vehicle) {
            if (vehicle == null) return 0;
            var subjectYear = (int?)vehicle["ModelYear"];
            if (!subjectYear.HasValue) return 0;
            Func<string, string> normalize = value => Regex.Replace(value ?? "", @"[^a-z0-9]", "", RegexOptions.IgnoreCase).ToLowerInvariant();
            var eligible = new List<JObject>();
            foreach (var item in comparables) {
                decimal price, miles, distance;
                var text = (string)item["vehicle"] ?? "";
                var year = (int?)item["year"];
                int parsed;
                if (!year.HasValue && int.TryParse(Regex.Match(text, @"\b(?:19|20)\d{2}\b").Value, out parsed)) year = parsed;
                var distanceText = Regex.Replace((string)item["distance"] ?? "", @"\b(approximately|approx\.?|estimated|about)\b|[~≈]|\([^)]*\)", "", RegexOptions.IgnoreCase).Trim();
                if (!TryPrice(item, out price) || !Number((string)item["mileage"], "miles", out miles) || Math.Abs(miles - subjectMileage) > 40000 ||
                    !Number(distanceText, "miles", out distance) || distance < 0 || distance > 250 || !year.HasValue || Math.Abs(year.Value - subjectYear.Value) > 1) continue;
                var details = normalize(text);
                if (new[] { "Make", "Model" }.Any(x => string.IsNullOrWhiteSpace((string)vehicle[x]) || !details.Contains(normalize((string)vehicle[x])))) continue;
                eligible.Add(item);
            }
            return eligible.GroupBy(item => {
                var title = ((string)item["titleAndPriceConditions"] ?? "").ToLowerInvariant();
                if (Regex.IsMatch(title, @"\b(salvage|rebuilt|reconstructed)\b")) return "branded";
                if (Regex.IsMatch(title, @"\bclean[ -]title\b") && !Regex.IsMatch(title, @"\b(unverified|unknown|not confirmed|not verified|assumed)\b")) return "advertised clean";
                return "unknown";
            }).Select(group => group.Count()).DefaultIfEmpty(0).Max();
        }
        private static string Money(decimal value) { return value.ToString("C2", CultureInfo.GetCultureInfo("en-US")); }
        private static decimal Quantile(List<decimal> values, decimal fraction) {
            var position = (values.Count - 1) * fraction;
            var low = (int)Math.Floor(position); var high = (int)Math.Ceiling(position);
            return values[low] + (values[high] - values[low]) * (position - low);
        }
        public static XElement Build(IList<JObject> comparables, int subjectMileage, JObject vehicle = null) {
            var section = new XElement("section", new XElement("h2", "Market asking-price evidence"));
            var usable = new List<Tuple<JObject, decimal, decimal>>();
            foreach (var item in comparables) {
                decimal price, miles, distance;
                if (!TryPrice(item, out price) ||
                    !Number((string)item["mileage"], "miles", out miles) || miles < 0 || miles > 2000000) continue;
                // A distance must be disclosed and cannot exceed the requested radius.
                var distanceText = Regex.Replace((string)item["distance"] ?? "", @"\b(approximately|approx\.?|estimated|about)\b|[~≈]", "", RegexOptions.IgnoreCase).Trim();
                distanceText = Regex.Replace(distanceText, @"\s*\([^)]*\)\s*$", "").Trim();
                if (!Number(distanceText, "miles", out distance) || distance < 0 || distance > 250) continue;
                if (vehicle != null) {
                    var details = Regex.Replace((string)item["vehicle"] ?? "", @"[^a-z0-9]", "", RegexOptions.IgnoreCase).ToLowerInvariant();
                    if (new[] { "ModelYear", "Make", "Model", "Trim" }.Where(x => !string.IsNullOrWhiteSpace((string)vehicle[x]))
                        .Any(x => !details.Contains(Regex.Replace((string)vehicle[x], @"[^a-z0-9]", "", RegexOptions.IgnoreCase).ToLowerInvariant()))) continue;
                }
                usable.Add(Tuple.Create(item, price, Math.Abs(miles - subjectMileage)));
            }
            var any = false;
            foreach (var group in usable.GroupBy(x => {
                var title = ((string)x.Item1["titleAndPriceConditions"] ?? "").ToLowerInvariant();
                if (Regex.IsMatch(title, @"\b(salvage|rebuilt|reconstructed)\b")) return "Salvage / rebuilt";
                if (Regex.IsMatch(title, @"\bclean[ -]title\b") && !Regex.IsMatch(title, @"\b(unverified|unknown|not confirmed|not verified|assumed)\b")) return "Advertised clean title (not independently verified)";
                return "Title unverified";
            })) {
                var sample = group.Where(x => x.Item3 <= 20000).ToList();
                var expanded = false;
                if (sample.Count < 3) { sample = group.Where(x => x.Item3 <= 40000).ToList(); expanded = true; }
                section.Add(new XElement("h3", group.Key));
                var sortedSample = sample.OrderBy(x => x.Item2).ToList();
                if (sortedSample.Count > 0) {
                    var table = new XElement("table", new XElement("thead", new XElement("tr",
                        new[] { "Sorted position", "YMMS / vehicle", "Dealer / source", "Advertised price (USD)", "Median selection" }.Select(x => new XElement("th", x)))));
                    var body = new XElement("tbody");
                    var lower = (sortedSample.Count - 1) / 2;
                    var upper = sortedSample.Count / 2;
                    for (var index = 0; index < sortedSample.Count; index++) {
                        var entry = sortedSample[index];
                        body.Add(new XElement("tr", new XElement("td", index + 1),
                            new XElement("td", (string)entry.Item1["vehicle"]),
                            new XElement("td", new XElement("a", new XAttribute("href", (string)entry.Item1["url"]), (string)entry.Item1["dealer"])),
                            new XElement("td", entry.Item2.ToString("F2", CultureInfo.InvariantCulture)),
                            new XElement("td", index == lower || index == upper ? "Middle value" : "")));
                    }
                    table.Add(body); section.Add(table);
                    var calculation = lower == upper ? Money(sortedSample[lower].Item2) : "(" + Money(sortedSample[lower].Item2) + " + " + Money(sortedSample[upper].Item2) + ") / 2";
                    section.Add(new XElement("p", "Observed sample median: " + calculation + " = " + Money(Quantile(sortedSample.Select(x => x.Item2).ToList(), .5m)) +
                        ". " + (sortedSample.Count < 3 ? "Insufficient sample for a market valuation." : "Based on the sorted advertised prices above; not a confirmed selling price.")));
                }
                if (sample.Count < 3) {
                    section.Add(new XElement("p", sample.Count + " usable comparable(s). At least three distinct listings in the same title group are required for an indicative asking-price range."));
                    if (sample.Count > 0) section.Add(new XElement("p", "Observed asking prices only: " + Money(sample.Min(x => x.Item2)) + "–" + Money(sample.Max(x => x.Item2)) + ". This small sample is not a market valuation or a safe auction bid."));
                    continue;
                }
                any = true;
                var prices = sample.Select(x => x.Item2).OrderBy(x => x).ToList();
                var median = Quantile(prices, .5m);
                section.Add(new XElement("p", "Indicative dealer asking-price range (middle 50%): " + Money(Quantile(prices, .25m)) + "–" + Money(Quantile(prices, .75m)) +
                    ". Median asking price: " + Money(median) + ". Full observed range: " + Money(prices.First()) + "–" + Money(prices.Last()) + "."));
                section.Add(new XElement("p", "Sample: " + sample.Count + " listings within 250 miles; mileage band ±" + (expanded ? "40,000 (expanded from 20,000 due to limited supply)" : "20,000") +
                    " miles. These are raw asking prices; there is no inferred sale-price discount or automatic mileage, trim, drivetrain, condition or history adjustment. Confirm those differences before treating this as the subject vehicle's value."));
                section.Add(new XElement("ul", sample.Select(x => new XElement("li", new XElement("a", new XAttribute("href", (string)x.Item1["url"]),
                    (string)x.Item1["dealer"] + " — " + (string)x.Item1["vehicle"] + " — " + Money(x.Item2))))));
            }
            if (!any) section.Add(new XElement("p", "Market price unavailable: fewer than three suitable source-backed comparables in any title group. Supply current dealer listings through POST /api/service/acquisition/{vin}/html or verify listings manually. No price is invented."));
            section.Add(new XElement("p", "Method: separate title groups; deduplicate before calculation; median and interpolated 25th/75th percentiles of advertised USD prices. Research date: " + DateTime.UtcNow.ToString("yyyy-MM-dd") + " UTC. Asking prices are not confirmed transaction prices."));
            section.SetAttributeValue("data-sufficient-evidence", any ? "true" : "false");
            return section;
        }
    }
}
