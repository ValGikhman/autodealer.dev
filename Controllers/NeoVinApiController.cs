using autodealer.dev.Models;
using autodealer.dev.Services;
using Newtonsoft.Json.Linq;
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
    [RoutePrefix("api/service/neovin"),Authorize(Roles="vin:read")]
    public sealed class NeoVinApiController : ApiController {
        [HttpPost,Route("")]
        public Task<IHttpActionResult> Decode([FromBody] NeoVinRequest input,CancellationToken cancellationToken){return Run(input==null?null:input.Vin,"report",cancellationToken);}
        [HttpGet,Route("{vin}/html")]
        public Task<IHttpActionResult> Html(string vin,CancellationToken cancellationToken){return Run(vin,"html",cancellationToken);}
        [HttpGet,Route("{vin}/json")]
        public Task<IHttpActionResult> Json(string vin,CancellationToken cancellationToken){return Run(vin,"json",cancellationToken);}

        private async Task<IHttpActionResult> Run(string vin,string format,CancellationToken token){
            if(!string.Equals(ConfigurationManager.AppSettings["ApiSecurity:Enabled"],"true",StringComparison.OrdinalIgnoreCase))return Error(HttpStatusCode.ServiceUnavailable,"API authentication must be enabled for VIN decoding.");
            try{
                var result=await new NeoVinService().DecodeAsync(vin,token);
                if(format=="html")return Document(NeoVinHtmlRenderer.Render(result),"text/html");
                if(format=="json")return Document(result.RawJson,"application/json");
                return Document(new JObject{["vin"]=result.Vin,["decodedAtUtc"]=result.DecodedAtUtc.ToString("o"),["html"]=NeoVinHtmlRenderer.Render(result),["rawJson"]=result.RawJson,["data"]=result.Data,["specifications"]=JArray.FromObject(result.Specifications)}.ToString(Newtonsoft.Json.Formatting.None),"application/json");
            }catch(ArgumentException ex){return Error(HttpStatusCode.BadRequest,ex.Message);}
            catch(NeoVinException ex){return Error(ex.StatusCode,ex.Message);}
            catch(OperationCanceledException){return Error(HttpStatusCode.GatewayTimeout,"VIN decoding was stopped or timed out. Please retry.");}
        }
        private IHttpActionResult Error(HttpStatusCode status,string message){return ResponseMessage(Response(status,new JObject{["message"]=message}.ToString(),"application/json"));}
        private IHttpActionResult Document(string body,string type){return ResponseMessage(Response(HttpStatusCode.OK,body,type));}
        private static HttpResponseMessage Response(HttpStatusCode status,string body,string type){
            var response=new HttpResponseMessage(status){Content=new StringContent(body,Encoding.UTF8,type)};
            response.Headers.CacheControl=new CacheControlHeaderValue{NoStore=true};response.Headers.TryAddWithoutValidation("X-Content-Type-Options","nosniff");response.Headers.TryAddWithoutValidation("Referrer-Policy","no-referrer");
            if(type=="text/html")response.Headers.TryAddWithoutValidation("Content-Security-Policy","default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'; frame-ancestors 'self'");
            return response;
        }
    }
}
