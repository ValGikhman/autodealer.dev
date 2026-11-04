using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
namespace autodealer.dev.Models {
    public sealed class AcquisitionComparableRequest {
        [Range(0, 2000000)] public int Mileage { get; set; }
        [Range(0, 1000000)] public decimal DesiredProfit { get; set; } = 3000m;
        [Required] public List<AcquisitionComparableListing> Comparables { get; set; }
        public Newtonsoft.Json.Linq.JObject Underwriting { get; set; }
    }
    public sealed class AcquisitionComparableListing {
        [Required, StringLength(200)] public string Dealer { get; set; }
        [Required, StringLength(200)] public string Location { get; set; }
        [Range(0, 250)] public decimal DistanceMiles { get; set; }
        [StringLength(17), RegularExpression("^[A-HJ-NPR-Z0-9]{17}$")] public string Vin { get; set; }
        [Range(1886, 2200)] public int? Year { get; set; }
        [StringLength(100)] public string Make { get; set; }
        [StringLength(100)] public string Model { get; set; }
        [StringLength(100)] public string SeriesTrim { get; set; }
        [Required, StringLength(400)] public string Vehicle { get; set; }
        [Range(0, 2000000)] public int Mileage { get; set; }
        [Range(1, 2000000)] public decimal AskingPrice { get; set; }
        [Required, RegularExpression("^(clean|unknown|salvage|rebuilt)$")] public string TitleStatus { get; set; }
        [StringLength(1000)] public string PriceConditions { get; set; }
        [Required, StringLength(2000)] public string ListingUrl { get; set; }
        [Required] public DateTime? ObservedAtUtc { get; set; }
    }
}
