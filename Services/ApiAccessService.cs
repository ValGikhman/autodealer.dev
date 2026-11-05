using autodealer.dev.Data;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace autodealer.dev.Services {
    public sealed class ApiAccessService : IApiAccessService, IClientResearchAccessService {
        private readonly string connectionString;

        public ApiAccessService() : this(AutoDealerConnectionString.Resolve()) { }

        public ApiAccessService(string connectionString) {
            this.connectionString = connectionString;
        }

        public ApiAccessResult BeginRequest(ApiAccessRequest request) {
            return BeginRequest(request,null);
        }

        public ApiAccessResult BeginClientResearch(ApiAccessRequest request, string verifiedEmail) {
            if(string.IsNullOrWhiteSpace(verifiedEmail)) return Result("client_research_denied");
            return BeginRequest(request,verifiedEmail.Trim().ToLowerInvariant());
        }

        public IList<ClientResearchKeyOption> GetClientResearchKeys(string verifiedEmail) {
            if (string.IsNullOrWhiteSpace(verifiedEmail)) return new List<ClientResearchKeyOption>();
            EnsureConfigured();
            var email = verifiedEmail.Trim().ToLowerInvariant();
            var now = DateTime.UtcNow;
            using (var context = new AutoDealerDataContext(connectionString)) {
                return ClientKeys(context, email, now)
                    .Where(key => key.Subscription.CurrentPeriodEndUtc > now &&
                        (key.Subscription.Status == "active" || key.Subscription.Status == "trialing"))
                    .Select(key => new { key.ApiKeyId, key.Name, key.KeyPrefix, key.Scopes })
                    .AsEnumerable()
                    .Where(key => HasScope(key.Scopes, "vin:read"))
                    .Select(key => new ClientResearchKeyOption { Id = key.ApiKeyId, Name = key.Name, Prefix = key.KeyPrefix })
                    .ToList();
            }
        }

        private static IQueryable<ApiKey> ClientKeys(AutoDealerDataContext context, string email, DateTime now) {
            return context.ApiKeys.Where(key => key.Client.Email == email && key.Client.Status == "active"
                && key.Status == "active" && (!key.ExpiresUtc.HasValue || key.ExpiresUtc.Value > now)
                && key.Subscription.ClientId == key.ClientId)
                .OrderByDescending(key => key.Name == "Primary key")
                .ThenByDescending(key => key.CreatedUtc)
                .ThenByDescending(key => key.ApiKeyId);
        }

        private ApiAccessResult BeginRequest(ApiAccessRequest request, string verifiedEmail) {
            if (request == null) throw new ArgumentNullException("request");
            EnsureConfigured();

            using (var context = new AutoDealerDataContext(connectionString)) {
                context.Connection.Open();
                using (var transaction = context.Connection.BeginTransaction(IsolationLevel.Serializable)) {
                    context.Transaction = transaction;
                    try {
                        var now = DateTime.UtcNow;
                        ApiKey apiKey;
                        if(verifiedEmail==null) apiKey=context.ApiKeys.SingleOrDefault(x=>x.KeyId==request.KeyId);
                        else if (request.ClientApiKeyId.HasValue) {
                            apiKey = ClientKeys(context, verifiedEmail, now)
                                .SingleOrDefault(key => key.ApiKeyId == request.ClientApiKeyId.Value);
                        } else {
                            var keys = ClientKeys(context, verifiedEmail, now).ToList();
                            apiKey=keys.FirstOrDefault(x=>HasScope(x.Scopes,request.RequiredScope) && (x.Subscription.Status=="active" || x.Subscription.Status=="trialing") && x.Subscription.CurrentPeriodEndUtc>now)
                                ?? keys.FirstOrDefault(x=>HasScope(x.Scopes,request.RequiredScope));
                        }
                        if (!IsActiveKey(apiKey,now) || (verifiedEmail==null && !FixedTimeEquals(apiKey.SecretHash.ToArray(),request.SecretHash))) {
                            transaction.Commit();
                            if (verifiedEmail == null) return Result("invalid_api_key");
                            return Result(request.ClientApiKeyId.HasValue ? "client_key_unavailable" : "client_research_denied");
                        }

                        if (apiKey.Subscription.Status != "trialing" && apiKey.Subscription.Status != "active") {
                            transaction.Commit();
                            return Result("subscription_inactive");
                        }
                        if (apiKey.Subscription.CurrentPeriodEndUtc <= now) {
                            transaction.Commit();
                            return Result("subscription_expired");
                        }

                        if (!HasScope(apiKey.Scopes, request.RequiredScope)) {
                            transaction.Commit();
                            return Result("scope_denied", apiKey);
                        }

                        var monthStart = new DateTime(now.Year, now.Month, 1);
                        var monthEnd = monthStart.AddMonths(1);
                        var monthlyUsage = context.ApiUsageDailies
                            .Where(x => x.SubscriptionId == apiKey.SubscriptionId &&
                                        x.UsageDate >= monthStart && x.UsageDate < monthEnd)
                            .Select(x => (int?)x.RequestCount)
                            .Sum() ?? 0;
                        var quota = apiKey.Subscription.Plan.MonthlyRequestQuota;
                        if (!apiKey.IsQuotaUnlimited && monthlyUsage >= quota) {
                            transaction.Commit();
                            return Result("quota_exceeded", apiKey, monthlyUsage);
                        }

                        var requestId = Guid.NewGuid();
                        context.ApiUsageLogs.InsertOnSubmit(new ApiUsageLog {
                            RequestId = requestId,
                            ApiKeyId = apiKey.ApiKeyId,
                            ClientId = apiKey.ClientId,
                            SubscriptionId = apiKey.SubscriptionId,
                            Endpoint = request.Endpoint,
                            HttpMethod = request.HttpMethod,
                            IpAddress = request.IpAddress,
                            UserAgent = request.UserAgent,
                            RequestedUtc = now
                        });

                        var today = now.Date;
                        var daily = context.ApiUsageDailies.SingleOrDefault(x =>
                            x.SubscriptionId == apiKey.SubscriptionId && x.UsageDate == today);
                        if (daily == null) {
                            daily = new ApiUsageDaily {
                                UsageDate = today,
                                ClientId = apiKey.ClientId,
                                SubscriptionId = apiKey.SubscriptionId,
                                RequestCount = 0,
                                ErrorCount = 0,
                                TotalDurationMs = 0
                            };
                            context.ApiUsageDailies.InsertOnSubmit(daily);
                        }

                        daily.RequestCount++;
                        apiKey.LastUsedUtc = now;
                        context.SubmitChanges();
                        transaction.Commit();

                        return new ApiAccessResult {
                            ResultCode = "ok",
                            RequestId = requestId,
                            ClientNumber = apiKey.Client.ClientNumber,
                            Scopes = apiKey.Scopes,
                            MonthlyQuota = quota,
                            IsQuotaUnlimited = apiKey.IsQuotaUnlimited,
                            MonthlyUsage = monthlyUsage + 1
                        };
                    }
                    catch {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        public void CompleteRequest(Guid requestId, int statusCode, long durationMs) {
            if (requestId == Guid.Empty) return;
            EnsureConfigured();

            using (var context = new AutoDealerDataContext(connectionString)) {
                context.Connection.Open();
                using (var transaction = context.Connection.BeginTransaction(IsolationLevel.Serializable)) {
                    context.Transaction = transaction;
                    try {
                        var log = context.ApiUsageLogs.SingleOrDefault(x => x.RequestId == requestId);
                        if (log == null || log.CompletedUtc.HasValue) {
                            transaction.Commit();
                            return;
                        }

                        log.StatusCode = (short)statusCode;
                        log.DurationMs = (int)Math.Min(int.MaxValue, Math.Max(0L, durationMs));
                        log.CompletedUtc = DateTime.UtcNow;

                        var usageDate = log.RequestedUtc.Date;
                        var daily = context.ApiUsageDailies.SingleOrDefault(x =>
                            x.SubscriptionId == log.SubscriptionId && x.UsageDate == usageDate);
                        if (daily != null) {
                            if (statusCode >= 400) daily.ErrorCount++;
                            daily.TotalDurationMs += log.DurationMs.Value;
                        }

                        context.SubmitChanges();
                        transaction.Commit();
                    }
                    catch {
                        transaction.Rollback();
                        throw;
                    }
                }
            }
        }

        private void EnsureConfigured() {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("The AutoDealer.dev database connection is not configured.");
        }

        private static bool IsActiveKey(ApiKey apiKey, DateTime now) {
            return apiKey != null &&
                   apiKey.Status == "active" &&
                   (!apiKey.ExpiresUtc.HasValue || apiKey.ExpiresUtc.Value > now) &&
                   apiKey.Client.Status == "active";
        }

        private static bool HasScope(string scopes, string requiredScope) {
            return (scopes ?? string.Empty)
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Any(x => string.Equals(x, requiredScope, StringComparison.Ordinal));
        }

        private static bool FixedTimeEquals(byte[] left, byte[] right) {
            if (left == null || right == null || left.Length != right.Length) return false;
            var difference = 0;
            for (var index = 0; index < left.Length; index++) difference |= left[index] ^ right[index];
            return difference == 0;
        }

        private static ApiAccessResult Result(string code, ApiKey apiKey = null, int usage = 0) {
            return new ApiAccessResult {
                ResultCode = code,
                ClientNumber = apiKey == null ? null : apiKey.Client.ClientNumber,
                Scopes = apiKey == null ? null : apiKey.Scopes,
                MonthlyQuota = apiKey == null ? 0 : apiKey.Subscription.Plan.MonthlyRequestQuota,
                IsQuotaUnlimited = apiKey != null && apiKey.IsQuotaUnlimited,
                MonthlyUsage = usage
            };
        }
    }
}
