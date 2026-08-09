using autodealer.dev.Data;
using autodealer.dev.Models;
using System;
using System.Configuration;
using System.Data;
using System.Data.Linq;
using System.Diagnostics;
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
            var verificationToken = RandomToken(32);
            var verificationTokenHash = Sha256Hex(verificationToken);
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
                            Status = "pending",
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
                        if (paymentProfile != null) context.PaymentProfiles.InsertOnSubmit(paymentProfile);
                        context.SubmitChanges();
                        clientId = client.ClientId;
                        context.GetTable<ClientEmailVerificationRecord>().InsertOnSubmit(new ClientEmailVerificationRecord {
                            ClientId = clientId,
                            TokenHash = verificationTokenHash,
                            CreatedByAdmin = emailTemporaryPassword,
                            ExpiresUtc = now.AddHours(24),
                            CreatedUtc = now
                        });
                        context.SubmitChanges();
                        transaction.Commit();
                    }
                    catch {
                        transaction.Rollback();
                        throw;
                    }
                }
            }

            var verificationUrl = SeoUrl.Absolute("account/verify-email?token=" + Uri.EscapeDataString(verificationToken));
            var emailed = emailService != null && emailService.SendVerification(clientId, model.FirstName, model.LastName, model.Email.Trim(), verificationUrl);
            return new AccountCreatedViewModel { Email = model.Email, VerificationEmailSent = emailed };
        }

        public EmailVerificationViewModel VerifyEmail(string token) {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("The AutoDealer.dev database connection is not configured.");

            var normalizedToken = (token ?? string.Empty).Trim();
            if (normalizedToken.Length < 40 || normalizedToken.Length > 100 ||
                !System.Text.RegularExpressions.Regex.IsMatch(normalizedToken, @"^[A-Za-z0-9_-]+$"))
                return new EmailVerificationViewModel { Status = EmailVerificationStatus.Invalid };

            var tokenHash = Sha256Hex(normalizedToken);
            var now = DateTime.UtcNow;
            long verificationId = 0;
            long clientId = 0;
            string businessName = null;
            string firstName = null;
            string lastName = null;
            string email = null;
            string phone = null;
            string clientNumber = null;
            string planCode = null;
            bool createdByAdmin = false;
            string fullKey = null;
            var verificationPrepared = false;

            const int maximumConflictAttempts = 3;
            for (var attempt = 1; attempt <= maximumConflictAttempts; attempt++) {
                using (var context = new AutoDealerDataContext(connectionString)) {
                    context.Connection.Open();
                    using (var transaction = context.Connection.BeginTransaction(IsolationLevel.Serializable)) {
                        context.Transaction = transaction;
                        try {
                            var verification = context.GetTable<ClientEmailVerificationRecord>()
                                .SingleOrDefault(x => x.TokenHash == tokenHash);
                            if (verification == null) {
                                transaction.Rollback();
                                return new EmailVerificationViewModel {
                                    Status = EmailVerificationStatus.Invalid
                                };
                            }

                            var client = context.Clients
                                .SingleOrDefault(x => x.ClientId == verification.ClientId);
                            if (client == null) {
                                transaction.Rollback();
                                return new EmailVerificationViewModel {
                                    Status = EmailVerificationStatus.Invalid
                                };
                            }
                            if (verification.CredentialsSentUtc.HasValue) {
                                transaction.Rollback();
                                return new EmailVerificationViewModel {
                                    Status = EmailVerificationStatus.AlreadyVerified,
                                    Email = client.Email
                                };
                            }
                            if (!verification.UsedUtc.HasValue && verification.ExpiresUtc < now) {
                                transaction.Rollback();
                                return new EmailVerificationViewModel {
                                    Status = EmailVerificationStatus.Expired,
                                    Email = client.Email
                                };
                            }

                            var subscription = context.Subscriptions
                                .Where(x => x.ClientId == client.ClientId)
                                .OrderByDescending(x => x.CreatedUtc)
                                .FirstOrDefault();
                            if (subscription == null)
                                throw new InvalidOperationException(
                                    "The workspace subscription could not be found.");

                            var firstConfirmation = !verification.UsedUtc.HasValue;

                            // A failed credential email can be retried with the same confirmation link.
                            // Revoke the undelivered key before issuing its replacement.
                            var activePrimaryKeys = context.ApiKeys.Where(x =>
                                x.ClientId == client.ClientId &&
                                x.Name == "Primary key" &&
                                x.Status == "active");
                            foreach (var oldKey in activePrimaryKeys) {
                                oldKey.Status = "revoked";
                                oldKey.RevokedUtc = now;
                            }

                            var keyId = "ad_live_" + RandomToken(9);
                            var secret = RandomToken(32);
                            fullKey = keyId + "." + secret;
                            context.ApiKeys.InsertOnSubmit(new ApiKey {
                                Client = client,
                                Subscription = subscription,
                                KeyId = keyId,
                                SecretHash = Sha256(secret),
                                KeyPrefix = keyId.Substring(0, Math.Min(20, keyId.Length)),
                                Name = "Primary key",
                                Scopes = "vin:read",
                                Status = "active",
                                CreatedUtc = now
                            });

                            client.Status = "active";
                            client.EmailVerifiedUtc = client.EmailVerifiedUtc ?? now;
                            client.UpdatedUtc = now;
                            if (firstConfirmation) {
                                subscription.CurrentPeriodStartUtc = now;
                                subscription.CurrentPeriodEndUtc = now.AddDays(14);
                                subscription.UpdatedUtc = now;
                            }
                            verification.UsedUtc = verification.UsedUtc ?? now;
                            context.SubmitChanges();

                            verificationId = verification.VerificationId;
                            clientId = client.ClientId;
                            businessName = client.BusinessName;
                            firstName = client.FirstName;
                            lastName = client.LastName;
                            email = client.Email;
                            phone = client.Phone;
                            clientNumber = client.ClientNumber;
                            planCode = subscription.Plan.PlanCode;
                            createdByAdmin = verification.CreatedByAdmin;
                            transaction.Commit();
                            verificationPrepared = true;
                            break;
                        }
                        catch (ChangeConflictException ex) {
                            var conflictedTypes = string.Join(
                                ", ",
                                context.ChangeConflicts
                                    .Select(conflict => conflict.Object.GetType().Name)
                                    .Distinct());
                            transaction.Rollback();
                            Trace.TraceWarning(
                                "Email verification concurrency conflict on attempt {0} of {1}. Records: {2}. {3}",
                                attempt,
                                maximumConflictAttempts,
                                conflictedTypes,
                                ex.Message);
                            if (attempt == maximumConflictAttempts) throw;
                        }
                        catch {
                            transaction.Rollback();
                            throw;
                        }
                    }
                }
            }

            if (!verificationPrepared)
                throw new InvalidOperationException("Email verification could not be prepared.");

            var credentialsSent = emailService != null && emailService.SendCredentials(
                clientId, businessName, firstName, lastName, email, phone, clientNumber, fullKey, planCode, createdByAdmin);
            if (!credentialsSent)
                return new EmailVerificationViewModel { Status = EmailVerificationStatus.DeliveryFailed, Email = email };

            try {
                using (var context = new AutoDealerDataContext(connectionString)) {
                    var verification = context.GetTable<ClientEmailVerificationRecord>()
                        .Single(x => x.VerificationId == verificationId);
                    verification.CredentialsSentUtc = DateTime.UtcNow;
                    context.SubmitChanges();
                }
            }
            catch (Exception ex) {
                Trace.TraceError("Email verification delivery status could not be recorded: {0}", ex);
            }

            return new EmailVerificationViewModel { Status = EmailVerificationStatus.Verified, Email = email };
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

        private static string Sha256Hex(string value) {
            var bytes = Sha256(value);
            var output = new StringBuilder(bytes.Length * 2);
            foreach (var item in bytes) output.Append(item.ToString("x2"));
            return output.ToString();
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
