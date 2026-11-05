using autodealer.dev.Models;
using autodealer.dev.Services;
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

class MarketDemandRegression {
 static void Check(bool valid,string text) { if(!valid) throw new Exception(text); Console.WriteLine("PASS "+text); }
 static int Main() { try { Run().GetAwaiter().GetResult();return 0; } catch(Exception ex) { Console.Error.WriteLine(ex.GetBaseException().Message); return 1; } }
 static async Task Run() {
  var request=new PriceResearchRequest { Vin="5NPDH4AE3GH688987",Miles=100000,Zip="45040",Radius=100 };
  var handler=new Handler { Body="{\"mds\":22.5,\"total_active_cars_for_ymmt\":10,\"total_cars_sold_in_last_45_days\":20}" };
  using(var client=new HttpClient(handler)) {
   var result=await new MarketDemandService(client,"private-fixture").GetAsync(request,CancellationToken.None);
   var query=HttpUtility.ParseQueryString(handler.Url.Query);
   Check(result.Status=="ready"&&result.DaysSupply==22.5m&&result.ActiveCount==10&&result.Sales45Days==20,"demand metrics are retained without inventing a selling-time prediction");
   Check(handler.Url.Host=="api.marketcheck.com"&&handler.Url.AbsolutePath=="/v2/mds/car"&&query["vin"]==request.Vin&&query["exact"]=="true"&&query["car_type"]=="used"&&query["zip"]=="45040"&&query["radius"]=="100","MDS sends exact build, used inventory and entered geographic scope");
  }
  var empty=MarketDemandResult.Parse("{\"mds\":null,\"total_active_cars_for_ymmt\":10,\"total_cars_sold_in_last_45_days\":0}");
  Check(empty.Status=="insufficient_data"&&!empty.DaysSupply.HasValue&&empty.Sales45Days==0&&empty.ActiveCount==10,"zero observed sales is insufficient evidence rather than zero days to sell");
  foreach(var code in new[]{HttpStatusCode.Unauthorized,HttpStatusCode.Forbidden,(HttpStatusCode)429,HttpStatusCode.InternalServerError,HttpStatusCode.Redirect}) {
   using(var client=new HttpClient(new Handler { Status=code,Body="private-fixture provider details" })) {
    var result=await new MarketDemandService(client,"private-fixture").GetAsync(request,CancellationToken.None);
    Check(result.Status=="unavailable"&&!result.Message.Contains("private-fixture")&&!result.Message.Contains("provider details"),"demand failure is safe and optional: "+(int)code);
   }
  }
  foreach(var invalid in new[]{"html", "{}", "{\"mds\":-1,\"total_active_cars_for_ymmt\":10,\"total_cars_sold_in_last_45_days\":1}","{\"mds\":null,\"total_active_cars_for_ymmt\":10,\"total_cars_sold_in_last_45_days\":1}"}) {
   using(var client=new HttpClient(new Handler { Body=invalid })) {
    Check((await new MarketDemandService(client,"private-fixture").GetAsync(request,CancellationToken.None)).Status=="unavailable","malformed or incomplete demand data cannot become a buying signal");
   }
  }
  using(var source=new CancellationTokenSource()) using(var client=new HttpClient(new Handler { Delay=true })) {
   var pending=new MarketDemandService(client,"private-fixture").GetAsync(request,source.Token);source.Cancel();
   bool canceled=false;try { await pending; } catch(OperationCanceledException) { canceled=true; }
   Check(canceled,"caller cancellation propagates instead of returning stale demand");
  }
 }
 sealed class Handler:HttpMessageHandler {
  public Uri Url;public string Body; public HttpStatusCode Status=HttpStatusCode.OK;public bool Delay;
  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token) {
   Url=request.RequestUri;if(Delay) await Task.Delay(10000,token);
   return new HttpResponseMessage(Status) { Content=new StringContent(Body??"{}") };
  }
 }
}
