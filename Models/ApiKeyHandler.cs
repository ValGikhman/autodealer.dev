using autodealer.dev.Services;
using Newtonsoft.Json;
using System;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using HttpAntiForgeryException = System.Web.Mvc.HttpAntiForgeryException;

namespace autodealer.dev.Models {
    public sealed class ApiKeyHandler : DelegatingHandler {
        private readonly IApiAccessService accessService;
        private readonly Func<HttpRequestMessage, string> defaultKeyResolver;
        private readonly Func<HttpRequestMessage, string> clientSessionResolver;

        public ApiKeyHandler() : this(new ApiAccessService()) { }

        public ApiKeyHandler(IApiAccessService accessService) : this(accessService, ResearchDefaultApiKey.Resolve) { }

        public ApiKeyHandler(IApiAccessService accessService, Func<HttpRequestMessage, string> defaultKeyResolver) : this(accessService,defaultKeyResolver,ClientResearchSession.Verify) { }
        public ApiKeyHandler(IApiAccessService accessService, Func<HttpRequestMessage, string> defaultKeyResolver, Func<HttpRequestMessage, string> clientSessionResolver) {
            this.accessService = accessService ?? throw new ArgumentNullException("accessService");
            this.defaultKeyResolver = defaultKeyResolver ?? throw new ArgumentNullException("defaultKeyResolver");
            this.clientSessionResolver=clientSessionResolver ?? throw new ArgumentNullException("clientSessionResolver");
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            if (request.Method == HttpMethod.Options) return await base.SendAsync(request, cancellationToken);
            if (request.RequestUri.AbsolutePath.StartsWith("/api/service/vin/", StringComparison.OrdinalIgnoreCase) &&
                DemoApiAccess.IsDemoRequest(request))
                return await base.SendAsync(request, cancellationToken);

            string bearerToken=null, clientEmail=null;
            var clientWorkspace=request.Headers.Contains(ClientResearchSession.WorkspaceHeader);
            if(clientWorkspace) {
                try { clientEmail=clientSessionResolver(request); }
                catch(HttpAntiForgeryException) { return Error(HttpStatusCode.Forbidden,"invalid_request_verification","The request could not be verified. Refresh the page and try again."); }
                if(clientEmail==null) return Error(HttpStatusCode.Unauthorized,"client_sign_in_required","Sign in to your client account to use this tool.");
            } else if (!DemoApiAccess.TryGetBearerToken(request, out bearerToken)) {
                if (!ResearchDefaultApiKey.HasExplicitCredentials(request)) {
                    try { bearerToken = defaultKeyResolver(request); }
                    catch (HttpAntiForgeryException) {
                        return Error(HttpStatusCode.Forbidden, "invalid_request_verification", "The request could not be verified. Refresh the page and try again.");
                    }
                }
                if (string.IsNullOrWhiteSpace(bearerToken))
                    return Error(HttpStatusCode.Unauthorized, "missing_api_key", "Send your API key as Authorization: Bearer <key> or in the access_token query parameter.");
            }

            ApiAccessResult access;
            try {
                if(clientWorkspace) {
                    var clientAccess=accessService as IClientResearchAccessService;
                    if(clientAccess==null) return Error(HttpStatusCode.ServiceUnavailable,"security_unavailable","Account research is temporarily unavailable.");
                    var accountRequest = AccessRequest(null, null, RequiredScope(request), request);
                    if (request.Headers.TryGetValues(ClientResearchSession.KeySelectionHeader, out var values)) {
                        var selections = values.Take(2).ToArray();
                        if (selections.Length != 1 || !long.TryParse(selections[0], NumberStyles.None, CultureInfo.InvariantCulture, out var selectedId) || selectedId <= 0)
                            return Error(HttpStatusCode.BadRequest, "invalid_key_selection", "Select an API key from your account.");
                        accountRequest.ClientApiKeyId = selectedId;
                    }
                    access = clientAccess.BeginClientResearch(accountRequest, clientEmail);
                } else {
                    var parts=bearerToken.Split(new[] { '.' },2);
                    if(parts.Length!=2 || !parts[0].StartsWith("ad_",StringComparison.Ordinal)) return Error(HttpStatusCode.Unauthorized,"invalid_api_key","The API key is invalid.");
                    access=accessService.BeginRequest(AccessRequest(parts[0],Hash(parts[1]),RequiredScope(request),request));
                }
            }
            catch (Exception ex) when (ex is SqlException || ex is InvalidOperationException) {
                return Error(HttpStatusCode.ServiceUnavailable, "security_unavailable", "API authentication is temporarily unavailable.");
            }
            if (access.ResultCode != "ok") {
                if (access.ResultCode == "client_key_unavailable") return Error(HttpStatusCode.Forbidden, access.ResultCode, "The selected API key is unavailable. Refresh the page and choose an active key.");
                if (access.ResultCode == "client_research_denied") return Error(HttpStatusCode.Forbidden,access.ResultCode,"This account needs an active VIN API key and subscription to use research tools.");
                if (access.ResultCode == "subscription_expired") return Error(HttpStatusCode.Forbidden, access.ResultCode, "Your subscription has expired. Update its period end date or renew it before using the API.");
                if (access.ResultCode == "subscription_inactive") return Error(HttpStatusCode.Forbidden, access.ResultCode, "Your subscription is inactive. Activate it before using the API.");
                if (access.ResultCode == "quota_exceeded") return Error((HttpStatusCode)429, access.ResultCode, "The monthly API quota has been reached.");
                if (access.ResultCode == "scope_denied") return Error(HttpStatusCode.Forbidden, access.ResultCode, "This key does not have access to the endpoint.");
                return Error(HttpStatusCode.Unauthorized, "invalid_api_key", "The API key is invalid or inactive.");
            }

            var identity = new GenericIdentity(access.ClientNumber, "ApiKey");
            var principal = new GenericPrincipal(identity, (access.Scopes ?? string.Empty).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            request.GetRequestContext().Principal = principal;
            if (HttpContext.Current != null) HttpContext.Current.User = principal;

            var timer = Stopwatch.StartNew();
            HttpResponseMessage response;
            try { response = await base.SendAsync(request, cancellationToken); }
            catch {
                Complete(access.RequestId, 500, timer.ElapsedMilliseconds);
                throw;
            }
            Complete(access.RequestId, (int)response.StatusCode, timer.ElapsedMilliseconds);
            response.Headers.TryAddWithoutValidation("X-Request-Id", access.RequestId.ToString());
            response.Headers.TryAddWithoutValidation("X-RateLimit-Limit", access.IsQuotaUnlimited ? "unlimited" : access.MonthlyQuota.ToString());
            response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", access.IsQuotaUnlimited ? "unlimited" : Math.Max(0, access.MonthlyQuota - access.MonthlyUsage).ToString());
            return response;
        }

        private static ApiAccessRequest AccessRequest(string keyId, byte[] secretHash, string requiredScope, HttpRequestMessage request) {
            return new ApiAccessRequest {
                KeyId = keyId,
                SecretHash = secretHash,
                RequiredScope = requiredScope,
                Endpoint = request.RequestUri.AbsolutePath,
                HttpMethod = request.Method.Method,
                IpAddress = ClientIp(),
                UserAgent = string.Join(" ", request.Headers.UserAgent.Select(x => x.ToString())).SubstringSafe(300)
            };
        }

        private void Complete(Guid requestId, int statusCode, long durationMs) {
            if (requestId == Guid.Empty) return;
            try {
                accessService.CompleteRequest(requestId, statusCode, durationMs);
            }
            catch (Exception) { /* The customer response must not fail because telemetry completion failed. */ }
        }

        private static string RequiredScope(HttpRequestMessage request) {
            return "vin:read";
        }

        private static string ClientIp() {
            var context = HttpContext.Current;
            if (context == null) return null;
            var forwarded = context.Request.Headers["X-Forwarded-For"];
            return (string.IsNullOrWhiteSpace(forwarded) ? context.Request.UserHostAddress : forwarded.Split(',')[0].Trim()).SubstringSafe(64);
        }

        private static byte[] Hash(string value) {
            using (var sha = SHA256.Create()) return sha.ComputeHash(Encoding.UTF8.GetBytes(value));
        }

        private static HttpResponseMessage Error(HttpStatusCode status, string code, string message) {
            return new HttpResponseMessage(status) {
                Content = new StringContent(JsonConvert.SerializeObject(new { error = new { code, message } }), Encoding.UTF8, "application/json")
            };
        }

    }

    internal static class ApiStringExtensions {
        public static string SubstringSafe(this string value, int length) {
            if (string.IsNullOrEmpty(value) || value.Length <= length) return value;
            return value.Substring(0, length);
        }
    }
}
