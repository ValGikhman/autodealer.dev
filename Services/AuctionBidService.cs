using autodealer.dev.Models;
using System;
using System.Collections.Specialized;
using System.Configuration;
using System.Globalization;

namespace autodealer.dev.Services {
    public sealed class AuctionBidService {
        public const string CostScope = "Auction fee and transport only; profit before other expenses.";
        private readonly AuctionBidSettings settings;

        public AuctionBidService() : this(ReadSettings(ConfigurationManager.AppSettings)) { }

        public AuctionBidService(AuctionBidSettings settings) {
            if (settings == null) throw new AuctionBidConfigurationException("Auction bid configuration is missing. Set AuctionBid:AuctionFeeUsd, AuctionBid:TransportUsd and AuctionBid:MinimumProfitUsd in Web.config.");
            ValidateSetting("AuctionBid:AuctionFeeUsd", settings.AuctionFeeUsd, 2000000m);
            ValidateSetting("AuctionBid:TransportUsd", settings.TransportUsd, 2000000m);
            ValidateSetting("AuctionBid:MinimumProfitUsd", settings.MinimumProfitUsd, 1000000m);
            this.settings = settings;
        }

        public static AuctionBidSettings ReadSettings(NameValueCollection values) {
            return new AuctionBidSettings(
                ReadSetting(values, "AuctionBid:AuctionFeeUsd", 2000000m),
                ReadSetting(values, "AuctionBid:TransportUsd", 2000000m),
                ReadSetting(values, "AuctionBid:MinimumProfitUsd", 1000000m));
        }

        private static decimal ReadSetting(NameValueCollection values, string key, decimal maximum) {
            decimal value;
            if (!decimal.TryParse((values?[key] ?? string.Empty).Trim(), NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value))
                throw InvalidSetting(key, maximum);
            ValidateSetting(key, value, maximum);
            return value;
        }

        private static void ValidateSetting(string key, decimal value, decimal maximum) {
            if (value < 0 || value > maximum || decimal.Round(value, 2) != value) throw InvalidSetting(key, maximum);
        }

        private static AuctionBidConfigurationException InvalidSetting(string key, decimal maximum) {
            return new AuctionBidConfigurationException("Auction bid configuration is missing or invalid. Set " + key
                + " in Web.config to a USD amount from 0 to " + maximum.ToString("0", CultureInfo.InvariantCulture) + " with no more than two decimal places.");
        }

        public AuctionBidResult Calculate(AuctionBidRequest request) {
            if (request == null) throw new ArgumentException("Enter an expected selling price.");
            request.Validate();
            var sale = request.ExpectedSellingPriceUsd.Value;
            var knownCosts = settings.AuctionFeeUsd + settings.TransportUsd;
            var rawCeiling = sale - knownCosts - settings.MinimumProfitUsd;
            var maximumBid = Math.Max(0m, Math.Floor(rawCeiling / 100m) * 100m);
            var maximumInvestment = maximumBid + knownCosts;
            var maximumProfit = sale - maximumInvestment;
            var feasible = rawCeiling > 0 && maximumBid > 0 && maximumProfit >= settings.MinimumProfitUsd;
            decimal? currentInvestment = request.CurrentHammerBidUsd.HasValue ? request.CurrentHammerBidUsd.Value + knownCosts : (decimal?)null;
            return new AuctionBidResult {
                Status = feasible ? "ready" : "no_bid",
                ExpectedSellingPriceUsd = sale,
                AuctionFeeUsd = settings.AuctionFeeUsd,
                TransportUsd = settings.TransportUsd,
                MinimumProfitUsd = settings.MinimumProfitUsd,
                TotalKnownCostsUsd = knownCosts,
                RoundingIncrementUsd = 100m,
                MaximumHammerBidUsd = feasible ? maximumBid : (decimal?)null,
                BreakEvenHammerBidUsd = Math.Max(0m, sale - knownCosts),
                MaximumTotalInvestmentUsd = feasible ? maximumInvestment : (decimal?)null,
                ProfitAtMaximumUsd = feasible ? maximumProfit : (decimal?)null,
                CurrentHammerBidUsd = request.CurrentHammerBidUsd,
                CurrentTotalInvestmentUsd = currentInvestment,
                CurrentProfitUsd = currentInvestment.HasValue ? sale - currentInvestment.Value : (decimal?)null,
                CurrentBidWithinLimit = request.CurrentHammerBidUsd.HasValue ? feasible && request.CurrentHammerBidUsd.Value <= maximumBid : (bool?)null,
                Formula = "Maximum hammer bid = expected selling price - auction fee - transport - minimum profit, rounded down to $100.",
                CostScope = CostScope
            };
        }
    }

    public sealed class AuctionBidConfigurationException : Exception {
        public AuctionBidConfigurationException(string message) : base(message) { }
    }
}
