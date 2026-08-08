using autodealer.dev.Data;
using System;
using System.Data;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace autodealer.dev.Services {
    public sealed class ApiKeyIssuanceService : IApiKeyIssuanceService {
        private readonly string connectionString;

        public ApiKeyIssuanceService() {
            connectionString = AutoDealerConnectionString.Resolve();
        }

        public ApiKeyIssuanceResult Issue(long clientId, string name) {
            if (clientId <= 0) throw new ArgumentException("A valid client is required.");
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("The AutoDealer.dev database connection is not configured.");

            var keyName = string.IsNullOrWhiteSpace(name) ? "Additional key" : name.Trim();
            if (keyName.Length > 80) throw new ArgumentException("The API key name cannot exceed 80 characters.");

            var now = DateTime.UtcNow;
            var keyId = "ad_live_" + RandomToken(9);
            var secret = RandomToken(32);
            var fullKey = keyId + "." + secret;
            long apiKeyId;
            string recipientEmail;
            string firstName;
            string clientNumber;
            string planCode;

            using (var context = new AutoDealerDataContext(connectionString)) {
                context.Connection.Open();
                using (var transaction = context.Connection.BeginTransaction(IsolationLevel.Serializable)) {
                    context.Transaction = transaction;
                    try {
                        var client = context.Clients.SingleOrDefault(x => x.ClientId == clientId && x.Status == "active");
                        if (client == null) throw new InvalidOperationException("The client account is not active.");

                        var subscription = context.Subscriptions
                            .Where(x => x.ClientId == clientId &&
                                (x.Status == "trialing" || x.Status == "active") &&
                                x.CurrentPeriodEndUtc > now)
                            .OrderByDescending(x => x.CurrentPeriodEndUtc)
                            .FirstOrDefault();
                        if (subscription == null)
                            throw new InvalidOperationException("An active, unexpired subscription is required before issuing another API key.");

                        var apiKey = new ApiKey {
                            ClientId = clientId,
                            SubscriptionId = subscription.SubscriptionId,
                            KeyId = keyId,
                            SecretHash = Sha256(secret),
                            KeyPrefix = keyId.Substring(0, Math.Min(20, keyId.Length)),
                            Name = keyName,
                            Scopes = "vin:read",
                            Status = "active",
                            CreatedUtc = now
                        };
                        context.ApiKeys.InsertOnSubmit(apiKey);
                        context.SubmitChanges();
                        apiKeyId = apiKey.ApiKeyId;
                        recipientEmail = client.Email;
                        firstName = client.FirstName;
                        clientNumber = client.ClientNumber;
                        planCode = subscription.Plan.PlanCode;
                        transaction.Commit();
                    }
                    catch {
                        transaction.Rollback();
                        throw;
                    }
                }
            }

            var emailSent = SendEmail(clientId, recipientEmail, firstName, clientNumber, planCode, keyName, fullKey);
            return new ApiKeyIssuanceResult {
                ApiKeyId = apiKeyId,
                Name = keyName,
                FullApiKey = fullKey,
                RecipientEmail = recipientEmail,
                EmailSent = emailSent
            };
        }

        private static bool SendEmail(long clientId, string email, string firstName, string clientNumber, string planCode, string keyName, string fullKey) {
            try {
                var body = EmailTemplateRenderer.Render(EmailTemplateName.AdditionalApiKey,
                    new EmailTemplateValues()
                        .Add("FIRST_NAME", firstName)
                        .Add("CLIENT_NUMBER", clientNumber)
                        .Add("PLAN_CODE", planCode)
                        .Add("KEY_NAME", keyName)
                        .Add("API_KEY", fullKey));
                SmtpMailSender.SendForClient(clientId, email, firstName, "A new AutoDealer.dev API key was issued", body, null, null);
                return true;
            }
            catch (Exception ex) {
                Trace.TraceError("Additional API key SMTP delivery failed: {0}", ex);
                return false;
            }
        }

        private static byte[] Sha256(string value) {
            using (var hash = SHA256.Create()) return hash.ComputeHash(Encoding.UTF8.GetBytes(value));
        }

        private static string RandomToken(int byteCount) {
            var bytes = new byte[byteCount];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}
