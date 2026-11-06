using Newtonsoft.Json.Linq;
using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;

namespace autodealer.dev.Models {
    public sealed class PriceResearchReportRequest {
        [Required, RegularExpression("^[A-HJ-NPR-Z0-9]{17}$")]
        public string Vin { get; set; }
        [Required, Range(0, 2000000)]
        public int? Miles { get; set; }
        [Newtonsoft.Json.JsonProperty(Required = Newtonsoft.Json.Required.DisallowNull)]
        [RegularExpression("^[0-9]{5}$")]
        public string Zip { get; set; }
        [Newtonsoft.Json.JsonProperty(Required = Newtonsoft.Json.Required.DisallowNull)]
        [Range(1, 500)]
        public int? Radius { get; set; }
    }

    public sealed class PriceResearchRequest {
        [Required, RegularExpression("^[A-HJ-NPR-Z0-9]{17}$", ErrorMessage="Enter a valid 17-character VIN (no I, O or Q).")]
        public string Vin { get; set; }
        [Required, Range(0, 2000000, ErrorMessage="Mileage must be between 0 and 2,000,000.")]
        public int? Miles { get; set; }
        [Required, RegularExpression("^[0-9]{5}$", ErrorMessage="Enter a five-digit ZIP code.")]
        public string Zip { get; set; } = "45040";
        [Range(1, 500, ErrorMessage="Radius must be between 1 and 500 miles.")]
        public int Radius { get; set; } = 100;

        public void NormalizeAndValidate() {
            Vin=(Vin??string.Empty).Trim().ToUpperInvariant();
            if(!Regex.IsMatch(Vin,"^[A-HJ-NPR-Z0-9]{17}$"))throw new ArgumentException("Enter a valid 17-character VIN (no I, O or Q).");
            NormalizeSearchScope();
        }

        internal void NormalizeSearchScope() {
            Zip = (Zip ?? string.Empty).Trim();
            if(!Miles.HasValue||Miles<0||Miles>2000000)throw new ArgumentException("Mileage must be between 0 and 2,000,000.");
            if(!Regex.IsMatch(Zip,"^[0-9]{5}$"))throw new ArgumentException("Enter a five-digit ZIP code.");
            if(Radius<1||Radius>500)throw new ArgumentException("Radius must be between 1 and 500 miles.");
        }
    }

    public sealed class PriceResearchResult {
        public PriceResearchRequest Request { get; private set; }
        public DateTime RequestedAtUtc { get; private set; }
        public string RawJson { get; private set; }
        public JObject Data { get; private set; }
        public decimal? PredictedPrice { get {return Number(Data["marketcheck_price"]);} }
        public JArray Comparables { get {return Data["comparables"]?["listings"] as JArray??new JArray();} }
        public JArray RecentComparables { get {return Data["recent_comparables"]?["listings"] as JArray??new JArray();} }
        public int LocalCompetitorCount { get {return Comparables.OfType<JObject>().Count(IsLocalCompetitor);} }
        public int OutsideRadiusCount { get {return Comparables.OfType<JObject>().Count(row=>Distance(row)>Request.Radius);} }
        public int SubjectListingCount { get {return Comparables.OfType<JObject>().Count(IsSubject);} }

        public static PriceResearchResult Create(PriceResearchRequest request,string json) {
            if(request==null)throw new ArgumentNullException("request");request.NormalizeAndValidate();
            JObject data;
            try{data=JObject.Parse(json??"");}catch(Newtonsoft.Json.JsonException){throw new PriceResearchException(HttpStatusCode.BadGateway,"MarketCheck returned an invalid response.");}
            if(data["marketcheck_price"]==null&&data["comparables"]==null&&data["recent_comparables"]==null)throw new PriceResearchException(HttpStatusCode.BadGateway,"MarketCheck returned no price research data.");
            return new PriceResearchResult {Request=new PriceResearchRequest{Vin=request.Vin,Miles=request.Miles,Zip=request.Zip,Radius=request.Radius},RequestedAtUtc=DateTime.UtcNow,RawJson=json,Data=data};
        }
        public bool IsSubject(JObject row){return string.Equals((string)row["vin"],Request.Vin,StringComparison.OrdinalIgnoreCase);}
        public bool IsLocalCompetitor(JObject row){var distance=Distance(row);return !IsSubject(row)&&distance.HasValue&&distance>=0&&distance<=Request.Radius;}
        public static decimal? Distance(JObject row){return Number(row["dist"]);}
        public static decimal? Number(JToken token){decimal n;return token!=null&&token.Type!=JTokenType.Null&&decimal.TryParse(token.ToString(),System.Globalization.NumberStyles.Number,System.Globalization.CultureInfo.InvariantCulture,out n)?(decimal?)n:null;}
        public JObject ToJsonObject(){return new JObject{["vin"]=Request.Vin,["miles"]=Request.Miles,["zip"]=Request.Zip,["radius"]=Request.Radius,["dealer_type"]="independent",["researchedAtUtc"]=RequestedAtUtc.ToString("o"),["data"]=Data.DeepClone()};}
    }

    public sealed class PriceResearchException:Exception {
        public HttpStatusCode StatusCode {get;private set;}
        public PriceResearchException(HttpStatusCode statusCode,string message):base(message){StatusCode=statusCode;}
    }
}
