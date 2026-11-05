using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Helpers;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Mvc;
using System.Web.Routing;
using System.Web.Security;
using autodealer.dev;
using autodealer.dev.Controllers;
using autodealer.dev.Models;
using autodealer.dev.Services;

class ClientResearchRegression {
 static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);}
 static int Main(){try{Run().GetAwaiter().GetResult();return 0;}catch(Exception ex){Console.Error.WriteLine(ex.GetBaseException().Message);return 1;}}
 static IPrincipal User(string data="",bool expired=false){return new GenericPrincipal(new FormsIdentity(new FormsAuthenticationTicket(1,"Client@Example.test",DateTime.Now.AddHours(-1),DateTime.Now.AddMinutes(expired?-1:60),false,data,"/")),new string[0]);}
 static HttpRequestMessage Request(string path,HttpMethod method=null,bool token=true){var r=new HttpRequestMessage(method??HttpMethod.Post,"http://localhost"+path);r.Headers.Add(ClientResearchSession.WorkspaceHeader,"account");if(token)r.Headers.Add(ResearchDefaultApiKey.VerificationHeaderName,"fixture-token");r.SetRequestContext(new HttpRequestContext());return r;}
 static async Task Run(){
  AntiForgeryConfig.CookieName="__ClientResearchFixture";
  var ctx=new FakeContext(User());var verified=0;
  Func<HttpRequestMessage,string> resolver=r=>ClientResearchSession.Verify(r,ctx,(cookie,token)=>{if(cookie!="fixture-cookie"||token!="fixture-token")throw new HttpAntiForgeryException("Synthetic failure");verified++;});
  var access=new Access();var endpoint=new Endpoint();var defaultCalls=0;
  using(var handler=new ApiKeyHandler(access,r=>{defaultCalls++;return null;},resolver){InnerHandler=endpoint})using(var invoker=new HttpMessageInvoker(handler)){
   foreach(var path in new[]{"/api/service/price-research","/api/service/neovin"}){
    var request = Request(path);
    request.Headers.Add(ClientResearchSession.KeySelectionHeader, "9007199254740993");
    var result=await invoker.SendAsync(request,CancellationToken.None);
    Check(result.StatusCode==HttpStatusCode.OK&&access.Email=="client@example.test"&&access.Last.RequiredScope=="vin:read"&&access.Last.SecretHash==null&&access.Last.ClientApiKeyId==9007199254740993L&&endpoint.Client=="fixture-client","signed-in client is metered using the selected key on "+path);
   }
   access.Code="quota_exceeded";
   var limited=await invoker.SendAsync(Request("/api/service/price-research"),CancellationToken.None);
   Check((int)limited.StatusCode==429&&endpoint.Calls==2,"client account quota stops provider work");
   access.Code="subscription_expired";
   Check((await invoker.SendAsync(Request("/api/service/neovin"),CancellationToken.None)).StatusCode==HttpStatusCode.Forbidden,"expired client subscription is rejected");
   var before=access.Calls;
   var invalidSelection = Request("/api/service/price-research");
   invalidSelection.Headers.Add(ClientResearchSession.KeySelectionHeader, "invalid");
   Check((await invoker.SendAsync(invalidSelection,CancellationToken.None)).StatusCode==HttpStatusCode.BadRequest,"invalid key selection is rejected before metering");
   foreach(var rejected in new[]{Request("/api/service/vin/example/json"),Request("/api/service/price-research",HttpMethod.Get),Request("/api/service/neovin",token:false),Request("/api/service/price-research?access_token=")})
    Check((await invoker.SendAsync(rejected,CancellationToken.None)).StatusCode==HttpStatusCode.Unauthorized,"client mode cannot authorize unsupported routes, methods or mixed credentials");
   var mixed=Request("/api/service/neovin");mixed.Headers.Authorization=new AuthenticationHeaderValue("Bearer","ad_fixture.secret");
   Check((await invoker.SendAsync(mixed,CancellationToken.None)).StatusCode==HttpStatusCode.Unauthorized,"an explicit key cannot be mixed with client session mode");
   var forged=Request("/api/service/neovin",token:false);forged.Headers.Add(ResearchDefaultApiKey.VerificationHeaderName,"forged");
   Check((await invoker.SendAsync(forged,CancellationToken.None)).StatusCode==HttpStatusCode.Forbidden,"forged form token is rejected before metering");
   foreach(var user in new[]{null,User("Admin"),User(expired:true),new GenericPrincipal(new GenericIdentity("fixture-client","ApiKey"),new[]{"vin:read"})}){
    ctx.User=user;Check((await invoker.SendAsync(Request("/api/service/neovin"),CancellationToken.None)).StatusCode==HttpStatusCode.Unauthorized,"only a current customer forms session can authorize client research");
   }
   Check(access.Calls==before&&defaultCalls==0&&verified==5,"rejected clients cannot consume quota or fall back to the Majordome key");
  }
  var routes=new RouteCollection();RouteConfig.RegisterRoutes(routes);
  foreach(var path in new[]{"account/priceResearch","account/vinDecoder","account/priceResearch/bid"}){
   ctx.Path="~/"+path;var route=routes.GetRouteData(ctx);Check(route!=null&&(string)route.Values["controller"]=="ClientResearch","client route resolves before account catch-all: "+path);
  }
  Check(typeof(ClientResearchController).IsDefined(typeof(System.Web.Mvc.AuthorizeAttribute),true)&&typeof(ClientResearchController).GetMethod("Bid").IsDefined(typeof(ValidateAntiForgeryTokenAttribute),true),"client research requires sign-in and bidding requires anti-forgery validation");
 }
 sealed class FakeContext:HttpContextBase{IPrincipal user;public string Path="~/";public FakeContext(IPrincipal user){this.user=user;}public override IPrincipal User{get{return user;}set{user=value;}}public override HttpRequestBase Request{get{return new FakeRequest(Path);}}}
 sealed class FakeRequest:HttpRequestBase{readonly string path;public FakeRequest(string path){this.path=path;}public override string AppRelativeCurrentExecutionFilePath{get{return path;}}public override string PathInfo{get{return "";}}public override HttpCookieCollection Cookies{get{var c=new HttpCookieCollection();c.Add(new HttpCookie(AntiForgeryConfig.CookieName,"fixture-cookie"));return c;}}}
 sealed class Access:IApiAccessService,IClientResearchAccessService{public string Email,Code="ok";public ApiAccessRequest Last;public int Calls;public ApiAccessResult BeginRequest(ApiAccessRequest request){throw new Exception("Wrong access path");}public ApiAccessResult BeginClientResearch(ApiAccessRequest request,string email){Calls++;Last=request;Email=email;return new ApiAccessResult{ResultCode=Code,ClientNumber="fixture-client",Scopes="vin:read",MonthlyQuota=10,MonthlyUsage=3};}public void CompleteRequest(Guid id,int code,long duration){}}
 sealed class Endpoint:HttpMessageHandler{public int Calls;public string Client;protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Calls++;Client=request.GetRequestContext().Principal.Identity.Name;return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));}}
}
