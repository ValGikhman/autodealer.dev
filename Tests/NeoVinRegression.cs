using autodealer.dev.Models;
using autodealer.dev.Services;
using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

class NeoVinRegression {
    private const string Vin="4T1BE46K19U342407";
    private const string Fixture="{\"vin\":\"4T1BE46K19U342407\",\"year\":2009,\"make\":\"Toyota\",\"model\":\"Camry\",\"trim\":\"LE\",\"engine\":{\"liters\":2.4,\"cylinders\":4},\"features\":{\"STANDARD\":[{\"category\":\"Safety\",\"feature_type\":\"Airbags\",\"description\":\"Side curtain airbags\",\"provider_detail\":\"Retained\"}]},\"installed_equipment\":{\"STANDARD\":[{\"category\":\"Interior\",\"item\":\"Seats\",\"attribute\":\"Heating\",\"location\":\"driver\",\"value\":\"standard\"}]},\"options\":[\"One\",\"Two\"],\"empty\":[],\"warranty\":{\"transferable\":false,\"notes\":null}}";
    static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
    static int Main(string[] args){try{ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;Run(args).GetAwaiter().GetResult();return 0;}catch(Exception ex){Console.Error.WriteLine(ex.GetBaseException().Message);return 1;}}
    static async Task Run(string[] args){
        var input=new NeoVinRequest{Vin=" 4t1be46k19u342407 "};input.NormalizeAndValidate();Check(input.Vin==Vin,"NeoVIN accepts normalized VIN and rejects unsupported characters");
        var invalid=false;try{NeoVinRequest.NormalizeVin("I2345678901234567");}catch(ArgumentException){invalid=true;}Check(invalid,"invalid VIN is rejected before provider calls");
        var result=NeoVinResult.Create(Vin,Fixture);Check(result.RawJson==Fixture&&JToken.DeepEquals(JObject.Parse(Fixture),result.Data)&&result.ToJsonObject()["data"]!=null,"full nested provider payload is retained unchanged");
        Check(result.Specifications.Any(r=>r.Category=="Engine"&&r.Field=="Liters"&&r.Value=="2.4")&&result.Specifications.Any(r=>r.Field=="Item 2"&&r.Value=="Two"),"nested objects and arrays produce readable specification rows");
        Check(result.Specifications.Any(r=>r.Category.Contains("Safety")&&r.Field=="Airbags"&&r.Value=="Side curtain airbags")&&result.Specifications.Any(r=>r.Value=="Retained"),"feature category and description are readable while extra provider fields remain");
        Check(result.Specifications.Any(r=>r.Field=="Seats · Heating (driver)"&&r.Value=="standard"),"installed equipment preserves item, attribute and location");
        Check(result.Specifications.Any(r=>r.Value=="[]")&&result.Specifications.Any(r=>r.Value=="No")&&result.Specifications.Any(r=>r.Value=="Not provided"),"empty, boolean and null values remain represented");
        var html=NeoVinHtmlRenderer.Render(result);Check(html.Contains("2009 Toyota Camry LE")&&html.Contains("Side curtain airbags")&&html.Contains("Complete MarketCheck response"),"formatted report includes vehicle identity and every specification category");
        var hostile=NeoVinResult.Create(Vin,Fixture.Replace("Toyota","<script>alert(1)</script>"));html=NeoVinHtmlRenderer.Render(hostile);Check(!html.Contains("<script>alert(1)</script>")&&html.Contains("&lt;script&gt;"),"provider text is HTML encoded in summaries, rows and raw data");
        invalid=false;try{NeoVinResult.Create(Vin,Fixture.Replace(Vin,"1HGCM82633A004352"));}catch(NeoVinException){invalid=true;}Check(invalid,"a response for another VIN is never presented as the requested vehicle");
        invalid=false;try{NeoVinResult.Create(Vin,"{\"message\":\"unavailable\"}");}catch(NeoVinException){invalid=true;}Check(invalid,"provider error objects are not misrepresented as decoded specifications");
        var handler=new Handler{Response=new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(Fixture)}};
        using(var client=new HttpClient(handler)){
            var fetched=await new NeoVinService(client,"fixture-key").DecodeAsync(" "+Vin.ToLowerInvariant()+" ",CancellationToken.None);
            Check(handler.Uri.AbsolutePath=="/v2/decode/car/neovin/"+Vin+"/specs"&&handler.Uri.Host=="api.marketcheck.com"&&handler.Uri.Query=="?api_key=fixture-key"&&fetched.Specifications.Count>0,"NeoVIN sends the exact fixed provider URL with a server-side key");
        }
        foreach(var status in new[]{HttpStatusCode.Unauthorized,HttpStatusCode.Forbidden,(HttpStatusCode)429}){
            handler=new Handler{Response=new HttpResponseMessage(status){Content=new StringContent("fixture-key private provider details")}};
            using(var client=new HttpClient(handler)){NeoVinException failure=null;try{await new NeoVinService(client,"fixture-key").DecodeAsync(Vin,CancellationToken.None);}catch(NeoVinException ex){failure=ex;}Check(failure!=null&&!failure.Message.Contains("fixture-key")&&!failure.Message.Contains("private provider")&&(status!=(HttpStatusCode)429||failure.StatusCode==(HttpStatusCode)429),"NeoVIN safely reports provider HTTP "+(int)status);}
        }
        handler=new Handler{Response=new HttpResponseMessage(HttpStatusCode.Found){Content=new StringContent("redirect")}};handler.Response.Headers.Location=new Uri("https://other.example/");
        using(var client=new HttpClient(handler)){invalid=false;try{await new NeoVinService(client,"fixture-key").DecodeAsync(Vin,CancellationToken.None);}catch(NeoVinException){invalid=true;}Check(invalid&&handler.Calls==1,"provider redirects never forward the API credential");}
        handler=new Handler{Response=new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(Fixture.Replace("Retained","fixture-key"))}};
        using(var client=new HttpClient(handler)){var redacted=await new NeoVinService(client,"fixture-key").DecodeAsync(Vin,CancellationToken.None);Check(!redacted.RawJson.Contains("fixture-key")&&redacted.RawJson.Contains("[redacted]"),"unexpected provider credential echoes are redacted");}
        handler=new Handler{Response=new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(new WaitingStream())}};
        using(var client=new HttpClient(handler))using(var cancel=new CancellationTokenSource(40)){invalid=false;try{await new NeoVinService(client,"fixture-key").DecodeAsync(Vin,cancel.Token);}catch(OperationCanceledException){invalid=true;}Check(invalid,"NeoVIN cancellation stops response-body reads");}
        if(args.Length==1){var sample=NeoVinResult.Create(Vin,File.ReadAllText(args[0]));Check(sample.Specifications.Count>100&&sample.VehicleTitle=="2009 Toyota Camry LE","live sample preserves its complete specification set");Console.WriteLine("Live sample: "+sample.Specifications.Count+" specification rows across "+sample.Specifications.Select(r=>r.Category).Distinct().Count()+" categories.");}
    }
    sealed class Handler:HttpMessageHandler {public HttpResponseMessage Response;public Uri Uri;public int Calls;protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Uri=request.RequestUri;Calls++;return Task.FromResult(Response);}}
    sealed class WaitingStream:MemoryStream {public override async Task<int> ReadAsync(byte[] buffer,int offset,int count,CancellationToken ct){await Task.Delay(Timeout.Infinite,ct);return 0;}}
}
