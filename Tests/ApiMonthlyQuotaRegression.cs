using autodealer.dev;
using autodealer.dev.Controllers;
using autodealer.dev.Models;
using autodealer.dev.Services;
using System;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Mvc;

class ApiMonthlyQuotaRegression {
    private static string Connection;
    static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
    static int Main(){try{Run();return 0;}catch(Exception ex){Console.Error.WriteLine(ex.GetBaseException().Message);return 1;}}
    static object Sql(string text,params object[] values){using(var c=new SqlConnection(Connection)){c.Open();using(var command=new SqlCommand(text,c)){for(var i=0;i<values.Length;i++)command.Parameters.AddWithValue("@p"+i,values[i]??DBNull.Value);return command.ExecuteScalar();}}}
    static void Run(){
        Connection=Environment.GetEnvironmentVariable("AUTODEALER_QUOTA_TEST_CONNECTION");
        var builder=new SqlConnectionStringBuilder(Connection);
        if(!builder.InitialCatalog.StartsWith("AutoDealerQuotaTest_",StringComparison.Ordinal)||!builder.IntegratedSecurity)throw new InvalidOperationException("Only the isolated quota-test database is accepted.");
        var client=Convert.ToInt64(Sql("INSERT dbo.Clients(ClientNumber,BusinessName,FirstName,LastName,Email,Status) OUTPUT INSERTED.ClientId VALUES('quota-fixture','Fixture dealer','Quota','Test','quota-fixture@example.invalid','active')"));
        var plan=Convert.ToInt32(Sql("INSERT dbo.Plans(PlanCode,DisplayName,MonthlyRequestQuota,MaxApiKeys) OUTPUT INSERTED.PlanId VALUES('QUOTA_FIXTURE','Quota fixture',1,2)"));
        var subscription=Convert.ToInt64(Sql("INSERT dbo.Subscriptions(ClientId,PlanId,Status,CurrentPeriodStartUtc,CurrentPeriodEndUtc) OUTPUT INSERTED.SubscriptionId VALUES(@p0,@p1,'active',DATEADD(day,-1,SYSUTCDATETIME()),DATEADD(month,1,SYSUTCDATETIME()))",client,plan));
        byte[] hash;using(var sha=SHA256.Create())hash=sha.ComputeHash(Encoding.UTF8.GetBytes("synthetic-fixture-secret"));
        var id=Convert.ToInt64(Sql("INSERT dbo.ApiKeys(ClientId,SubscriptionId,KeyId,SecretHash,KeyPrefix,Name,Scopes,Status) OUTPUT INSERTED.ApiKeyId VALUES(@p0,@p1,'ad_quota_fixture',@p2,'ad_quota_fixture','Quota fixture','vin:read','active')",client,subscription,hash));
        Sql("INSERT dbo.ApiKeys(ClientId,SubscriptionId,KeyId,SecretHash,KeyPrefix,Name,Scopes,Status) VALUES(@p0,@p1,'ad_limited_fixture',@p2,'ad_limited_fixture','Limited sibling','vin:read','active')",client,subscription,hash);
        var service=new ApiAccessService(Connection);
        var request=new ApiAccessRequest{KeyId="ad_quota_fixture",SecretHash=hash,RequiredScope="vin:read",Endpoint="/api/service/price-research",HttpMethod="POST"};
        var first=service.BeginRequest(request);
        Check(first.ResultCode=="ok"&&!first.IsQuotaUnlimited&&first.MonthlyUsage==1,"existing keys retain the plan quota by default");
        service.CompleteRequest(first.RequestId,200,7);
        Check(service.BeginRequest(request).ResultCode=="quota_exceeded","limited key stops at the plan's monthly quota");
        var admin=new AdminService();var edit=admin.GetApiKeyForEdit(id);Check(!edit.IsQuotaUnlimited,"Majordome edit model loads the current limited setting");
        edit.IsQuotaUnlimited=true;var saved=admin.UpdateApiKey(edit);
        Check(saved.IsQuotaUnlimited&&admin.GetApiKeyForEdit(id).IsQuotaUnlimited&&Convert.ToBoolean(Sql("SELECT IsQuotaUnlimited FROM dbo.ApiKeys WHERE ApiKeyId=@p0",id)),"Majordome edit persists unlimited quota through LINQ change tracking");
        var second=service.BeginRequest(request);var third=service.BeginRequest(request);
        Check(second.ResultCode=="ok"&&third.ResultCode=="ok"&&third.IsQuotaUnlimited&&third.MonthlyUsage==3,"unlimited key continues after the monthly plan quota");
        service.CompleteRequest(second.RequestId,200,11);service.CompleteRequest(third.RequestId,503,13);
        Check(Convert.ToInt32(Sql("SELECT SUM(RequestCount) FROM dbo.ApiUsageDaily WHERE SubscriptionId=@p0",subscription))==3&&Convert.ToInt32(Sql("SELECT COUNT(*) FROM dbo.ApiUsageLog WHERE ApiKeyId=@p0",id))==3,"unlimited requests still record monthly usage and request logs");
        Check(Convert.ToInt32(Sql("SELECT SUM(ErrorCount) FROM dbo.ApiUsageDaily WHERE SubscriptionId=@p0",subscription))==1&&Convert.ToInt64(Sql("SELECT SUM(TotalDurationMs) FROM dbo.ApiUsageDaily WHERE SubscriptionId=@p0",subscription))==31,"unlimited requests preserve duration and error metering");
        request.KeyId="ad_limited_fixture";Check(service.BeginRequest(request).ResultCode=="quota_exceeded","unlimited override does not expand a sibling key's quota");request.KeyId="ad_quota_fixture";
        edit=admin.GetApiKeyForEdit(id);edit.IsQuotaUnlimited=false;admin.UpdateApiKey(edit);Check(service.BeginRequest(request).ResultCode=="quota_exceeded","switching back to plan quota restores enforcement immediately");edit.IsQuotaUnlimited=true;admin.UpdateApiKey(edit);
        request.RequiredScope="other:read";Check(service.BeginRequest(request).ResultCode=="scope_denied","unlimited does not bypass endpoint scopes");request.RequiredScope="vin:read";
        Sql("UPDATE dbo.Subscriptions SET CurrentPeriodStartUtc=DATEADD(day,-3,SYSUTCDATETIME()),CurrentPeriodEndUtc=DATEADD(day,-1,SYSUTCDATETIME()) WHERE SubscriptionId=@p0",subscription);
        Check(service.BeginRequest(request).ResultCode=="subscription_expired","unlimited does not bypass an expired subscription");
        Sql("UPDATE dbo.Subscriptions SET CurrentPeriodEndUtc=DATEADD(month,1,SYSUTCDATETIME()),Status='paused' WHERE SubscriptionId=@p0",subscription);
        Check(service.BeginRequest(request).ResultCode=="subscription_inactive","unlimited does not bypass an inactive subscription");Sql("UPDATE dbo.Subscriptions SET Status='active' WHERE SubscriptionId=@p0",subscription);
        Sql("UPDATE dbo.Clients SET Status='suspended' WHERE ClientId=@p0",client);Check(service.BeginRequest(request).ResultCode=="invalid_api_key","unlimited does not bypass suspended customers");Sql("UPDATE dbo.Clients SET Status='active' WHERE ClientId=@p0",client);
        Sql("UPDATE dbo.ApiKeys SET Status='revoked' WHERE ApiKeyId=@p0",id);Check(service.BeginRequest(request).ResultCode=="invalid_api_key","unlimited does not bypass revoked keys");Sql("UPDATE dbo.ApiKeys SET Status='active',ExpiresUtc=DATEADD(day,-1,SYSUTCDATETIME()) WHERE ApiKeyId=@p0",id);
        Check(service.BeginRequest(request).ResultCode=="invalid_api_key","unlimited does not bypass key expiration");Sql("UPDATE dbo.ApiKeys SET ExpiresUtc=NULL WHERE ApiKeyId=@p0",id);
        request.SecretHash=new byte[32];Check(service.BeginRequest(request).ResultCode=="invalid_api_key","unlimited does not bypass credential verification");
        Check(Convert.ToInt32(Sql("SELECT SUM(RequestCount) FROM dbo.ApiUsageDaily WHERE SubscriptionId=@p0",subscription))==3,"rejected requests do not inflate accepted usage");
        var action=typeof(AdminController).GetMethod("EditApiKey",new[]{typeof(AdminApiKeyEditViewModel)});
        Check(action.IsDefined(typeof(AdminAuthorizeAttribute),true)&&action.IsDefined(typeof(ValidateAntiForgeryTokenAttribute),true)&&action.IsDefined(typeof(System.Web.Mvc.HttpPostAttribute),true),"quota changes remain restricted to the Majordome admin POST with anti-forgery validation");
        Headers().GetAwaiter().GetResult();
    }
    static async Task Headers(){
        var access=new HeaderAccess{Unlimited=true};
        using(var handler=new ApiKeyHandler(access){InnerHandler=new SuccessHandler()})using(var client=new HttpClient(handler)){
            Func<HttpRequestMessage> request=()=>{var message=new HttpRequestMessage(HttpMethod.Get,"https://localhost/api/service/price-research/test/json");message.Headers.Authorization=new AuthenticationHeaderValue("Bearer","ad_headerfixture.synthetic");message.SetRequestContext(new HttpRequestContext());return message;};
            using(var response=await client.SendAsync(request()))Check(response.Headers.GetValues("X-RateLimit-Limit").Single()=="unlimited"&&response.Headers.GetValues("X-RateLimit-Remaining").Single()=="unlimited","unlimited key reports unlimited HTTP quota headers");
            access.Unlimited=false;using(var response=await client.SendAsync(request()))Check(response.Headers.GetValues("X-RateLimit-Limit").Single()=="1"&&response.Headers.GetValues("X-RateLimit-Remaining").Single()=="0","limited key retains numerical HTTP quota headers");
        }
    }
    sealed class HeaderAccess:IApiAccessService {
        public bool Unlimited;
        public ApiAccessResult BeginRequest(ApiAccessRequest request){return new ApiAccessResult{ResultCode="ok",ClientNumber="quota-fixture",Scopes="vin:read",MonthlyQuota=1,MonthlyUsage=3,IsQuotaUnlimited=Unlimited};}
        public void CompleteRequest(Guid id,int code,long duration){}
    }
    sealed class SuccessHandler:HttpMessageHandler {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct){return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));}}
}
