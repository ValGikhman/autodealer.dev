using autodealer.dev.Models;
using autodealer.dev.Services;
using System;
using System.Configuration;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace autodealer.dev.Controllers {
    [RoutePrefix("api/service/price-research"), Authorize(Roles="vin:read")]
    public sealed class PriceResearchApiController : ApiController {
        private static readonly JsonSerializerSettings BidJsonSettings = new JsonSerializerSettings {
            ContractResolver = new CamelCasePropertyNamesContractResolver()
        };

        [HttpPost, Route("")]
        public Task<IHttpActionResult> Research([FromBody] PriceResearchReportRequest input, CancellationToken cancellationToken) {
            if (input == null) return Run(null, "report", cancellationToken);
            try {
                var request = new PriceResearchRequest {
                    Vin = input.Vin,
                    Miles = input.Miles,
                    Zip = input.Zip ?? (ConfigurationManager.AppSettings["Research:DefaultZip"] ?? "45040"),
                    Radius = input.Radius ?? DefaultInteger("Research:DefaultRadius", 100, 1, 500)
                };
                return Run(request, "report", cancellationToken);
            } catch (ConfigurationErrorsException) {
                return Task.FromResult(Error(HttpStatusCode.ServiceUnavailable, "Price research scope is not configured."));
            }
        }

        [HttpPost, Route("auction-budget")]
        public IHttpActionResult AuctionBudget([FromBody] AuctionBidRequest input) {
            var accessError = CheckStaffAccess();
            if (accessError != null) return accessError;
            if (!ModelState.IsValid) return Error(HttpStatusCode.BadRequest, "Enter valid auction amounts.");
            try {
                var result = new AuctionBidService().Calculate(input);
                return Document(JsonConvert.SerializeObject(result, BidJsonSettings), "application/json");
            } catch (ArgumentException ex) {
                return Error(HttpStatusCode.BadRequest, ex.Message);
            } catch (AuctionBidConfigurationException) {
                return Error(HttpStatusCode.ServiceUnavailable, "Auction costs are not configured.");
            }
        }

        [HttpPost, Route("auction-appraisal")]
        public Task<IHttpActionResult> AuctionAppraisal([FromBody] JObject input, CancellationToken cancellationToken) {
            return RunAppraisal(input, (research, token) => AuctionAppraisalService.AppraiseAsync(research,
                (decimal?)input["resaleUsd"], (decimal?)input["hammerBidUsd"], token), cancellationToken);
        }

        [HttpPost, Route("market-price")]
        public Task<IHttpActionResult> MarketPrice([FromBody] JObject input, CancellationToken cancellationToken) {
            return RunAppraisal(input, AuctionAppraisalService.GetMarketPriceAsync, cancellationToken);
        }

        [HttpPost, Route("market-inventory")]
        public Task<IHttpActionResult> MarketInventory([FromBody] JObject input, CancellationToken cancellationToken) {
            return RunAppraisal(input, AuctionAppraisalService.CountMarketInventoryAsync, cancellationToken);
        }

        [HttpPost, Route("market-listings")]
        public Task<IHttpActionResult> MarketListings([FromBody] JObject input, CancellationToken cancellationToken) {
            return RunAppraisal(input, (scope, token) => AuctionAppraisalService.ListMarketAsync(scope, input, token),
                cancellationToken, allowVehicleName: true);
        }

        private async Task<IHttpActionResult> RunAppraisal(JObject input, Func<PriceResearchRequest, CancellationToken, Task<JObject>> lookup,
            CancellationToken cancellationToken, bool allowVehicleName = false) {
            var accessError = CheckStaffAccess();
            if (accessError != null) return accessError;

            try {
                if (!ModelState.IsValid || input == null
                    || (input["miles"] != null && input["miles"].Type != JTokenType.Integer)
                    || (input["radius"] != null && input["radius"].Type != JTokenType.Integer)
                    || ((!allowVehicleName || input["vin"] != null) && input["vin"]?.Type != JTokenType.String)
                    || (input["zip"] != null && input["zip"].Type != JTokenType.String)
                    || !IsOptionalMoney(input["resaleUsd"]) || !IsOptionalMoney(input["hammerBidUsd"]))
                    return Error(HttpStatusCode.BadRequest, "Enter a VIN, whole-number mileage and a valid ZIP and radius.");
                var research = new PriceResearchRequest {
                    Vin = (string)input["vin"],
                    Miles = (int?)input["miles"] ?? DefaultInteger("Research:DefaultMileage", 50000, 0, 2000000),
                    Zip = (string)input["zip"] ?? (ConfigurationManager.AppSettings["Research:DefaultZip"] ?? "45040"),
                    Radius = (int?)input["radius"] ?? DefaultInteger("Research:DefaultRadius", 100, 1, 500)
                };
                if (allowVehicleName && input["vin"] == null) research.NormalizeSearchScope();
                else research.NormalizeAndValidate();
                var result = await lookup(research, cancellationToken);
                if (input["miles"] == null) result["usedDefaultMileage"] = true;
                return Document(result.ToString(Newtonsoft.Json.Formatting.None), "application/json");
            } catch (PriceResearchException ex) { return Error(ex.StatusCode, ex.Message); }
            catch (OperationCanceledException) { return Error(HttpStatusCode.GatewayTimeout, "The appraisal timed out. Please try again."); }
            catch (Exception ex) when (ex is ConfigurationErrorsException || ex is AuctionBidConfigurationException) {
                return Error(HttpStatusCode.ServiceUnavailable, "Auction appraisal is not configured.");
            } catch (Exception ex) when (ex is HttpRequestException || ex is NeoVinException || ex is Newtonsoft.Json.JsonException) {
                return Error(HttpStatusCode.BadGateway, "MarketCheck could not complete the appraisal.");
            } catch (Exception ex) when (ex is ArgumentException || ex is OverflowException || ex is FormatException) {
                return Error(HttpStatusCode.BadRequest, "Enter valid appraisal inputs and USD amounts.");
            }
        }

        private IHttpActionResult CheckStaffAccess() {
            var allowedKeyId = ConfigurationManager.AppSettings["Research:RabinovichKeyId"]?.Trim();
            var authorization = Request.Headers.Authorization;
            if (!string.Equals(ConfigurationManager.AppSettings["ApiSecurity:Enabled"], "true", StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(allowedKeyId))
                return Error(HttpStatusCode.ServiceUnavailable, "Auction appraisal is unavailable.");
            if (User?.Identity?.AuthenticationType != "ApiKey" || Request.Headers.Contains(ClientResearchSession.WorkspaceHeader)
                || !string.Equals(authorization?.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
                || authorization.Parameter?.StartsWith(allowedKeyId + ".", StringComparison.Ordinal) != true)
                return Error(HttpStatusCode.Forbidden, "This API key cannot use auction appraisal.");
            return null;
        }

        private static int DefaultInteger(string setting, int fallback, int minimum, int maximum) {
            var value = ConfigurationManager.AppSettings[setting];
            if (value == null) return fallback;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
                && number >= minimum && number <= maximum) return number;
            throw new ConfigurationErrorsException("Invalid research default: " + setting);
        }

        private static bool IsOptionalMoney(JToken value) {
            return value == null || value.Type == JTokenType.Null
                || value.Type == JTokenType.Integer || value.Type == JTokenType.Float;
        }

        [HttpGet, Route("{vin}/html")]
        public Task<IHttpActionResult> Html(string vin, int? miles=null, string zip="45040", int radius=100, CancellationToken cancellationToken=default(CancellationToken)) {
            return Run(new PriceResearchRequest{Vin=vin,Miles=miles,Zip=zip,Radius=radius},"html",cancellationToken);
        }

        [HttpGet, Route("{vin}/json")]
        public Task<IHttpActionResult> Json(string vin, int? miles=null, string zip="45040", int radius=100, CancellationToken cancellationToken=default(CancellationToken)) {
            return Run(new PriceResearchRequest{Vin=vin,Miles=miles,Zip=zip,Radius=radius},"json",cancellationToken);
        }

        private async Task<IHttpActionResult> Run(PriceResearchRequest input, string format, CancellationToken cancellationToken) {
            if(!string.Equals(ConfigurationManager.AppSettings["ApiSecurity:Enabled"],"true",StringComparison.OrdinalIgnoreCase))
                return Error(HttpStatusCode.ServiceUnavailable,"API authentication must be enabled for price research.");
            try {
                if(input==null)throw new ArgumentException("Enter a VIN, mileage, ZIP code and radius.");
                input.NormalizeAndValidate();
                // Binding errors (for example a fractional mileage) must not be
                // silently converted into a different billable provider request.
                foreach(var state in ModelState) {
                    if(state.Value.Errors.Count>0&&state.Key.IndexOf("Vin",StringComparison.OrdinalIgnoreCase)<0)
                        return Error(HttpStatusCode.BadRequest,"Enter valid whole-number mileage and radius, a VIN and a five-digit ZIP code.");
                }
                if(format!="report") {
                    var document=await new PriceResearchService().ResearchAsync(input,cancellationToken);
                    return format=="html" ? Document(PriceResearchHtmlRenderer.Render(document),"text/html") : Document(document.RawJson,"application/json");
                }
                PriceResearchResult result;
                MarketDemandResult demand;
                using(var combined=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)) {
                    var priceTask=new PriceResearchService().ResearchAsync(input,combined.Token);
                    var demandTask=new MarketDemandService().GetAsync(input,combined.Token);
                    try { result=await priceTask; }
                    catch { combined.Cancel(); try { await demandTask; } catch { } throw; }
                    demand=await demandTask;
                }
                var body=new Newtonsoft.Json.Linq.JObject {
                    ["html"]=PriceResearchHtmlRenderer.Render(result),
                    ["rawJson"]=result.RawJson,
                    ["data"]=result.Data,
                    ["request"]=Newtonsoft.Json.Linq.JObject.FromObject(new{vin=input.Vin,miles=input.Miles,zip=input.Zip,radius=input.Radius,dealer_type="independent"}),
                    ["requestedAtUtc"]=result.RequestedAtUtc.ToString("o")
                };
                body["demand"]=Newtonsoft.Json.Linq.JObject.FromObject(new { status=demand.Status,daysSupply=demand.DaysSupply,activeCount=demand.ActiveCount,sales45Days=demand.Sales45Days,checkedAtUtc=demand.CheckedAtUtc,message=demand.Message });
                return Document(body.ToString(Newtonsoft.Json.Formatting.None),"application/json");
            } catch(ArgumentException ex) { return Error(HttpStatusCode.BadRequest,ex.Message); }
            catch(PriceResearchException ex) { return Error(ex.StatusCode,ex.Message); }
            catch(OperationCanceledException) { return Error(HttpStatusCode.GatewayTimeout,"Price research was stopped or timed out. Please retry."); }
        }

        private IHttpActionResult Error(HttpStatusCode status,string message) {
            return ResponseMessage(Response(status,new Newtonsoft.Json.Linq.JObject{["message"]=message}.ToString(),"application/json"));
        }
        private IHttpActionResult Document(string body,string type) {return ResponseMessage(Response(HttpStatusCode.OK,body,type));}
        private static HttpResponseMessage Response(HttpStatusCode status,string body,string type) {
            var response=new HttpResponseMessage(status){Content=new StringContent(body,Encoding.UTF8,type)};
            response.Headers.CacheControl=new CacheControlHeaderValue{NoStore=true};
            response.Headers.TryAddWithoutValidation("X-Content-Type-Options","nosniff");
            response.Headers.TryAddWithoutValidation("Referrer-Policy","no-referrer");
            if(type=="text/html")response.Headers.TryAddWithoutValidation("Content-Security-Policy","default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'; frame-ancestors 'self'");
            return response;
        }
    }
}
