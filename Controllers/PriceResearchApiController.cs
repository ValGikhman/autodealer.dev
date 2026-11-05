using autodealer.dev.Models;
using autodealer.dev.Services;
using System;
using System.Configuration;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;

namespace autodealer.dev.Controllers {
    [RoutePrefix("api/service/price-research"), Authorize(Roles="vin:read")]
    public sealed class PriceResearchApiController : ApiController {
        [HttpPost, Route("")]
        public Task<IHttpActionResult> Research([FromBody] PriceResearchRequest input, CancellationToken cancellationToken) {
            return Run(input,"report",cancellationToken);
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
