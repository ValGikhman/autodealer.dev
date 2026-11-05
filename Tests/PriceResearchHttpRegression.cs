using autodealer.dev.Models;
using autodealer.dev.Services;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using System.Web.Http;

class PriceResearchHttpRegression {
    static void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS "+label);}
    static int Main(string[] args){try{ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;Run(args.Contains("--live")).GetAwaiter().GetResult();return 0;}catch(Exception ex){Console.Error.WriteLine(ex.GetBaseException().Message);return 1;}}
    static async Task Run(bool live){
        using(var config=new HttpConfiguration()){
            WebApiConfig.Register(config);
            // Use production attribute/conventional routes with isolated API access.
            // The fake prevents database access; live provider calls remain opt-in.
            config.MessageHandlers.Clear();config.MessageHandlers.Add(new ApiKeyHandler(new TestAccess()));config.EnsureInitialized();
            var controllers=config.Services.GetHttpControllerSelector().GetControllerMapping();
            Check(!controllers.Keys.Any(name=>string.Equals(name,"Acquisition",StringComparison.OrdinalIgnoreCase)),"retired acquisition controller is absent before legacy routes are exercised");
            using(var server=new HttpServer(config))using(var client=new HttpClient(server)){
                var url="http://localhost/api/service/price-research";
                var response=await client.PostAsync(url,new StringContent("{}",Encoding.UTF8,"application/json"));
                Check(response.StatusCode==HttpStatusCode.Unauthorized,"price research requires the existing AutoDealer API key");
                client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer","ad_pricefixture.synthetic");
                var retiredUrl="http://localhost/api/service/acquisition/4T1BE46K19U342407/html";
                response=await client.GetAsync(retiredUrl+"?mileage=73929&desiredProfit=3000");
                Check(response.StatusCode==HttpStatusCode.NotFound,"retired acquisition GET route returns 404 without a report controller or provider request");
                response=await client.PostAsync(retiredUrl,new StringContent("{}",Encoding.UTF8,"application/json"));
                Check(response.StatusCode==HttpStatusCode.NotFound,"retired acquisition POST route returns 404 without a report controller or provider request");
                response=await client.PostAsync(url,new StringContent("{\"Vin\":\"4T1BE46K19U342407\",\"Zip\":\"45040\",\"Radius\":100}",Encoding.UTF8,"application/json"));
                Check(response.StatusCode==HttpStatusCode.BadRequest,"HTTP missing mileage rejected before a provider call");
                response=await client.GetAsync(url+"/4T1BE46K19U342407/json?miles=73929&radius=-1");
                Check(response.StatusCode==HttpStatusCode.BadRequest,"DataOne-style price JSON route validates radius");
                response=await client.PostAsync(url,new StringContent("{\"Vin\":\"4T1BE46K19U342407\",\"Miles\":1.5,\"Zip\":\"45040\",\"Radius\":100}",Encoding.UTF8,"application/json"));
                Check(response.StatusCode==HttpStatusCode.BadRequest,"fractional mileage is not silently converted into a provider query");
                if(live){
                    response=await client.PostAsync(url,new StringContent("{\"Vin\":\"4T1BE46K19U342407\",\"Miles\":73929,\"Zip\":\"45040\",\"Radius\":100}",Encoding.UTF8,"application/json"));
                    var text=await response.Content.ReadAsStringAsync();if(response.StatusCode!=HttpStatusCode.OK){var safe=text;var secret=Environment.GetEnvironmentVariable("MARKETCHECK_API_KEY");if(!string.IsNullOrEmpty(secret))safe=safe.Replace(secret,"[redacted]");Console.WriteLine("Live HTTP "+(int)response.StatusCode+": "+safe.Substring(0,Math.Min(1000,safe.Length)));}Check(response.StatusCode==HttpStatusCode.OK,"live MarketCheck request through authenticated API pipeline");
                    var result=JObject.Parse(text);var raw=JObject.Parse((string)result["rawJson"]);var current=raw["comparables"]?["listings"] as JArray??new JArray();
                    Check(current.Count>0&&current.Count==((JArray)result["data"]["comparables"]["listings"]).Count,"live response retains every provider comparable");
                    var html=(string)result["html"];Check(html.Contains("4T1BE46K19U342407")&&html.Contains("Current comparables within radius")&&!html.Contains("<code>4T1BE46K19U342407</code>"),"live report renders the requested VIN and limits comparable tables to local competitors");
                    var key=Environment.GetEnvironmentVariable("MARKETCHECK_API_KEY");Check(string.IsNullOrEmpty(key)||!text.Contains(key),"provider credential is absent from browser response");
                    Check(response.Headers.CacheControl!=null&&response.Headers.CacheControl.NoStore,"price research responses are not cached");
                    Console.WriteLine("Live result: predicted price "+raw["marketcheck_price"]+"; "+current.Count+" comparables; "+(raw["recent_comparables"]?["listings"] as JArray??new JArray()).Count+" recent comparables.");
                }
            }
        }
    }
    sealed class TestAccess:IApiAccessService {
        public ApiAccessResult BeginRequest(ApiAccessRequest request){return new ApiAccessResult{ResultCode="ok",ClientNumber="price-fixture",Scopes="vin:read",MonthlyQuota=10000,MonthlyUsage=1};}
        public void CompleteRequest(Guid id,int code,long duration){}
    }
}
