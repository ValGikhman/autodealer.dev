using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;

namespace autodealer.dev.Services {
    public sealed class AcquisitionUnderwriting {
        public decimal MaximumBid { get; private set; }
        public decimal RecommendedBid { get; private set; }
        public decimal SellingPrice { get; private set; }
        public decimal MaximumInvestment { get; private set; }
        public decimal MaximumProfit { get { return SellingPrice - MaximumInvestment; } }
        private decimal fixedFee, feeRate, otherCosts, desiredProfit;
        private JObject inputs;
        private static readonly string[] CostKeys = { "transportUsd", "inspectionUsd", "repairsUsd", "detailingUsd", "taxAndTitleUsd", "holdingUsd", "sellingExpensesUsd" };
        private static readonly string[] CostNames = { "Transport to 45040", "Inspection / diagnostics", "Repairs / maintenance reserve", "Detailing", "Acquisition tax / title / administration", "Holding costs", "Selling expenses" };
        public static JObject Schema() {
            var properties = new JObject();
            foreach (var key in new[] { "competitiveListingLowUsd", "competitiveListingHighUsd", "expectedSellingPriceUsd", "quickSaleLowUsd", "quickSaleHighUsd", "recommendedBidUsd", "fixedAuctionFeeUsd", "auctionFeeRate", "downsideSellingPriceUsd", "downsideExtraRepairsUsd" }.Concat(CostKeys))
                properties[key] = new JObject { ["type"] = new JArray("number", "null") };
            properties["assumptions"] = new JObject { ["type"] = "array", ["items"] = new JObject { ["type"] = "string" } };
            return new JObject { ["type"] = "object", ["additionalProperties"] = false, ["properties"] = properties, ["required"] = new JArray(properties.Properties().Select(x => x.Name)) };
        }
        private static decimal? Read(JObject input, string key) {
            var value = input?[key];
            if (value == null || (value.Type != JTokenType.Integer && value.Type != JTokenType.Float)) return null;
            decimal number;
            if (!decimal.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)) return null;
            return number >= 0 && number <= 2000000 ? number : (decimal?)null;
        }
        public static IList<string> MissingInputs(JObject input, decimal? desiredProfit) {
            var issues = new List<string>();
            if (!desiredProfit.HasValue || desiredProfit < 0 || desiredProfit > 1000000) issues.Add("valid desired profit");
            if (input == null) { issues.Add("acquisition cost estimates and auction fee model"); return issues; }
            var sale = Read(input, "expectedSellingPriceUsd");
            if (!sale.HasValue || sale <= 0) issues.Add("usable average selling price");
            if (!Read(input, "fixedAuctionFeeUsd").HasValue) issues.Add("fixed auction fee estimate");
            var rate = Read(input, "auctionFeeRate");
            if (!rate.HasValue || rate > 1) issues.Add("auction fee rate as a fraction (0.025 means 2.5%)");
            for (var index = 0; index < CostKeys.Length; index++)
                if (!Read(input, CostKeys[index]).HasValue) issues.Add(CostNames[index]);
            if (!(input["assumptions"] as JArray ?? new JArray()).Any(x => !string.IsNullOrWhiteSpace((string)x))) issues.Add("explicit cost assumptions");
            return issues;
        }

