using autodealer.dev.Services;
using Newtonsoft.Json;
using System;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace autodealer.dev.Models {
    public sealed class ApiKeyHandler : DelegatingHandler {
        private readonly IApiAccessService accessService;

        public ApiKeyHandler() : this(new ApiAccessService()) { }

        public ApiKeyHandler(IApiAccessService accessService) {
            this.accessService = accessService ?? throw new ArgumentNullException("accessService");
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
            if (request.Method == HttpMethod.Options) return await base.SendAsync(request, cancellationToken);
            if (request.RequestUri.AbsolutePath.StartsWith("/api/service/vin/", StringComparison.OrdinalIgnoreCase) &&
                DemoApiAccess.IsDemoRequest(request))
                return await base.SendAsync(request, cancellationToken);

            var auth = request.Headers.Authorization;
            if (auth == null || !string.Equals(auth.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
                return Error(HttpStatusCode.Unauthorized, "missing_api_key", "Send your API key as Authorization: Bearer <key>.");

            var parts = (auth.Parameter ?? string.Empty).Split(new[] { '.' }, 2);
            if (parts.Length != 2 || !parts[0].StartsWith("ad_", StringComparison.Ordinal))
                return Error(HttpStatusCode.Unauthorized, "invalid_api_key", "The API key is invalid.");

            ApiAccessResult access;
            try { access = Authenticate(parts[0], Hash(parts[1]), RequiredScope(request), request); }
            catch (Exception ex) when (ex is SqlException || ex is InvalidOperationException) {
                return Error(HttpStatusCode.ServiceUnavailable, "security_unavailable", "API authentication is temporarily unavailable.");
            }
            if (access.ResultCode != "ok") {
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
            response.Headers.TryAddWithoutValidation("X-RateLimit-Limit", access.MonthlyQuota.ToString());
            response.Headers.TryAddWithoutValidation("X-RateLimit-Remaining", Math.Max(0, access.MonthlyQuota - access.MonthlyUsage).ToString());
            return response;
        }

        private ApiAccessResult Authenticate(string keyId, byte[] secretHash, string requiredScope, HttpRequestMessage request) {
            return accessService.BeginRequest(new ApiAccessRequest {
                KeyId = keyId,
                SecretHash = secretHash,
                RequiredScope = requiredScope,
                Endpoint = request.RequestUri.AbsolutePath,
                HttpMethod = request.Method.Method,
                IpAddress = ClientIp(),
                UserAgent = string.Join(" ", request.Headers.UserAgent.Select(x => x.ToString())).SubstringSafe(300)
            });
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
