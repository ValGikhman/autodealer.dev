using autodealer.dev.Data;
using System;
using System.Configuration;
using System.Data;
using System.Linq;

namespace autodealer.dev.Services {
    public sealed class ApiAccessService : IApiAccessService {
        private readonly string connectionString;

        public ApiAccessService() : this(ConfigurationManager.ConnectionStrings["AutoDealer.dev"] == null
            ? null
            : ConfigurationManager.ConnectionStrings["AutoDealer.dev"].ConnectionString) { }

        public ApiAccessService(string connectionString) {
            this.connectionString = connectionString;
        }

        public ApiAccessResult BeginRequest(ApiAccessRequest request) {
            if (request == null) throw new ArgumentNullException("request");
            EnsureConfigured();

            using (var context = new AutoDealerDataContext(connectionString)) {
                context.Connection.Open();
                using (var transaction = context.Connection.BeginTransaction(IsolationLevel.Serializable)) {
                    context.Transaction = transaction;
                    try {
                        var now = DateTime.UtcNow;
                        var apiKey = context.ApiKeys.SingleOrDefault(x => x.KeyId == request.KeyId);
                        if (!IsActive(apiKey, request.SecretHash, now)) {
                            transaction.Commit();
                            return Result("invalid_api_key");
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
                        if (monthlyUsage >= quota) {
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

        private static bool IsActive(ApiKey apiKey, byte[] secretHash, DateTime now) {
            return apiKey != null &&
                   FixedTimeEquals(apiKey.SecretHash.ToArray(), secretHash) &&
                   apiKey.Status == "active" &&
                   (!apiKey.ExpiresUtc.HasValue || apiKey.ExpiresUtc.Value > now) &&
                   apiKey.Client.Status == "active" &&
                   (apiKey.Subscription.Status == "trialing" || apiKey.Subscription.Status == "active") &&
                   apiKey.Subscription.CurrentPeriodEndUtc > now;
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
                MonthlyUsage = usage
            };
        }
    }
}
