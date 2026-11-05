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

class NeoVinHttpRegression {
    static void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS "+label);}
    static int Main(string[] args){try{ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;Run(args.Contains("--live")).GetAwaiter().GetResult();return 0;}catch(Exception ex){Console.Error.WriteLine(ex.GetBaseException().Message);return 1;}}
    static async Task Run(bool live){
        using(var config=new HttpConfiguration()){
            config.MessageHandlers.Add(new ApiKeyHandler(new TestAccess()));config.MapHttpAttributeRoutes();config.EnsureInitialized();
            using(var server=new HttpServer(config))using(var client=new HttpClient(server)){
                var url="http://localhost/api/service/neovin";
                using(var response=await client.PostAsync(url,new StringContent("{}",Encoding.UTF8,"application/json")))Check(response.StatusCode==HttpStatusCode.Unauthorized,"NeoVIN requires an authenticated AutoDealer API key");
                client.DefaultRequestHeaders.Authorization=new AuthenticationHeaderValue("Bearer","ad_neovinfixture.synthetic");
                using(var response=await client.PostAsync(url,new StringContent("{}",Encoding.UTF8,"application/json")))Check(response.StatusCode==HttpStatusCode.BadRequest,"NeoVIN rejects missing VIN before a provider call");
                using(var response=await client.GetAsync(url+"/invalid/html"))Check(response.StatusCode==HttpStatusCode.BadRequest,"NeoVIN HTML route validates the VIN");
                if(live){
                    using(var response=await client.PostAsync(url,new StringContent("{\"Vin\":\"4T1BE46K19U342407\"}",Encoding.UTF8,"application/json"))){
                        var text=await response.Content.ReadAsStringAsync();Check(response.StatusCode==HttpStatusCode.OK,"NeoVIN live provider call through authenticated HTTP pipeline");
                        var result=JObject.Parse(text);var raw=JObject.Parse((string)result["rawJson"]);var specs=(JArray)result["specifications"];
                        Check((string)raw["vin"]=="4T1BE46K19U342407"&&JToken.DeepEquals(raw,result["data"]),"NeoVIN returns the complete original provider payload");
                        Check(specs.Count>50&&specs.Any(r=>((string)r["Category"]).StartsWith("Features")),"NeoVIN includes native-grid specification and feature rows");
                        Check(((string)result["html"]).Contains("Toyota")&&((string)result["html"]).Contains("Camry"),"NeoVIN formatted report includes the decoded vehicle");
                        var secret=Environment.GetEnvironmentVariable("MARKETCHECK_API_KEY");Check(string.IsNullOrEmpty(secret)||!text.Contains(secret),"NeoVIN does not expose the provider credential");
                        Check(response.Headers.CacheControl!=null&&response.Headers.CacheControl.NoStore,"NeoVIN responses are not cached");
                        Console.WriteLine("Live NeoVIN: "+raw["year"]+" "+raw["make"]+" "+raw["model"]+" "+raw["trim"]+"; "+specs.Count+" specification rows.");
                    }
                }
            }
        }
    }
    sealed class TestAccess:IApiAccessService {
        public ApiAccessResult BeginRequest(ApiAccessRequest request){return new ApiAccessResult{ResultCode="ok",ClientNumber="neovin-fixture",Scopes="vin:read",MonthlyQuota=10000,MonthlyUsage=1};}
        public void CompleteRequest(Guid id,int code,long duration){}
    }
}
