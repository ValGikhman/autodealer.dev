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

class PriceResearchRegression {
    private const string Fixture="{\"marketcheck_price\":9810,\"comparables\":{\"num_found\":4,\"listings\":[{\"vin\":\"4T1BE46K19U342407\",\"year\":2009,\"make\":\"Toyota\",\"model\":\"Camry\",\"trim\":\"LE\",\"price\":11499,\"miles\":73929,\"dist\":8.31,\"dealer_name\":\"GTX\",\"vdp_url\":\"https://dealer.example/stock/123\"},{\"vin\":\"4T4BE46K19R107494\",\"price\":8069,\"miles\":136369,\"dist\":6.19,\"dealer_name\":\"<script>alert(1)</script>\",\"vdp_url\":\"javascript:alert(1)\"},{\"vin\":\"4T4BE46K59R124217\",\"price\":1,\"dist\":996.6},{\"vin\":\"4T1BE46K49U909628\",\"price\":1}]},\"recent_comparables\":{\"num_found\":1,\"listings\":[{\"vin\":\"4T1BE46K99U272199\",\"price\":5898,\"miles\":173454,\"dist\":12.4}]},\"extra_data\":{\"preserved\":true}}";
    private static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
    private static PriceResearchRequest Request(){return new PriceResearchRequest{Vin=" 4t1be46k19u342407 ",Miles=73929};}
    static int Main(){try{Run().GetAwaiter().GetResult();return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
    private static async Task Run(){
        var request=Request();request.NormalizeAndValidate();Check(request.Vin=="4T1BE46K19U342407"&&request.Zip=="45040"&&request.Radius==100,"request normalizes VIN and defaults editable ZIP/radius");
        var invalid=Request();invalid.Miles=null;bool rejected=false;try{invalid.NormalizeAndValidate();}catch(ArgumentException){rejected=true;}Check(rejected,"missing mileage fails before provider call");
        var result=PriceResearchResult.Create(request,Fixture);Check(result.Comparables.Count==4&&result.RecentComparables.Count==1&&result.Data["extra_data"]!=null&&result.RawJson==Fixture,"complete provider payload and both comparable sets are retained");
        Check(result.LocalCompetitorCount==1&&result.SubjectListingCount==1&&result.OutsideRadiusCount==1,"local statistics exclude own VIN, out-of-radius and unknown distances");
        request.Radius=500;Check(result.Request.Radius==100,"result snapshots original request parameters");
        var html=PriceResearchHtmlRenderer.Render(result);Check(html.Contains("$9,810")&&html.Contains("Current within radius")&&html.Contains("Recent within radius")&&html.Contains("$8,069")&&!html.Contains("Comparable scope")&&!html.Contains("4T4BE46K59R124217")&&!html.Contains("4T1BE46K49U909628")&&!html.Contains("<code>4T1BE46K19U342407</code>"),"formatted report keeps provider prediction and displays only local competitors");
        Check(!html.Contains("<script>alert(1)</script>")&&!html.Contains("href=\"javascript:")&&html.Contains("&lt;script&gt;"),"provider text is encoded and unsafe listing schemes are rejected");
        Check(html.Contains("4T1BE46K99U272199")&&!html.Contains("Complete MarketCheck response")&&!html.Contains("Provider statistics; full returned scope")&&result.RawJson==Fixture,"recent local listings render while full provider data stays in the separate JSON payload");
        var boundaryData=JObject.Parse(Fixture);
        var current=(JArray)boundaryData["comparables"]["listings"];
        current.Add(new JObject{{"vin","4T1BE46K19U347801"},{"price",6068},{"dist",0},{"dealer_name","CURRENT_ZERO_DISTANCE"}});
        current.Add(new JObject{{"vin","4T1BE46K19U347802"},{"price",10001},{"dist",100},{"dealer_name","CURRENT_RADIUS_EDGE"}});
        current.Add(new JObject{{"vin","4T1BE46K19U347803"},{"price",999999},{"dist",100.001m},{"dealer_name","CURRENT_BEYOND_RADIUS"}});
        current.Add(new JObject{{"vin","4T1BE46K19U347804"},{"price",999999},{"dist",-1},{"dealer_name","CURRENT_NEGATIVE_DISTANCE"}});
        var recent=(JArray)boundaryData["recent_comparables"]["listings"];
        recent.Add(new JObject{{"vin",result.Request.Vin},{"price",999999},{"dist",0},{"dealer_name","RECENT_SUBJECT"}});
        recent.Add(new JObject{{"vin","4T1BE46K19U347805"},{"price",5000},{"dist",0},{"dealer_name","RECENT_ZERO_DISTANCE"}});
        recent.Add(new JObject{{"vin","4T1BE46K19U347806"},{"price",7000},{"dist",100},{"dealer_name","RECENT_RADIUS_EDGE"}});
        recent.Add(new JObject{{"vin","4T1BE46K19U347807"},{"price",999999},{"dist",100.001m},{"dealer_name","RECENT_BEYOND_RADIUS"}});
        recent.Add(new JObject{{"vin","4T1BE46K19U347808"},{"price",999999},{"dealer_name","RECENT_UNKNOWN_DISTANCE"}});
        recent.Add(new JObject{{"vin","4T1BE46K19U347809"},{"price",999999},{"dist",-1},{"dealer_name","RECENT_NEGATIVE_DISTANCE"}});
        boundaryData["comparables"]["stats"]=new JObject{{"price",new JObject{{"mean",999999}}}};
        var boundaryRaw=boundaryData.ToString();var boundary=PriceResearchResult.Create(result.Request,boundaryRaw);var boundaryHtml=PriceResearchHtmlRenderer.Render(boundary);
        Check(boundaryHtml.Contains("CURRENT_ZERO_DISTANCE")&&boundaryHtml.Contains("CURRENT_RADIUS_EDGE")&&boundaryHtml.Contains("RECENT_ZERO_DISTANCE")&&boundaryHtml.Contains("RECENT_RADIUS_EDGE")&&!boundaryHtml.Contains("CURRENT_BEYOND_RADIUS")&&!boundaryHtml.Contains("CURRENT_NEGATIVE_DISTANCE")&&!boundaryHtml.Contains("RECENT_BEYOND_RADIUS")&&!boundaryHtml.Contains("RECENT_UNKNOWN_DISTANCE")&&!boundaryHtml.Contains("RECENT_NEGATIVE_DISTANCE")&&!boundaryHtml.Contains("RECENT_SUBJECT"),"both rendered tables include zero and exact-radius distances and exclude beyond-radius, negative, unknown and subject rows");
        Check(boundaryHtml.Contains("$8,046")&&!boundaryHtml.Contains("$999,999")&&!boundaryHtml.Contains("Current comparable statistics")&&boundaryHtml.IndexOf("CURRENT_ZERO_DISTANCE",StringComparison.Ordinal)<boundaryHtml.IndexOf("CURRENT_RADIUS_EDGE",StringComparison.Ordinal),"formatted local mean excludes broader provider scope and table rows sort by distance");
        Check(boundary.RawJson==boundaryRaw&&boundary.Comparables.Count==8&&boundary.RecentComparables.Count==7&&boundary.Data["comparables"]["stats"]["price"]["mean"].Value<int>()==999999,"rendering never removes or rewrites the complete API response");
        var emptyData=JObject.Parse(Fixture);emptyData["comparables"]["listings"]=new JArray(current.OfType<JObject>().Where(r=>!boundary.IsLocalCompetitor(r)));emptyData["recent_comparables"]["listings"]=new JArray(recent.OfType<JObject>().Where(r=>!boundary.IsLocalCompetitor(r)));
        var emptyHtml=PriceResearchHtmlRenderer.Render(PriceResearchResult.Create(result.Request,emptyData.ToString()));Check(!emptyHtml.Contains("<tbody>")&&emptyHtml.Contains("No comparables within the requested radius are available"),"empty local result sets show an empty state instead of broader listings");
        var handler=new Handler{Response=new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(Fixture)}};
        using(var http=new HttpClient(handler)){
            var fetched=await new PriceResearchService(http,"fixture-key").ResearchAsync(Request(),CancellationToken.None);
            var query=System.Web.HttpUtility.ParseQueryString(handler.Uri.Query);
            Check(query["vin"]=="4T1BE46K19U342407"&&query["miles"]=="73929"&&query["zip"]=="45040"&&query["radius"]=="100"&&query["dealer_type"]=="independent"&&handler.Uri.Host=="api.marketcheck.com","provider request sends the exact VIN/mileage/dealer/location pattern");
        }
        handler=new Handler{Response=new HttpResponseMessage(HttpStatusCode.Forbidden){Content=new StringContent("forbidden fixture-key https://private.example/?api_key=fixture-key")}};
        using(var http=new HttpClient(handler)){
            PriceResearchException error=null;try{await new PriceResearchService(http,"fixture-key").ResearchAsync(Request(),CancellationToken.None);}catch(PriceResearchException e){error=e;}
            Check(error!=null&&error.StatusCode==HttpStatusCode.BadGateway&&!error.Message.Contains("fixture-key")&&!error.Message.Contains("private.example"),"provider access errors are safe and do not disclose credentials or response bodies");
        }
        handler=new Handler{Response=new HttpResponseMessage(HttpStatusCode.Found){Content=new StringContent("redirect")}};handler.Response.Headers.Location=new Uri("https://other.example/");
        using(var http=new HttpClient(handler)){
            rejected=false;try{await new PriceResearchService(http,"fixture-key").ResearchAsync(Request(),CancellationToken.None);}catch(PriceResearchException){rejected=true;}Check(rejected&&handler.Calls==1,"provider redirects are rejected rather than transmitting credentials elsewhere");
        }
        handler=new Handler{Response=new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(Fixture.Replace("preserved\":true","preserved\":\"fixture-key\""))}};
        using(var http=new HttpClient(handler)){var echoed=await new PriceResearchService(http,"fixture-key").ResearchAsync(Request(),CancellationToken.None);Check(!echoed.RawJson.Contains("fixture-key")&&echoed.RawJson.Contains("[redacted]"),"unexpected credential echoes are removed from browser/download data");}
        handler=new Handler{Response=new HttpResponseMessage(HttpStatusCode.OK){Content=new StreamContent(new WaitingStream())}};
        using(var http=new HttpClient(handler))using(var stop=new CancellationTokenSource(40)){
            rejected=false;try{await new PriceResearchService(http,"fixture-key").ResearchAsync(Request(),stop.Token);}catch(OperationCanceledException){rejected=true;}Check(rejected,"cancellation interrupts response-body reads after headers arrive");
        }
    }
    private sealed class Handler:HttpMessageHandler {
        public Uri Uri;public int Calls;public HttpResponseMessage Response;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){Uri=request.RequestUri;Calls++;return Task.FromResult(Response);}
    }
    private sealed class WaitingStream:MemoryStream {
        public override async Task<int> ReadAsync(byte[] buffer,int offset,int count,CancellationToken ct){await Task.Delay(Timeout.Infinite,ct);return 0;}
    }
}
