using autodealer.dev.Data;
using autodealer.dev.Models;
using System;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace autodealer.dev.Services {
    public sealed class ClientAccountService : IClientAccountService {
        private const int PasswordIterations = 210000;
        private readonly string connectionString;
        private readonly ICredentialEmailService emailService;

        public ClientAccountService(ICredentialEmailService emailService) {
            connectionString = AutoDealerConnectionString.Resolve();
            this.emailService = emailService;
        }

        public AccountCreatedViewModel Create(AccountRegistrationViewModel model) {
            return Create(model, null, false);
        }

        public AccountCreatedViewModel Create(AccountRegistrationViewModel model, string requestedClientNumber, bool emailTemporaryPassword) {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("The AutoDealer.dev database connection is not configured.");

            var planCode = NormalizePlan(model.PlanCode);
            if (!PasswordPolicy.IsValid(model.Password))
                throw new ArgumentException("The password does not meet the required security policy.");
            var normalizedEmail = model.Email.Trim().ToLowerInvariant();
            var now = DateTime.UtcNow;
            var clientNumber = ResolveClientNumber(requestedClientNumber);
            var keyId = "ad_live_" + RandomToken(9);
            var secret = RandomToken(32);
            var fullKey = keyId + "." + secret;
            var passwordSalt = RandomBytes(32);
            long clientId = 0;
            byte[] passwordHash;
            using (var derive = new Rfc2898DeriveBytes(model.Password, passwordSalt, PasswordIterations, HashAlgorithmName.SHA256))
                passwordHash = derive.GetBytes(32);

            using (var context = new AutoDealerDataContext(connectionString)) {
                context.Connection.Open();
                using (var transaction = context.Connection.BeginTransaction(IsolationLevel.Serializable)) {
                    context.Transaction = transaction;
                    try {
                        if (context.Clients.Any(x => x.Email == normalizedEmail))
                            throw new InvalidOperationException("An account already exists for this email address.");
                        if (context.Clients.Any(x => x.ClientNumber == clientNumber))
                            throw new InvalidOperationException("That generated client number is no longer available. Reopen the form to generate another one.");

                        var plan = context.Plans.SingleOrDefault(x => x.PlanCode == planCode && x.IsActive);
                        if (plan == null) throw new InvalidOperationException("The selected plan is not available.");

                        var client = new Client {
                            ClientNumber = clientNumber,
                            BusinessName = model.BusinessName.Trim(),
                            FirstName = model.FirstName.Trim(),
                            LastName = model.LastName.Trim(),
                            Email = normalizedEmail,
                            Phone = string.IsNullOrWhiteSpace(model.Phone) ? null : model.Phone.Trim(),
                            Status = "active",
                            CreatedUtc = now,
                            UpdatedUtc = now
                        };
                        var credential = new ClientCredential {
                            Client = client,
                            PasswordHash = passwordHash,
                            PasswordSalt = passwordSalt,
                            PasswordIterations = PasswordIterations,
                            PasswordAlgorithm = "PBKDF2-SHA256",
                            PasswordChangedUtc = now,
                            FailedLoginCount = 0
                        };

                        var subscription = new Subscription {
                            Client = client,
                            Plan = plan,
                            Status = "trialing",
                            CurrentPeriodStartUtc = now,
                            CurrentPeriodEndUtc = now.AddDays(14),
                            CancelAtPeriodEnd = false,
                            CreatedUtc = now,
                            UpdatedUtc = now
                        };

                        var apiKey = new ApiKey {
                            Client = client,
                            Subscription = subscription,
                            KeyId = keyId,
                            SecretHash = Sha256(secret),
                            KeyPrefix = keyId.Substring(0, Math.Min(20, keyId.Length)),
                            Name = "Primary key",
                            Scopes = "vin:read",
                            Status = "active",
                            CreatedUtc = now
                        };

                        PaymentProfile paymentProfile = null;
                        if (!string.IsNullOrWhiteSpace(model.PaymentMethodToken)) {
                            paymentProfile = new PaymentProfile {
                                Client = client,
                                Provider = "configured-provider",
                                ProviderPaymentMethodId = model.PaymentMethodToken.Trim(),
                                IsDefault = true,
                                CreatedUtc = now
                            };
                        }

                        context.Clients.InsertOnSubmit(client);
                        context.ClientCredentials.InsertOnSubmit(credential);
                        context.Subscriptions.InsertOnSubmit(subscription);
                        context.ApiKeys.InsertOnSubmit(apiKey);
                        if (paymentProfile != null) context.PaymentProfiles.InsertOnSubmit(paymentProfile);
                        context.SubmitChanges();
                        clientId = client.ClientId;
                        transaction.Commit();
                    }
                    catch {
                        transaction.Rollback();
                        throw;
                    }
                }
            }

            var emailed = emailService != null && emailService.Send(clientId, model.BusinessName, model.FirstName, model.LastName, model.Email.Trim(), model.Phone, clientNumber, fullKey, planCode, emailTemporaryPassword ? model.Password : null);
            return new AccountCreatedViewModel { ClientNumber = clientNumber, ApiKey = fullKey, Email = model.Email, CredentialsEmailed = emailed };
        }

        public bool IsEmailAvailable(string email, long? excludedClientId) {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("The AutoDealer.dev database connection is not configured.");
            var normalizedEmail = (email ?? string.Empty).Trim().ToLowerInvariant();
            if (normalizedEmail.Length == 0 || normalizedEmail.Length > 254) return false;
            using (var context = new AutoDealerDataContext(connectionString)) {
                var clientsWithEmail = context.Clients.Where(x => x.Email == normalizedEmail);
                if (excludedClientId.HasValue) {
                    var clientId = excludedClientId.Value;
                    clientsWithEmail = clientsWithEmail.Where(x => x.ClientId != clientId);
                }
                return !clientsWithEmail.Any();
            }
        }

        public AccountDashboardViewModel Authenticate(string email, string password) {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("The AutoDealer.dev database connection is not configured.");

            var normalizedEmail = (email ?? string.Empty).Trim().ToLowerInvariant();
            using (var context = new AutoDealerDataContext(connectionString)) {
                var client = context.Clients.SingleOrDefault(x => x.Email == normalizedEmail);
                var credential = client == null ? null : client.ClientCredential;
                if (client == null || credential == null || client.Status != "active") return null;
                var now = DateTime.UtcNow;
                if (credential.LockedUntilUtc.HasValue && credential.LockedUntilUtc.Value > now) return null;

                byte[] candidate;
                using (var derive = new Rfc2898DeriveBytes(password ?? string.Empty, credential.PasswordSalt.ToArray(), credential.PasswordIterations, HashAlgorithmName.SHA256))
                    candidate = derive.GetBytes(credential.PasswordHash.Length);

                if (!FixedTimeEquals(candidate, credential.PasswordHash.ToArray())) {
                    credential.FailedLoginCount++;
                    if (credential.FailedLoginCount >= 5) {
                        credential.LockedUntilUtc = now.AddMinutes(15);
                        credential.FailedLoginCount = 0;
                    }
                    context.SubmitChanges();
                    return null;
                }

                credential.FailedLoginCount = 0;
                credential.LockedUntilUtc = null;
                context.SubmitChanges();
                return BuildDashboard(context, client);
            }
        }

        public AccountDashboardViewModel GetDashboard(string email) {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("The AutoDealer.dev database connection is not configured.");
            var normalizedEmail = (email ?? string.Empty).Trim().ToLowerInvariant();
            using (var context = new AutoDealerDataContext(connectionString)) {
                var client = context.Clients.SingleOrDefault(x => x.Email == normalizedEmail && x.Status == "active");
                return client == null ? null : BuildDashboard(context, client);
            }
        }

        private static AccountDashboardViewModel BuildDashboard(AutoDealerDataContext context, Client client) {
            var subscription = context.Subscriptions
                .Where(x => x.ClientId == client.ClientId)
                .OrderByDescending(x => x.CurrentPeriodEndUtc)
                .Select(x => new { Item = x, Plan = x.Plan })
                .FirstOrDefault();
            var subscriptionStatus = subscription == null ? null : subscription.Item.Status;
            var paymentRequired = subscription != null &&
                (subscription.Item.CurrentPeriodEndUtc <= DateTime.UtcNow ||
                 string.Equals(subscriptionStatus, "paused", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(subscriptionStatus, "past_due", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(subscriptionStatus, "canceled", StringComparison.OrdinalIgnoreCase));
            return new AccountDashboardViewModel {
                ClientNumber = client.ClientNumber,
                BusinessName = client.BusinessName,
                ContactName = client.FirstName + " " + client.LastName,
                Email = client.Email,
                PlanName = subscription == null ? "No active plan" : subscription.Plan.DisplayName,
                SubscriptionStatus = subscription == null ? "unavailable" : subscriptionStatus,
                MonthlyRequestQuota = subscription == null ? 0 : subscription.Plan.MonthlyRequestQuota,
                CurrentPeriodEndUtc = subscription == null ? (DateTime?)null : subscription.Item.CurrentPeriodEndUtc,
                ActiveApiKeyCount = context.ApiKeys.Count(x => x.ClientId == client.ClientId && x.Status == "active"),
                PaymentRequired = paymentRequired,
                PaymentUrl = paymentRequired ? (ConfigurationManager.AppSettings["Billing:PaymentUrl"] ?? string.Empty).Trim() : string.Empty
            };
        }

        private static bool FixedTimeEquals(byte[] left, byte[] right) {
            if (left == null || right == null || left.Length != right.Length) return false;
            var difference = 0;
            for (var i = 0; i < left.Length; i++) difference |= left[i] ^ right[i];
            return difference == 0;
        }

        private static string NormalizePlan(string value) {
            var plan = (value ?? string.Empty).Trim().ToUpperInvariant();
            if (plan.Length == 0 || plan.Length > 32) throw new ArgumentException("Please choose a valid plan.");
            return plan;
        }

        private static string ResolveClientNumber(string value) {
            if (string.IsNullOrWhiteSpace(value)) return ClientNumberGenerator.Generate();
            var clientNumber = value.Trim().ToUpperInvariant();
            if (clientNumber.Length > 32 || !System.Text.RegularExpressions.Regex.IsMatch(clientNumber, @"^DLR-[0-9]{6}-[A-Z0-9_-]{6}$"))
                throw new ArgumentException("The generated client number is invalid.");
            return clientNumber;
        }

        private static byte[] Sha256(string value) {
            using (var hash = SHA256.Create()) return hash.ComputeHash(Encoding.UTF8.GetBytes(value));
        }

        private static byte[] RandomBytes(int count) {
            var bytes = new byte[count];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return bytes;
        }

        private static string RandomToken(int byteCount) {
            return Convert.ToBase64String(RandomBytes(byteCount)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}