        public static AcquisitionUnderwriting Calculate(JObject input, decimal desiredProfit) {
            if (input == null || desiredProfit < 0 || desiredProfit > 1000000) return null;
            var sale = Read(input, "expectedSellingPriceUsd");
            var fixedFee = Read(input, "fixedAuctionFeeUsd");
            var rate = Read(input, "auctionFeeRate");
            var costs = CostKeys.Select(x => Read(input, x)).ToList();
            if (!sale.HasValue || sale <= 0 || !fixedFee.HasValue || !rate.HasValue || rate > 1 || costs.Any(x => !x.HasValue)) return null;
            if (!(input["assumptions"] as JArray ?? new JArray()).Any(x => !string.IsNullOrWhiteSpace((string)x))) return null;
            var total = costs.Sum(x => x.Value);
            var maximum = Math.Max(0, Math.Floor((sale.Value - desiredProfit - total - fixedFee.Value) / (1 + rate.Value) / 100) * 100);
            var recommended = Read(input, "recommendedBidUsd");
            if (!recommended.HasValue) recommended = maximum;
            var result = new AcquisitionUnderwriting { inputs = input, fixedFee = fixedFee.Value, feeRate = rate.Value, otherCosts = total,
                desiredProfit = desiredProfit, SellingPrice = sale.Value, MaximumBid = maximum, RecommendedBid = Math.Min(maximum, Math.Floor(recommended.Value / 100) * 100) };
            result.MaximumInvestment = result.Investment(maximum);
            return result;
        }
        private decimal Fee(decimal bid) { return decimal.Round(fixedFee + bid * feeRate, 2, MidpointRounding.AwayFromZero); }
        private decimal Investment(decimal bid) { return bid + Fee(bid) + otherCosts; }
        public static string Money(decimal value) { return value.ToString("C2", CultureInfo.GetCultureInfo("en-US")); }
        public XElement RenderSummary(int priceCount, decimal? advertisedTotal = null) {
            var section = new XElement("section", new XElement("h2", "Auction bid summary"),
                new XElement("p", "Average advertised price: " + Money(advertisedTotal ?? SellingPrice * priceCount) + " ÷ " + priceCount + " = " + Money(SellingPrice) + ". Used as the resale-price assumption. Desired profit: " + Money(desiredProfit) + "."),
                new XElement("h3", "Hammer bid calculation"));
            section.Add(new XElement("p", "Maximum hammer bid = (" + Money(SellingPrice) + " average − " + Money(desiredProfit) + " desired profit − " + Money(otherCosts) + " other costs − " + Money(fixedFee) + " fixed auction fee) ÷ " + (1 + feeRate).ToString("0.####", CultureInfo.InvariantCulture) + " = " + Money((SellingPrice - desiredProfit - otherCosts - fixedFee) / (1 + feeRate)) + "; rounded down to $100: " + Money(MaximumBid) + "."));
            var table = new XElement("table", new XElement("thead", new XElement("tr", new XElement("th", "Calculation"), new XElement("th", "Recommended bid"), new XElement("th", "Maximum bid"))));
            var rows = new XElement("tbody");
            Action<string, decimal, decimal> add = (name, first, second) => rows.Add(new XElement("tr", new XElement("td", name), new XElement("td", Money(first)), new XElement("td", Money(second))));
            add("Hammer bid", RecommendedBid, MaximumBid);
            add("Modeled auction / buyer fees", Fee(RecommendedBid), Fee(MaximumBid));
            add("Other acquisition and selling costs", otherCosts, otherCosts);
            add("Total investment through sale", Investment(RecommendedBid), MaximumInvestment);
            add("Assumed selling price", SellingPrice, SellingPrice);
            add("Estimated profit before income tax", SellingPrice - Investment(RecommendedBid), MaximumProfit);
            var downside = Read(inputs, "downsideSellingPriceUsd"); var extra = Read(inputs, "downsideExtraRepairsUsd");
            if (downside.HasValue && extra.HasValue && downside <= SellingPrice)
                add("Downside profit — " + Money(downside.Value) + " sale and " + Money(extra.Value) + " additional repairs", downside.Value - Investment(RecommendedBid) - extra.Value, downside.Value - MaximumInvestment - extra.Value);
            table.Add(rows); section.Add(table);
            section.Add(new XElement("p", "Auction fee assumption: " + Money(fixedFee) + " + " + (feeRate * 100).ToString("0.####", CultureInfo.InvariantCulture) + "% of hammer bid. Costs and selling price are estimates."));
            return section;
        }
        public static XElement RenderUnavailable(JObject input) {
            var section = new XElement("section", new XElement("h2", "Resale estimate and adjustments"),
                new XElement("p", "Amounts shown are provisional assumptions. Missing inputs remain unavailable and are never treated as zero."));
            Func<string, string> amount = key => Read(input, key).HasValue ? Money(Read(input, key).Value) : "Unavailable — estimate or verification required";
            var pricing = new XElement("table", new XElement("thead", new XElement("tr", new XElement("th", "Pricing decision"), new XElement("th", "Estimate (USD)"))));
            var priceRows = new XElement("tbody");
            foreach (var pair in new[] { new[] { "competitiveListingLowUsd", "Competitive listing — low" }, new[] { "competitiveListingHighUsd", "Competitive listing — high" }, new[] { "expectedSellingPriceUsd", "Base-case negotiated selling price" }, new[] { "quickSaleLowUsd", "Quick sale — low" }, new[] { "quickSaleHighUsd", "Quick sale — high" } })
                priceRows.Add(new XElement("tr", new XElement("td", pair[1]), new XElement("td", amount(pair[0]))));
            pricing.Add(priceRows); section.Add(pricing);
            section.Add(new XElement("h2", "Acquisition budget"));
            var assumptions = input?["assumptions"] as JArray ?? new JArray();
            section.Add(new XElement("p", "Verify title, condition, transport, holding period, all auction charges and acquisition-tax eligibility."));
            if (assumptions.Count > 0) section.Add(new XElement("ul", assumptions.Select(x => new XElement("li", (string)x))));
            var budget = new XElement("table", new XElement("thead", new XElement("tr", new XElement("th", "Expense"), new XElement("th", "Budget (USD)"))));
            var rows = new XElement("tbody");
            var fee = Read(input, "fixedAuctionFeeUsd"); var rate = Read(input, "auctionFeeRate");
            rows.Add(new XElement("tr", new XElement("td", "Auction / buyer fees"), new XElement("td", fee.HasValue && rate.HasValue && rate <= 1 ? Money(fee.Value) + " + " + (rate.Value * 100).ToString("0.####", CultureInfo.InvariantCulture) + "% of hammer bid — illustrative model" : "Unavailable — fee schedule or explicit assumption required")));
            for (var index = 0; index < CostKeys.Length; index++) rows.Add(new XElement("tr", new XElement("td", CostNames[index]), new XElement("td", amount(CostKeys[index]))));
            var costs = CostKeys.Select(x => Read(input, x)).ToList();
            rows.Add(new XElement("tr", new XElement("td", "Costs excluding hammer price and auction fees"), new XElement("td", costs.All(x => x.HasValue) ? Money(costs.Sum(x => x.Value)) : "Unavailable — one or more costs are missing")));
            budget.Add(rows); section.Add(budget);
            section.Add(new XElement("p", "Fee models are not verified auction quotes. Verify broker, online bidding, arbitration and gate fees. Acquisition-tax exemptions require confirmation of eligibility."));
            section.Add(new XElement("h2", "Maximum bid and profit"), new XElement("p", "Maximum hammer bid = (expected selling price − desired profit − other acquisition/selling costs − fixed auction fee) / (1 + bid-dependent auction fee rate). Every input is required for a numeric result."));
            var scenarios = new XElement("table", new XElement("thead", new XElement("tr", new XElement("th", "Scenario"), new XElement("th", "Recommended bid"), new XElement("th", "Maximum bid"))));
            var scenarioRows = new XElement("tbody");
            foreach (var label in new[] { "Hammer price", "Modeled auction fees", "Other budgeted costs", "Total investment through sale", "Expected selling price", "Estimated profit before income tax", "Downside: lower selling price and higher repairs" })
                scenarioRows.Add(new XElement("tr", new XElement("td", label), new XElement("td", "Unavailable — incomplete inputs"), new XElement("td", "Unavailable — incomplete inputs")));
            scenarios.Add(scenarioRows); section.Add(scenarios);
            return section;
        }

