using System;
using System.ComponentModel.DataAnnotations;

namespace autodealer.dev.Models {
    public sealed class AuctionBidRequest {
        [Required(ErrorMessage = "Enter an expected selling price.")]
        [Range(typeof(decimal), "0", "2000000", ErrorMessage = "Expected selling price must be greater than $0 and no more than $2,000,000.")]
        public decimal? ExpectedSellingPriceUsd { get; set; }

        [Range(typeof(decimal), "0", "2000000", ErrorMessage = "Current hammer bid must be between $0 and $2,000,000.")]
        public decimal? CurrentHammerBidUsd { get; set; }

        public void Validate() {
            if (!ExpectedSellingPriceUsd.HasValue || ExpectedSellingPriceUsd <= 0 || ExpectedSellingPriceUsd > 2000000m)
                throw new ArgumentException("Expected selling price must be greater than $0 and no more than $2,000,000.");
            if (CurrentHammerBidUsd.HasValue && (CurrentHammerBidUsd < 0 || CurrentHammerBidUsd > 2000000m))
                throw new ArgumentException("Current hammer bid must be between $0 and $2,000,000.");
        }
    }

    public sealed class AuctionBidSettings {
        public decimal AuctionFeeUsd { get; private set; }
        public decimal TransportUsd { get; private set; }
        public decimal MinimumProfitUsd { get; private set; }

        public AuctionBidSettings(decimal auctionFeeUsd, decimal transportUsd, decimal minimumProfitUsd) {
            AuctionFeeUsd = auctionFeeUsd;
            TransportUsd = transportUsd;
            MinimumProfitUsd = minimumProfitUsd;
        }
    }

    public sealed class AuctionBidResult {
        public string Status { get; set; }
        public decimal ExpectedSellingPriceUsd { get; set; }
        public decimal AuctionFeeUsd { get; set; }
        public decimal TransportUsd { get; set; }
        public decimal MinimumProfitUsd { get; set; }
        public decimal TotalKnownCostsUsd { get; set; }
        public decimal RoundingIncrementUsd { get; set; }
        public decimal? MaximumHammerBidUsd { get; set; }
        public decimal BreakEvenHammerBidUsd { get; set; }
        public decimal? MaximumTotalInvestmentUsd { get; set; }
        public decimal? ProfitAtMaximumUsd { get; set; }
        public decimal? CurrentHammerBidUsd { get; set; }
        public decimal? CurrentTotalInvestmentUsd { get; set; }
        public decimal? CurrentProfitUsd { get; set; }
        public bool? CurrentBidWithinLimit { get; set; }
        public string Formula { get; set; }
        public string CostScope { get; set; }
    }
}
