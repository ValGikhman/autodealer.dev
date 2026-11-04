using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace autodealer.dev.Services {
    public static class AcquisitionReportDocument {
        public static JObject Format() {
            var format = JObject.Parse(@"{
              'type':'json_schema','name':'acquisition_report','strict':true,'schema':{
                'type':'object','additionalProperties':false,
                'properties':{
                  'recommendation':{'type':'string','enum':['BUY','BID LOWER','PASS']},
                  'summary':{'type':'string'},
                  'comparisonOverview':{'type':'string'},
                  'excludedListingNotes':{'type':'array','items':{'type':'string'}},
                  'finalDecision':{'type':'object','additionalProperties':false,'properties':{
                    'maximumAuctionBid':{'type':'string'},'expectedTotalInvestment':{'type':'string'},
                    'likelySellingPrice':{'type':'string'},'estimatedProfit':{'type':'string'},
                    'mainRisks':{'type':'array','items':{'type':'string'}},
                    'missingInformation':{'type':'array','items':{'type':'string'}}
                  },'required':['maximumAuctionBid','expectedTotalInvestment','likelySellingPrice','estimatedProfit','mainRisks','missingInformation']},
                  'comparables':{'type':'array','items':{'type':'object','additionalProperties':false,'properties':{
                    'dealer':{'type':'string'},'listingVin':{'type':'string'},'location':{'type':'string'},'distance':{'type':'string'},
                    'vehicle':{'type':'string'},'year':{'type':['integer','null']},'make':{'type':'string'},'model':{'type':'string'},'seriesTrim':{'type':'string'},'priceUsd':{'type':['number','null']},'mileage':{'type':'string'},'askingPrice':{'type':'string'},
                    'selectionNotes':{'type':'string'},'adjustmentUsd':{'type':['number','null']},'adjustmentBasis':{'type':'string'},'titleAndPriceConditions':{'type':'string'},'url':{'type':'string'}
                  },'required':['dealer','listingVin','location','distance','vehicle','year','make','model','seriesTrim','priceUsd','mileage','askingPrice','selectionNotes','adjustmentUsd','adjustmentBasis','titleAndPriceConditions','url']}},
                  'sections':{'type':'array','items':{'type':'object','additionalProperties':false,'properties':{
                    'heading':{'type':'string'},'paragraphs':{'type':'array','items':{'type':'string'}}
                  },'required':['heading','paragraphs']}}
                },'required':['recommendation','summary','comparisonOverview','excludedListingNotes','finalDecision','comparables','sections']
              }
            }");
            format["schema"]["properties"]["underwriting"] = AcquisitionUnderwriting.Schema();
            ((JArray)format["schema"]["required"]).Add("underwriting");
            return format;
        }

        private static string CanonicalUrl(string url) {
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || (uri.Scheme != "https" && uri.Scheme != "http")) return null;
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            foreach (var key in query.AllKeys.Where(x => x != null && x.StartsWith("utm_", StringComparison.OrdinalIgnoreCase)).ToArray()) query.Remove(key);
            return uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + (query.Count == 0 ? "" : "?" + query);
        }

        public static string ToArticle(JObject report, JObject response, int? subjectMileage = null, JObject vehicle = null, bool callerSupplied = false, decimal? desiredProfit = null) {
            var sources = response["output"] == null ? new List<string>() : response["output"].SelectTokens("$..url")
                .Where(x => x.Type == JTokenType.String).Select(x => (string)x).Where(x => CanonicalUrl(x) != null).Distinct().ToList();
            var sourceSet = new HashSet<string>(sources.Select(CanonicalUrl), StringComparer.OrdinalIgnoreCase);
            var candidates = (report["comparables"] as JArray ?? new JArray()).OfType<JObject>().ToList();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenVins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var comparables = candidates.Where(x => {
                var url = CanonicalUrl((string)x["url"]);
                var dealer = (string)x["dealer"] ?? "";
                var listingVin = ((string)x["listingVin"] ?? "").Trim().ToUpperInvariant();
                var subjectVin = (string)vehicle?["VIN"];
                if (!string.IsNullOrWhiteSpace(subjectVin) && (string.Equals(subjectVin, listingVin, StringComparison.OrdinalIgnoreCase) || (url ?? "").IndexOf(subjectVin, StringComparison.OrdinalIgnoreCase) >= 0)) return false;
                if (AcquisitionMarketPrice.OutsideRadius(x) || url == null || !sourceSet.Contains(url) || seen.Contains(url) || dealer.Length == 0 ||
                    Regex.IsMatch(dealer, @"^(dealer\s+[a-z0-9]|example|sample|placeholder)\b", RegexOptions.IgnoreCase)) return false;
                if (Regex.IsMatch(listingVin, "^[A-HJ-NPR-Z0-9]{17}$") && !seenVins.Add(listingVin)) return false;
                seen.Add(url);
                return true;
            }).ToList();

            var averageSample = comparables.Where(item => { decimal price; return AcquisitionMarketPrice.TryPrice(item, out price); }).ToList();
            var averagePrice = AcquisitionMarketPrice.Average(averageSample);
            var insufficient = !averagePrice.HasValue;
            var budgetInputs = report["underwriting"] as JObject;
            if (averagePrice.HasValue) {
                budgetInputs = budgetInputs == null ? new JObject() : (JObject)budgetInputs.DeepClone();
                budgetInputs["expectedSellingPriceUsd"] = averagePrice.Value;
                var assumptions = budgetInputs["assumptions"] as JArray ?? new JArray();
                assumptions.Add("Expected selling price is the server-calculated arithmetic mean of usable advertised prices in the displayed dealer comparison table, used as an assumption rather than a confirmed sale price.");
                budgetInputs["assumptions"] = assumptions;
            }
            if (!averagePrice.HasValue && budgetInputs != null) {
                budgetInputs = (JObject)budgetInputs.DeepClone();
                budgetInputs["expectedSellingPriceUsd"] = JValue.CreateNull();
            }
            var underwriting = desiredProfit.HasValue ? AcquisitionUnderwriting.Calculate(budgetInputs, desiredProfit.Value) : null;
            var article = new XElement("article", new XElement("p", "Research date (UTC): " + DateTime.UtcNow.ToString("yyyy-MM-dd") + " • Clean title and average condition assumed."));
            if (callerSupplied) article.Add(new XElement("p", "Listing details supplied by the caller."));
            article.Add(new XElement("h2", comparables.Count > 0 ? comparables.Count + " nearby dealer comparisons" : "Nearby dealer comparisons"));
            {
                var table = new XElement("table", new XElement("thead", new XElement("tr",
                    new[] { "Dealer / listing", "Location / approx. distance", "Vehicle", "Mileage", "Advertised price", "Qualifications" }.Select(x => new XElement("th", x)))));
                var body = new XElement("tbody");
                foreach (var item in comparables) {
                    var qualifications = new[] { (string)item["titleAndPriceConditions"], (string)item["selectionNotes"] }
                        .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct();
                    decimal displayedPrice;
                    var priceIncluded = AcquisitionMarketPrice.TryPrice(item, out displayedPrice);
                    var qualificationText = string.Join(" ", qualifications);
                    if (!priceIncluded) qualificationText += " Excluded from average: advertised price unavailable or ambiguous.";
                    else if (System.Text.RegularExpressions.Regex.Matches((string)item["askingPrice"] ?? "", @"\$").Count > 1) qualificationText += " Average uses " + AcquisitionUnderwriting.Money(displayedPrice) + ".";
                    body.Add(new XElement("tr",
                        new XElement("td", new XElement("a", new XAttribute("href", (string)item["url"]), (string)item["dealer"])),
                        new XElement("td", (string)item["location"] + " / " + (string)item["distance"]),
                        new XElement("td", (string)item["vehicle"]), new XElement("td", (string)item["mileage"]),
                        new XElement("td", (string)item["askingPrice"]), new XElement("td", qualificationText.Trim())));
                }
                table.Add(body); article.Add(table);



            }
            var advertisedTotal = averageSample.Sum(item => { decimal price; AcquisitionMarketPrice.TryPrice(item, out price); return price; });
            if (underwriting != null && !insufficient) article.Add(underwriting.RenderSummary(averageSample.Count, advertisedTotal));
            else {
                if (averagePrice.HasValue) article.Add(new XElement("p", "Average advertised price: " + AcquisitionUnderwriting.Money(advertisedTotal) + " ÷ " + averageSample.Count + " = " + AcquisitionUnderwriting.Money(averagePrice.Value) + "."));
                var reasons = new List<string>();
                if (insufficient) reasons.Add("none of the displayed dealer rows has an unambiguous advertised price");
                reasons.AddRange(AcquisitionUnderwriting.MissingInputs(budgetInputs, desiredProfit).Where(x => !insufficient || x != "usable average selling price"));
                article.Add(new XElement("p", "Hammer bid calculation needs: " + string.Join("; ", reasons) + "."));
            }
            return article.ToString();
        }
    }
}