        public XElement Render() {
            var section = new XElement("section", new XElement("h2", "Resale estimate and adjustments"),
                new XElement("p", "These are provisional resale and cost assumptions, not confirmed sale prices, history, tax eligibility or an actual auction fee quote. Verify the fee model, title, condition and every cost before bidding."));
            var prices = new XElement("table", new XElement("thead", new XElement("tr", new XElement("th", "Pricing estimate"), new XElement("th", "USD"))));
            var priceBody = new XElement("tbody");
            foreach (var pair in new[] { new[] { "competitiveListingLowUsd", "Competitive listing — low" }, new[] { "competitiveListingHighUsd", "Competitive listing — high" }, new[] { "expectedSellingPriceUsd", "Expected negotiated sale" }, new[] { "quickSaleLowUsd", "Quick sale — low" }, new[] { "quickSaleHighUsd", "Quick sale — high" } })
                priceBody.Add(new XElement("tr", new XElement("td", pair[1]), new XElement("td", Read(inputs, pair[0]).HasValue ? Money(Read(inputs, pair[0]).Value) : "Unavailable")));
            prices.Add(priceBody); section.Add(prices);
            section.Add(new XElement("h2", "Acquisition budget"));
            var budget = new XElement("table", new XElement("thead", new XElement("tr", new XElement("th", "Expense assumption"), new XElement("th", "USD"))));
            var budgetBody = new XElement("tbody");
            for (var index = 0; index < CostKeys.Length; index++) budgetBody.Add(new XElement("tr", new XElement("td", CostNames[index]), new XElement("td", Money(Read(inputs, CostKeys[index]).Value))));
            budgetBody.Add(new XElement("tr", new XElement("td", "Total excluding hammer bid and auction fee"), new XElement("td", Money(otherCosts))));
            budgetBody.Add(new XElement("tr", new XElement("td", "Auction fee F(B) — provisional model"), new XElement("td", Money(fixedFee) + " + " + (feeRate * 100).ToString("0.####", CultureInfo.InvariantCulture) + "% × hammer bid")));
            budget.Add(budgetBody); section.Add(budget);
            section.Add(new XElement("h2", "Maximum bid and profit"));
            section.Add(new XElement("p", "Maximum B = (expected sale − desired profit − other costs − fixed auction fee) / (1 + auction fee rate) = (" + Money(SellingPrice) + " − " + Money(desiredProfit) + " − " + Money(otherCosts) + " − " + Money(fixedFee) + ") / " + (1 + feeRate).ToString("0.####", CultureInfo.InvariantCulture) + ". Rounded down to $100: " + Money(MaximumBid) + "."));
            var scenario = new XElement("table", new XElement("thead", new XElement("tr", new XElement("th", "Scenario"), new XElement("th", "Recommended bid"), new XElement("th", "Maximum bid"))));
            var rows = new XElement("tbody");
            Action<string, decimal, decimal> add = (label, first, second) => rows.Add(new XElement("tr", new XElement("td", label), new XElement("td", Money(first)), new XElement("td", Money(second))));
            add("Hammer bid", RecommendedBid, MaximumBid); add("Modeled auction fee", Fee(RecommendedBid), Fee(MaximumBid));
            add("Other budgeted costs", otherCosts, otherCosts); add("Total investment through sale", Investment(RecommendedBid), MaximumInvestment);
            add("Expected sale", SellingPrice, SellingPrice); add("Estimated profit before income tax", SellingPrice - Investment(RecommendedBid), MaximumProfit);
            var downsideSale = Read(inputs, "downsideSellingPriceUsd"); var extraRepairs = Read(inputs, "downsideExtraRepairsUsd");
            if (downsideSale.HasValue && extraRepairs.HasValue && downsideSale <= SellingPrice)
                add("Downside profit: " + Money(downsideSale.Value) + " sale; " + Money(extraRepairs.Value) + " additional repairs", downsideSale.Value - Investment(RecommendedBid) - extraRepairs.Value, downsideSale.Value - MaximumInvestment - extraRepairs.Value);
            else section.Add(new XElement("p", "Downside profit unavailable: a lower selling price and additional repair reserve were not supplied."));
            scenario.Add(rows); section.Add(scenario);
            section.Add(new XElement("h3", "Assumptions requiring verification"), new XElement("ul", (inputs["assumptions"] as JArray ?? new JArray()).Select(x => new XElement("li", (string)x))));
            return section;
        }
    }
}
