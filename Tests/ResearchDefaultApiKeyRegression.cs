using autodealer.dev.Models;
using autodealer.dev.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Web.Helpers;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Security;
using HttpAntiForgeryException = System.Web.Mvc.HttpAntiForgeryException;

class ResearchDefaultApiKeyRegression {
    private const string FixtureKey="ad_savedfixture.default-fixture-secret";
    private const string FixtureCookie="fixture-cookie-token";
    private const string FixtureToken="fixture-form-token";
    private static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);}
    static int Main(){try{Run().GetAwaiter().GetResult();return 0;}catch(Exception ex){Console.Error.WriteLine(ex.GetBaseException().Message);return 1;}}

    private static HttpRequestMessage Request(string path="/api/service/price-research",HttpMethod method=null,bool verification=true){
        var request=new HttpRequestMessage(method??HttpMethod.Post,"http://localhost"+path);
        if(verification)request.Headers.Add(ResearchDefaultApiKey.VerificationHeaderName,FixtureToken);
        request.SetRequestContext(new HttpRequestContext());
        return request;
    }
    private static IPrincipal Admin(bool expired=false,string userData="Admin",string role="Admin"){
        var ticket=new FormsAuthenticationTicket(1,"fixture-admin",DateTime.Now.AddHours(-1),expired?DateTime.Now.AddMinutes(-1):DateTime.Now.AddHours(1),false,userData,"/");
        return new GenericPrincipal(new FormsIdentity(ticket),new[]{role});
    }
    private static async Task Run(){
        AntiForgeryConfig.CookieName="__ResearchDefaultKeyFixture";
        Check(typeof(ResearchDefaultApiKey).GetProperty("IsConfigured").PropertyType==typeof(bool),"default availability exposes a boolean property without returning a credential");
        foreach(var path in new[]{"/api/service/price-research","/api/service/neovin"}){
            var fixture=new Fixture();var access=new Access(fixture.Events);
            var observation=await Send(Request(path),fixture,access);
            Check(observation.Status==HttpStatusCode.OK&&fixture.Verifications==1&&access.Begins==1&&observation.InnerCalls==1&&access.Completes==1,"verified forms Admin uses normal account-key authentication for "+path);
            byte[] expected;using(var sha=SHA256.Create())expected=sha.ComputeHash(Encoding.UTF8.GetBytes("default-fixture-secret"));
            Check(access.Last.KeyId=="ad_savedfixture"&&access.Last.SecretHash.SequenceEqual(expected)&&access.Last.RequiredScope=="vin:read"&&access.Last.Endpoint==path&&access.Last.HttpMethod=="POST","saved key is parsed, hashed and scoped through the existing access service");
            Check(fixture.Events.SequenceEqual(new[]{"validate","begin","endpoint","complete"})&&observation.ApiPrincipal&&observation.Headers["X-RateLimit-Limit"]=="10"&&observation.Headers["X-RateLimit-Remaining"]=="7","verification precedes metering and normal quota headers/principal/completion remain intact");
            Check(!observation.Body.Contains(FixtureKey)&&!observation.Body.Contains(FixtureCookie)&&!observation.Body.Contains(FixtureToken),"saved account key and verification tokens are absent from the successful response");
        }

        foreach(var path in new[]{"/api/service/price-research/","/api/service/price-research/other","/api/service/price-research-extra","/api/service/neovin/4T1BE46K19U342407/json","/api/service/vin/4T1BE46K19U342407/json","/majordome/priceResearch/bid"}){
            var fixture=new Fixture();var access=new Access(fixture.Events);var observation=await Send(Request(path),fixture,access);
            Check(observation.Status==HttpStatusCode.Unauthorized&&fixture.Reads==0&&fixture.Verifications==0&&access.Begins==0&&observation.InnerCalls==0,"saved key is unavailable outside the two exact POST endpoints: "+path);
        }
        foreach(var method in new[]{HttpMethod.Get,HttpMethod.Put,HttpMethod.Delete}){
            var fixture=new Fixture();var access=new Access(fixture.Events);var observation=await Send(Request(method:method),fixture,access);
            Check(observation.Status==HttpStatusCode.Unauthorized&&fixture.Verifications==0&&access.Begins==0,"saved key is unavailable for "+method.Method);
        }
        var optionsFixture=new Fixture();var optionsAccess=new Access(optionsFixture.Events);var options=await Send(Request(method:HttpMethod.Options),optionsFixture,optionsAccess);
        Check(options.Status==HttpStatusCode.OK&&optionsFixture.Reads==0&&optionsAccess.Begins==0,"OPTIONS keeps its existing unauthenticated preflight behavior");

        var deniedUsers=new[]{
            (IPrincipal)null,
            new GenericPrincipal(new GenericIdentity(""),new string[0]),
            new GenericPrincipal(new GenericIdentity("api-client","ApiKey"),new[]{"Admin"}),
            Admin(role:"Customer"),
            Admin(userData:"Customer"),
            Admin(expired:true)
        };
        foreach(var user in deniedUsers){
            var fixture=new Fixture{Context=new Context(user)};var access=new Access(fixture.Events);var observation=await Send(Request(),fixture,access);
            Check(observation.Status==HttpStatusCode.Unauthorized&&fixture.Reads==0&&fixture.Verifications==0&&access.Begins==0,"anonymous, API-key, nonadmin and expired identities cannot use the saved key");
        }
        var absentContext=new Fixture{Context=null};var absentAccess=new Access(absentContext.Events);
        var orphanRequest=Request();orphanRequest.GetRequestContext().Principal=Admin();
        var absent=await Send(orphanRequest,absentContext,absentAccess);
        Check(absent.Status==HttpStatusCode.Unauthorized&&absentContext.Reads==0&&absentAccess.Begins==0,"a request principal alone cannot replace the authenticated ASP.NET Admin context");
        foreach(var token in new[]{(string)null," "}){
            var fixture=new Fixture();var access=new Access(fixture.Events);var request=Request(verification:false);
            if(token!=null)request.Headers.TryAddWithoutValidation(ResearchDefaultApiKey.VerificationHeaderName,token);
            var observation=await Send(request,fixture,access);
            Check(observation.Status==HttpStatusCode.Unauthorized&&fixture.Reads==0&&fixture.Verifications==0&&access.Begins==0,"missing or blank verification retains the normal missing-key response");
        }

        foreach(var badCookie in new[]{false,true}){
            var fixture=new Fixture();if(badCookie)fixture.Context.Request.Cookies.Clear();
            var access=new Access(fixture.Events);var request=Request();
            if(!badCookie){request.Headers.Remove(ResearchDefaultApiKey.VerificationHeaderName);request.Headers.Add(ResearchDefaultApiKey.VerificationHeaderName,"wrong-fixture-token");}
            var observation=await Send(request,fixture,access);
            Check(observation.Status==HttpStatusCode.Forbidden&&fixture.Verifications==1&&access.Begins==0&&observation.InnerCalls==0&&!observation.Body.Contains("wrong-fixture-token")&&!observation.Body.Contains(FixtureKey)&&observation.Body.Contains("invalid_request_verification"),"invalid anti-forgery cookie/header is rejected safely before any quota or provider work");
        }
        var duplicateFixture=new Fixture();var duplicateAccess=new Access(duplicateFixture.Events);var duplicateRequest=Request(verification:false);
        duplicateRequest.Headers.TryAddWithoutValidation(ResearchDefaultApiKey.VerificationHeaderName,new[]{FixtureToken,FixtureToken});
        var duplicate=await Send(duplicateRequest,duplicateFixture,duplicateAccess);
        Check(duplicate.Status==HttpStatusCode.Forbidden&&duplicateAccess.Begins==0,"ambiguous duplicate verification headers cannot authenticate");

        foreach(var path in new[]{"/api/service/price-research?access_token=","/api/service/price-research?access_token","/api/service/price-research?%61ccess_token=","/api/service/price-research?ACCESS_TOKEN="}){
            var fixture=new Fixture();var access=new Access(fixture.Events);var observation=await Send(Request(path),fixture,access);
            Check(observation.Status==HttpStatusCode.Unauthorized&&fixture.ResolverCalls==0&&access.Begins==0,"an explicitly empty query credential never falls back to the saved key");
        }
        foreach(var authorization in new[]{new AuthenticationHeaderValue("Basic","fixture"),new AuthenticationHeaderValue("Bearer"),new AuthenticationHeaderValue("Bearer","not-an-account-key")}){
            var fixture=new Fixture();var access=new Access(fixture.Events);var request=Request();request.Headers.Authorization=authorization;
            var observation=await Send(request,fixture,access);
            Check(observation.Status==HttpStatusCode.Unauthorized&&fixture.ResolverCalls==0&&fixture.Verifications==0&&access.Begins==0,"invalid explicit Authorization keeps its own failure without saved-key fallback");
        }
        foreach(var query in new[]{false,true}){
            var fixture=new Fixture();var access=new Access(fixture.Events);
            var request=Request(query?"/api/service/neovin/4T1BE46K19U342407/json?access_token=ad_manualfixture.manual-secret":"/api/service/neovin/4T1BE46K19U342407/json",HttpMethod.Get,false);
            if(!query)request.Headers.Authorization=new AuthenticationHeaderValue("Bearer","ad_manualfixture.manual-secret");
            var observation=await Send(request,fixture,access);
            Check(observation.Status==HttpStatusCode.OK&&fixture.ResolverCalls==0&&access.Begins==1&&access.Last.KeyId=="ad_manualfixture","normal explicit bearer/query credentials work without Admin cookies or verification");
        }

        var unconfigured=new Fixture{ConfiguredKey=" "};var unconfiguredAccess=new Access(unconfigured.Events);
        var unconfiguredResponse=await Send(Request(),unconfigured,unconfiguredAccess);
        Check(unconfiguredResponse.Status==HttpStatusCode.Unauthorized&&unconfigured.Verifications==0&&unconfiguredAccess.Begins==0,"a missing server default never invents credentials or consumes quota");
        var malformed=new Fixture{ConfiguredKey="malformed-fixture-default"};var malformedAccess=new Access(malformed.Events);var malformedResponse=await Send(Request(),malformed,malformedAccess);
        Check(malformedResponse.Status==HttpStatusCode.Unauthorized&&malformedAccess.Begins==0&&!malformedResponse.Body.Contains("malformed-fixture-default"),"a malformed configured default is rejected by normal key parsing without disclosure");

        foreach(var code in new[]{"quota_exceeded","scope_denied","subscription_expired","subscription_inactive","invalid_api_key"}){
            var fixture=new Fixture();var access=new Access(fixture.Events){Code=code};var observation=await Send(Request(),fixture,access);
            var expected=code=="quota_exceeded"?(HttpStatusCode)429:code=="invalid_api_key"?HttpStatusCode.Unauthorized:HttpStatusCode.Forbidden;
            Check(observation.Status==expected&&fixture.Verifications==1&&access.Begins==1&&observation.InnerCalls==0&&access.Completes==0&&!observation.Body.Contains(FixtureKey),"saved-key mode preserves normal access denial: "+code);
        }
        var unlimitedFixture=new Fixture();var unlimitedAccess=new Access(unlimitedFixture.Events){Unlimited=true};var unlimited=await Send(Request(),unlimitedFixture,unlimitedAccess);
        Check(unlimited.Status==HttpStatusCode.OK&&unlimited.Headers["X-RateLimit-Limit"]=="unlimited"&&unlimited.Headers["X-RateLimit-Remaining"]=="unlimited"&&unlimitedAccess.Begins==1&&unlimitedAccess.Completes==1,"the existing per-key unlimited quota behavior remains unchanged");
    }

    private static async Task<Observation> Send(HttpRequestMessage request,Fixture fixture,Access access){
        var inner=new Endpoint(fixture.Events);
        using(request)using(var handler=new ApiKeyHandler(access,fixture.Resolve){InnerHandler=inner})using(var invoker=new HttpMessageInvoker(handler))using(var response=await invoker.SendAsync(request,CancellationToken.None)){
            return new Observation{Status=response.StatusCode,Body=await response.Content.ReadAsStringAsync(),Headers=response.Headers.ToDictionary(x=>x.Key,x=>string.Join(",",x.Value),StringComparer.OrdinalIgnoreCase),InnerCalls=inner.Calls,ApiPrincipal=inner.ApiPrincipal};
        }
    }
    private sealed class Observation {
        public HttpStatusCode Status;public string Body;public Dictionary<string,string> Headers;public int InnerCalls;public bool ApiPrincipal;
    }
    private sealed class Fixture {
        public Context Context=new Context(Admin());public string ConfiguredKey=FixtureKey;
        public int Reads,Verifications,ResolverCalls;public readonly List<string> Events=new List<string>();
        public string Resolve(HttpRequestMessage request){
            ResolverCalls++;
            return ResearchDefaultApiKey.Resolve(request,Context,()=>{Reads++;return ConfiguredKey;},(cookie,token)=>{
                Verifications++;Events.Add("validate");
                if(cookie!=FixtureCookie||token!=FixtureToken)throw new HttpAntiForgeryException("Synthetic verification failed: "+cookie+" / "+token);
            });
        }
    }
    private sealed class Context:HttpContextBase {
        private readonly RequestContext request=new RequestContext();private IPrincipal principal;
        public Context(IPrincipal user){principal=user;request.Cookies.Add(new HttpCookie(AntiForgeryConfig.CookieName,FixtureCookie));}
        public override IPrincipal User{get{return principal;}set{principal=value;}}
        public override HttpRequestBase Request{get{return request;}}
    }
    private sealed class RequestContext:HttpRequestBase {
        private readonly HttpCookieCollection cookies=new HttpCookieCollection();
        public override HttpCookieCollection Cookies{get{return cookies;}}
    }
    private sealed class Access:IApiAccessService {
        private readonly List<string> events;public int Begins,Completes;public ApiAccessRequest Last;public string Code="ok";public bool Unlimited;
        public Access(List<string> events){this.events=events;}
        public ApiAccessResult BeginRequest(ApiAccessRequest request){Begins++;events.Add("begin");Last=request;return new ApiAccessResult{ResultCode=Code,RequestId=Guid.NewGuid(),ClientNumber="fixture-client",Scopes="vin:read",MonthlyQuota=10,MonthlyUsage=3,IsQuotaUnlimited=Unlimited};}
        public void CompleteRequest(Guid requestId,int status,long duration){Completes++;events.Add("complete");}
    }
    private sealed class Endpoint:HttpMessageHandler {
        private readonly List<string> events;public int Calls;public bool ApiPrincipal;
        public Endpoint(List<string> events){this.events=events;}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken){
            Calls++;events.Add("endpoint");var principal=request.GetRequestContext().Principal;
            ApiPrincipal=principal!=null&&principal.IsInRole("vin:read")&&!principal.IsInRole("Admin");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"ok\":true}")});
        }
    }
}
