using autodealer.dev.Data;
using autodealer.dev.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace autodealer.dev.Services {
    public sealed class AdminService : IAdminService {
        private readonly string connectionString;

        public AdminService() {
            connectionString = AutoDealerConnectionString.Resolve();
        }

        public bool Authenticate(string userId, string password) {
            var configuredUser = ConfigurationManager.AppSettings["AdminAuth:UserId"];
            var saltValue = ConfigurationManager.AppSettings["AdminAuth:PasswordSalt"];
            var hashValue = ConfigurationManager.AppSettings["AdminAuth:PasswordHash"];
            var iterationsValue = ConfigurationManager.AppSettings["AdminAuth:PasswordIterations"];

            int iterations;
            if (string.IsNullOrWhiteSpace(configuredUser) ||
                string.IsNullOrWhiteSpace(saltValue) ||
                string.IsNullOrWhiteSpace(hashValue) ||
                !int.TryParse(iterationsValue, out iterations) || iterations < 100000)
                throw new InvalidOperationException("Administrator authentication is not configured correctly.");

            var userMatches = string.Equals(
                (userId ?? string.Empty).Trim(),
                configuredUser,
                StringComparison.OrdinalIgnoreCase);
            byte[] salt;
            byte[] expectedHash;
            try {
                salt = Convert.FromBase64String(saltValue);
                expectedHash = Convert.FromBase64String(hashValue);
            }
            catch (FormatException) {
                throw new InvalidOperationException("Administrator authentication is not configured correctly.");
            }

            byte[] candidateHash;
            using (var derive = new Rfc2898DeriveBytes(
                password ?? string.Empty,
                salt,
                iterations,
                HashAlgorithmName.SHA256))
                candidateHash = derive.GetBytes(expectedHash.Length);

            return userMatches && FixedTimeEquals(candidateHash, expectedHash);
        }

        public AdminDashboardViewModel GetDashboard() {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("The AutoDealer.dev database connection is not configured.");

            using (var context = new AutoDealerDataContext(connectionString)) {
                var clients = context.Clients
                    .Where(x => x.Status == "active")
                    .OrderByDescending(x => x.CreatedUtc)
                    .ToList();
                var clientIds = clients.Select(x => x.ClientId).ToList();

                var subscriptions = context.Subscriptions
                    .Where(x => clientIds.Contains(x.ClientId))
                    .Select(x => new {
                        x.ClientId,
                        x.Status,
                        x.CurrentPeriodEndUtc,
                        PlanName = x.Plan.DisplayName
                    })
                    .ToList()
                    .GroupBy(x => x.ClientId)
                    .ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.CurrentPeriodEndUtc).First());
                var subscriptionCounts = context.Subscriptions
                    .Where(x => clientIds.Contains(x.ClientId))
                    .GroupBy(x => x.ClientId)
                    .Select(x => new { ClientId = x.Key, Count = x.Count() })
                    .ToDictionary(x => x.ClientId, x => x.Count);

                var keyCounts = context.ApiKeys
                    .Where(x => clientIds.Contains(x.ClientId) && x.Status == "active")
                    .GroupBy(x => x.ClientId)
                    .Select(x => new { ClientId = x.Key, Count = x.Count() })
                    .ToDictionary(x => x.ClientId, x => x.Count);
                var allKeyCounts = context.ApiKeys
                    .Where(x => clientIds.Contains(x.ClientId))
                    .GroupBy(x => x.ClientId)
                    .Select(x => new { ClientId = x.Key, Count = x.Count() })
                    .ToDictionary(x => x.ClientId, x => x.Count);
                var emailCounts = GetClientEmailCounts(clientIds);

                var customerRows = new List<AdminCustomerViewModel>();
                foreach (var client in clients) {
                    var subscription = subscriptions.ContainsKey(client.ClientId)
                        ? subscriptions[client.ClientId]
                        : null;
                    customerRows.Add(new AdminCustomerViewModel {
                        ClientId = client.ClientId,
                        ClientNumber = client.ClientNumber,
                        BusinessName = client.BusinessName,
                        ContactName = client.FirstName + " " + client.LastName,
                        Email = client.Email,
                        PlanName = subscription == null ? "No plan" : subscription.PlanName,
                        SubscriptionStatus = subscription == null ? "unavailable" : subscription.Status,
                        PeriodEndUtc = subscription == null ? (DateTime?)null : subscription.CurrentPeriodEndUtc,
                        ActiveApiKeyCount = keyCounts.ContainsKey(client.ClientId) ? keyCounts[client.ClientId] : 0,
                        ApiKeyCount = allKeyCounts.ContainsKey(client.ClientId) ? allKeyCounts[client.ClientId] : 0,
                        SubscriptionCount = subscriptionCounts.ContainsKey(client.ClientId)
                            ? subscriptionCounts[client.ClientId]
                            : 0,
                        EmailCount = emailCounts.ContainsKey(client.ClientId) ? emailCounts[client.ClientId] : 0,
                        CreatedUtc = client.CreatedUtc
                    });
                }

                return new AdminDashboardViewModel {
                    TotalCustomers = context.Clients.Count(),
                    ActiveCustomers = customerRows.Count,
                    TrialingSubscriptions = context.Subscriptions.Count(x => x.Status == "trialing"),
                    ActiveApiKeys = context.ApiKeys.Count(x => x.Status == "active"),
                    Customers = customerRows,
                    DemoRequests = GetDemoRequests()
                };
            }
        }

        public IReadOnlyList<AdminClientEmailViewModel> GetClientEmails(long clientId) {
            var emails = new List<AdminClientEmailViewModel>();
            if (clientId <= 0 || string.IsNullOrWhiteSpace(connectionString)) return emails;

            using (var connection = new SqlConnection(connectionString)) {
                connection.Open();
                if (!TableExists(connection, "dbo.ClientEmailHistory")) return emails;

                const string sql = @"SELECT TOP (250)
                    ClientEmailHistoryId,ClientId,SentUtc,ToEmail,Subject,HtmlBody
                    FROM dbo.ClientEmailHistory
                    WHERE ClientId=@ClientId
                    ORDER BY SentUtc DESC,ClientEmailHistoryId DESC;";
                using (var command = new SqlCommand(sql, connection)) {
                    command.Parameters.Add("@ClientId", SqlDbType.BigInt).Value = clientId;
                    using (var reader = command.ExecuteReader()) {
                        while (reader.Read()) {
                            emails.Add(new AdminClientEmailViewModel {
                                ClientEmailHistoryId = reader.GetInt64(0),
                                ClientId = reader.GetInt64(1),
                                SentUtc = reader.GetDateTime(2),
                                ToEmail = reader.GetString(3),
                                Subject = reader.GetString(4),
                                HtmlBody = reader.GetString(5)
                            });
                        }
                    }
                }
            }
            return emails;
        }

        public AdminCustomerAccountDetailViewModel GetClientAccountDetails(long clientId) {
            var empty = new AdminCustomerAccountDetailViewModel {
                ApiKeys = new List<AdminApiKeyViewModel>(),
                Subscriptions = new List<AdminSubscriptionViewModel>()
            };
            if (clientId <= 0 || string.IsNullOrWhiteSpace(connectionString)) return empty;

            using (var context = new AutoDealerDataContext(connectionString)) {
                if (!context.Clients.Any(x => x.ClientId == clientId)) return empty;

                var apiKeys = context.ApiKeys
                    .Where(x => x.ClientId == clientId)
                    .OrderByDescending(x => x.CreatedUtc)
                    .Select(x => new AdminApiKeyViewModel {
                        ApiKeyId = x.ApiKeyId,
                        Name = x.Name,
                        KeyPrefix = x.KeyPrefix,
                        Scopes = x.Scopes,
                        Status = x.Status,
                        CreatedUtc = x.CreatedUtc,
                        LastUsedUtc = x.LastUsedUtc,
                        ExpiresUtc = x.ExpiresUtc,
                        RevokedUtc = x.RevokedUtc
                    }).ToList();

                var subscriptions = context.Subscriptions
                    .Where(x => x.ClientId == clientId)
                    .OrderByDescending(x => x.CurrentPeriodEndUtc)
                    .Select(x => new AdminSubscriptionViewModel {
                        SubscriptionId = x.SubscriptionId,
                        PlanName = x.Plan.DisplayName,
                        PlanCode = x.Plan.PlanCode,
                        Status = x.Status,
                        MonthlyRequestQuota = x.Plan.MonthlyRequestQuota,
                        MaxApiKeys = x.Plan.MaxApiKeys,
                        CurrentPeriodStartUtc = x.CurrentPeriodStartUtc,
                        CurrentPeriodEndUtc = x.CurrentPeriodEndUtc,
                        CancelAtPeriodEnd = x.CancelAtPeriodEnd,
                        ProviderSubscriptionId = x.ProviderSubscriptionId,
                        CreatedUtc = x.CreatedUtc
                    }).ToList();

                return new AdminCustomerAccountDetailViewModel { ApiKeys = apiKeys, Subscriptions = subscriptions };
            }
        }

        public AdminClientEditViewModel GetClientForEdit(long clientId) {
            EnsureDatabaseConfigured();
            using (var context = new AutoDealerDataContext(connectionString)) {
                var client = context.Clients.SingleOrDefault(x => x.ClientId == clientId);
                if (client == null) throw new KeyNotFoundException("The dealer account could not be found.");
                return MapClientEdit(client);
            }
        }

        public AdminClientCreateViewModel GetNewClientDefaults() {
            EnsureDatabaseConfigured();
            using (var context = new AutoDealerDataContext(connectionString)) {
                string clientNumber = null;
                for (var attempt = 0; attempt < 20; attempt++) {
                    var candidate = ClientNumberGenerator.Generate();
                    if (!context.Clients.Any(x => x.ClientNumber == candidate)) {
                        clientNumber = candidate;
                        break;
                    }
                }
                if (clientNumber == null)
                    throw new InvalidOperationException(
                        "A unique client number could not be generated. Try again.");

                return new AdminClientCreateViewModel {
                    ClientNumber = clientNumber,
                    TemporaryPassword = ClientNumberGenerator.GenerateTemporaryPassword(),
                    ConfirmTemporaryPassword = null,
                    PlanOptions = new List<AdminEditOptionViewModel>()
                };
            }
        }

        public AdminClientEditViewModel UpdateClient(AdminClientEditViewModel model) {
            if (model == null) throw new ArgumentNullException("model");
            EnsureDatabaseConfigured();

            var businessName = (model.BusinessName ?? string.Empty).Trim();
            var firstName = (model.FirstName ?? string.Empty).Trim();
            var lastName = (model.LastName ?? string.Empty).Trim();
            var email = (model.Email ?? string.Empty).Trim().ToLowerInvariant();
            var phone = string.IsNullOrWhiteSpace(model.Phone) ? null : model.Phone.Trim();
            var status = (model.Status ?? string.Empty).Trim().ToLowerInvariant();
            if (businessName.Length == 0 || businessName.Length > 160)
                throw new ArgumentException("Enter a business name of 160 characters or fewer.");
            if (firstName.Length == 0 || firstName.Length > 80 || lastName.Length == 0 || lastName.Length > 80)
                throw new ArgumentException("Enter both contact names using 80 characters or fewer.");
            if (email.Length == 0 || email.Length > 254)
                throw new ArgumentException("Enter a valid account email address.");
            if (phone != null && phone.Length > 32)
                throw new ArgumentException("Enter a phone number of 32 characters or fewer.");
            if (status != "pending" && status != "active" && status != "suspended" && status != "closed")
                throw new ArgumentException("Select a valid dealer account status.");

            using (var context = new AutoDealerDataContext(connectionString)) {
                var client = context.Clients.SingleOrDefault(x => x.ClientId == model.ClientId);
                if (client == null) throw new KeyNotFoundException("The dealer account could not be found.");
                if (context.Clients.Any(x => x.ClientId != model.ClientId && x.Email == email))
                    throw new ArgumentException("Another dealer account already uses this email address.");

                client.BusinessName = businessName;
                client.FirstName = firstName;
                client.LastName = lastName;
                client.Email = email;
                client.Phone = phone;
                client.Status = status;
                client.EmailVerifiedUtc = model.EmailVerifiedUtc.HasValue
                    ? DateTime.SpecifyKind(model.EmailVerifiedUtc.Value, DateTimeKind.Utc)
                    : (DateTime?)null;
                client.UpdatedUtc = DateTime.UtcNow;
                context.SubmitChanges();
                return MapClientEdit(client);
            }
        }

        public string DeleteClient(long clientId) {
            if (clientId <= 0) throw new ArgumentException("Select a valid dealer account.");
            EnsureDatabaseConfigured();

            using (var context = new AutoDealerDataContext(connectionString)) {
                context.CommandTimeout = 120;
                if (context.Connection.State == ConnectionState.Closed) context.Connection.Open();
                using (var transaction = context.Connection.BeginTransaction(IsolationLevel.Serializable)) {
                    context.Transaction = transaction;
                    try {
                        var client = context.Clients.SingleOrDefault(x => x.ClientId == clientId);
                        if (client == null) throw new KeyNotFoundException("The dealer account could not be found.");
                        var businessName = client.BusinessName;

                        context.ApiUsageLogs.DeleteAllOnSubmit(context.ApiUsageLogs.Where(x => x.ClientId == clientId));
                        context.ApiUsageDailies.DeleteAllOnSubmit(
                            context.ApiUsageDailies.Where(x => x.ClientId == clientId));
                        context.SubmitChanges();

                        context.ApiKeys.DeleteAllOnSubmit(context.ApiKeys.Where(x => x.ClientId == clientId));
                        context.SubmitChanges();

                        context.PaymentProfiles.DeleteAllOnSubmit(
                            context.PaymentProfiles.Where(x => x.ClientId == clientId));
                        context.ClientCredentials.DeleteAllOnSubmit(
                            context.ClientCredentials.Where(x => x.ClientId == clientId));
                        context.SubmitChanges();

                        context.Subscriptions.DeleteAllOnSubmit(
                            context.Subscriptions.Where(x => x.ClientId == clientId));
                        context.SubmitChanges();

                        context.Clients.DeleteOnSubmit(client);
                        context.SubmitChanges();
                        transaction.Commit();
                        return businessName;
                    }
                    catch {
                        try { transaction.Rollback(); }
                        catch { }
                        throw;
                    }
                }
            }
        }

        public string DeleteDemoRequest(Guid requestId) {
            if (requestId == Guid.Empty) throw new ArgumentException("Select a valid opportunity.");
            EnsureDatabaseConfigured();

            using (var context = new AutoDealerDataContext(connectionString)) {
                var requests = context.GetTable<DealerDemoRequestRecord>();
                var request = requests.SingleOrDefault(x => x.RequestId == requestId);
                if (request == null) throw new KeyNotFoundException("The opportunity could not be found.");
                var businessName = request.BusinessName;
                requests.DeleteOnSubmit(request);
                context.SubmitChanges();
                return businessName;
            }
        }

        public AdminDemoRequestEditViewModel GetNewDemoRequestDefaults() {
            return new AdminDemoRequestEditViewModel {
                RequestId = Guid.Empty,
                PreferredContact = "Email",
                Status = "new",
                CreatedUtc = DateTime.UtcNow
            };
        }

        public AdminDemoRequestEditViewModel GetDemoRequestForEdit(Guid requestId) {
            if (requestId == Guid.Empty) throw new ArgumentException("Select a valid opportunity.");
            EnsureDatabaseConfigured();
            using (var context = new AutoDealerDataContext(connectionString)) {
                var request = context.GetTable<DealerDemoRequestRecord>()
                    .SingleOrDefault(x => x.RequestId == requestId);
                if (request == null) throw new KeyNotFoundException("The opportunity could not be found.");
                return MapDemoRequestEdit(request);
            }
        }

        public AdminDemoRequestEditViewModel SaveDemoRequest(AdminDemoRequestEditViewModel model, bool create) {
            if (model == null) throw new ArgumentNullException("model");
            EnsureDatabaseConfigured();

            var businessName = (model.BusinessName ?? string.Empty).Trim();
            var contactName = (model.ContactName ?? string.Empty).Trim();
            var email = (model.Email ?? string.Empty).Trim().ToLowerInvariant();
            var phone = string.IsNullOrWhiteSpace(model.Phone) ? null : model.Phone.Trim();
            var website = string.IsNullOrWhiteSpace(model.CurrentWebsite) ? null : model.CurrentWebsite.Trim();
            var inventorySize = (model.InventorySize ?? string.Empty).Trim();
            var primaryGoal = (model.PrimaryGoal ?? string.Empty).Trim();
            var preferredContact = string.Equals(
                model.PreferredContact,
                "Phone",
                StringComparison.OrdinalIgnoreCase)
                    ? "Phone"
                    : "Email";
            var message = (model.Message ?? string.Empty).Trim();
            var status = (model.Status ?? string.Empty).Trim().ToLowerInvariant();

            if (businessName.Length == 0 || businessName.Length > 160)
                throw new ArgumentException("Enter a business name of 160 characters or fewer.");
            if (contactName.Length == 0 || contactName.Length > 160)
                throw new ArgumentException("Enter a contact name of 160 characters or fewer.");
            if (email.Length == 0 || email.Length > 254)
                throw new ArgumentException("Enter a valid contact email address.");
            if (phone != null && phone.Length > 32)
                throw new ArgumentException("Enter a phone number of 32 characters or fewer.");
            if (preferredContact == "Phone" && phone == null)
                throw new ArgumentException("Enter a phone number when phone is the preferred contact method.");
            if (website != null && website.Length > 300)
                throw new ArgumentException("Enter a website of 300 characters or fewer.");
            if (!model.LocationCount.HasValue || model.LocationCount.Value < 1 || model.LocationCount.Value > 1000)
                throw new ArgumentException("Enter a location count between 1 and 1000.");
            if (inventorySize.Length == 0 || inventorySize.Length > 80)
                throw new ArgumentException("Enter an inventory size of 80 characters or fewer.");
            if (primaryGoal.Length == 0 || primaryGoal.Length > 120)
                throw new ArgumentException("Enter a primary goal of 120 characters or fewer.");
            if (message.Length == 0 || message.Length > 3000)
                throw new ArgumentException("Enter an opportunity message of 3000 characters or fewer.");
            if (status != "new" && status != "active" && status != "postponed" && status != "closed")
                throw new ArgumentException("Select a valid opportunity status.");

            using (var context = new AutoDealerDataContext(connectionString)) {
                var requests = context.GetTable<DealerDemoRequestRecord>();
                DealerDemoRequestRecord request;
                if (create) {
                    request = new DealerDemoRequestRecord {
                        RequestId = Guid.NewGuid(),
                        CreatedUtc = DateTime.UtcNow
                    };
                    requests.InsertOnSubmit(request);
                } else {
                    if (model.RequestId == Guid.Empty) throw new ArgumentException("Select a valid opportunity.");
                    request = requests.SingleOrDefault(x => x.RequestId == model.RequestId);
                    if (request == null) throw new KeyNotFoundException("The opportunity could not be found.");
                }

                request.BusinessName = businessName;
                request.ContactName = contactName;
                request.Email = email;
                request.Phone = phone;
                request.CurrentWebsite = website;
                request.LocationCount = model.LocationCount;
                request.InventorySize = inventorySize;
                request.PrimaryGoal = primaryGoal;
                request.PreferredContact = preferredContact;
                request.Message = message;
                request.Status = status;
                context.SubmitChanges();
                return MapDemoRequestEdit(request);
            }
        }

        public AdminApiKeyEditViewModel GetApiKeyForEdit(long apiKeyId) {
            EnsureDatabaseConfigured();
            using (var context = new AutoDealerDataContext(connectionString)) {
                var apiKey = context.ApiKeys.SingleOrDefault(x => x.ApiKeyId == apiKeyId);
                if (apiKey == null) throw new KeyNotFoundException("The API key could not be found.");
                return MapApiKeyEdit(context, apiKey);
            }
        }

        public AdminApiKeyEditViewModel UpdateApiKey(AdminApiKeyEditViewModel model) {
            if (model == null) throw new ArgumentNullException("model");
            EnsureDatabaseConfigured();

            var name = (model.Name ?? string.Empty).Trim();
            var scopes = (model.Scopes ?? string.Empty).Trim().ToLowerInvariant();
            var status = (model.Status ?? string.Empty).Trim().ToLowerInvariant();
            if (name.Length == 0 || name.Length > 80)
                throw new ArgumentException("Enter an API key name of 80 characters or fewer.");
            if (scopes != "vin:read") throw new ArgumentException("Select a supported API scope.");
            if (status != "active" && status != "revoked" && status != "expired")
                throw new ArgumentException("Select a valid API key status.");
            if (status == "active" && model.ExpiresUtc.HasValue && model.ExpiresUtc.Value <= DateTime.UtcNow)
                throw new ArgumentException(
                    "An active API key must have a future expiration date or no expiration date.");

            using (var context = new AutoDealerDataContext(connectionString)) {
                var apiKey = context.ApiKeys.SingleOrDefault(x => x.ApiKeyId == model.ApiKeyId);
                if (apiKey == null) throw new KeyNotFoundException("The API key could not be found.");
                var subscriptionExists = context.Subscriptions.Any(x =>
                    x.SubscriptionId == model.SubscriptionId &&
                    x.ClientId == apiKey.ClientId);
                if (!subscriptionExists)
                    throw new ArgumentException("Select a subscription belonging to this customer.");

                var now = DateTime.UtcNow;
                apiKey.Name = name;
                apiKey.Scopes = scopes;
                apiKey.Status = status;
                apiKey.SubscriptionId = model.SubscriptionId;
                apiKey.ExpiresUtc = model.ExpiresUtc.HasValue
                    ? DateTime.SpecifyKind(model.ExpiresUtc.Value, DateTimeKind.Utc)
                    : (DateTime?)null;
                if (status == "revoked") apiKey.RevokedUtc = apiKey.RevokedUtc ?? now;
                else apiKey.RevokedUtc = null;
                if (status == "expired" &&
                    (!apiKey.ExpiresUtc.HasValue || apiKey.ExpiresUtc.Value > now)) {
                    apiKey.ExpiresUtc = now;
                }
                context.SubmitChanges();
                return MapApiKeyEdit(context, apiKey);
            }
        }

        public AdminSubscriptionEditViewModel GetNewSubscriptionDefaults(long clientId) {
            EnsureDatabaseConfigured();
            using (var context = new AutoDealerDataContext(connectionString)) {
                if (!context.Clients.Any(x => x.ClientId == clientId))
                    throw new KeyNotFoundException("The dealer account could not be found.");
                var plan = context.Plans.Where(x => x.IsActive).OrderBy(x => x.PlanId).FirstOrDefault();
                if (plan == null)
                    throw new InvalidOperationException(
                        "Create or activate a subscription plan before adding a subscription.");
                var now = DateTime.UtcNow;
                return new AdminSubscriptionEditViewModel {
                    ClientId = clientId,
                    PlanId = plan.PlanId,
                    Status = "active",
                    CurrentPeriodStartUtc = now,
                    CurrentPeriodEndUtc = now.AddMonths(1),
                    CancelAtPeriodEnd = false,
                    PlanOptions = GetSubscriptionPlanOptions(context)
                };
            }
        }

        public AdminSubscriptionEditViewModel CreateSubscription(AdminSubscriptionEditViewModel model) {
            if (model == null) throw new ArgumentNullException("model");
            EnsureDatabaseConfigured();
            var status = ValidateSubscription(model);

            using (var context = new AutoDealerDataContext(connectionString)) {
                if (!context.Clients.Any(x => x.ClientId == model.ClientId))
                    throw new KeyNotFoundException("The dealer account could not be found.");
                if (!context.Plans.Any(x => x.PlanId == model.PlanId))
                    throw new ArgumentException("Select a valid subscription plan.");

                var now = DateTime.UtcNow;
                var subscription = new Subscription {
                    ClientId = model.ClientId,
                    PlanId = model.PlanId,
                    Status = status,
                    CurrentPeriodStartUtc = DateTime.SpecifyKind(model.CurrentPeriodStartUtc, DateTimeKind.Utc),
                    CurrentPeriodEndUtc = DateTime.SpecifyKind(model.CurrentPeriodEndUtc, DateTimeKind.Utc),
                    CancelAtPeriodEnd = model.CancelAtPeriodEnd,
                    ProviderSubscriptionId = string.IsNullOrWhiteSpace(model.ProviderSubscriptionId)
                        ? null
                        : model.ProviderSubscriptionId.Trim(),
                    CreatedUtc = now,
                    UpdatedUtc = now
                };
                context.Subscriptions.InsertOnSubmit(subscription);
                context.SubmitChanges();
                return MapSubscriptionEdit(context, subscription);
            }
        }

        public AdminSubscriptionEditViewModel GetSubscriptionForEdit(long subscriptionId) {
            EnsureDatabaseConfigured();
            using (var context = new AutoDealerDataContext(connectionString)) {
                var subscription = context.Subscriptions.SingleOrDefault(x => x.SubscriptionId == subscriptionId);
                if (subscription == null) throw new KeyNotFoundException("The subscription could not be found.");
                return MapSubscriptionEdit(context, subscription);
            }
        }

        public AdminSubscriptionEditViewModel UpdateSubscription(AdminSubscriptionEditViewModel model) {
            if (model == null) throw new ArgumentNullException("model");
            EnsureDatabaseConfigured();

            var status = ValidateSubscription(model);

            using (var context = new AutoDealerDataContext(connectionString)) {
                var subscription = context.Subscriptions.SingleOrDefault(x => x.SubscriptionId == model.SubscriptionId);
                if (subscription == null) throw new KeyNotFoundException("The subscription could not be found.");
                if (!context.Plans.Any(x => x.PlanId == model.PlanId))
                    throw new ArgumentException("Select a valid subscription plan.");

                subscription.PlanId = model.PlanId;
                subscription.Status = status;
                subscription.CurrentPeriodStartUtc = DateTime.SpecifyKind(
                    model.CurrentPeriodStartUtc,
                    DateTimeKind.Utc);
                subscription.CurrentPeriodEndUtc = DateTime.SpecifyKind(model.CurrentPeriodEndUtc, DateTimeKind.Utc);
                subscription.CancelAtPeriodEnd = model.CancelAtPeriodEnd;
                subscription.ProviderSubscriptionId = string.IsNullOrWhiteSpace(model.ProviderSubscriptionId)
                    ? null
                    : model.ProviderSubscriptionId.Trim();
                subscription.UpdatedUtc = DateTime.UtcNow;
                context.SubmitChanges();
                return MapSubscriptionEdit(context, subscription);
            }
        }

        private static AdminApiKeyEditViewModel MapApiKeyEdit(AutoDealerDataContext context, ApiKey apiKey) {
            var subscriptions = context.Subscriptions
                .Where(x => x.ClientId == apiKey.ClientId)
                .OrderByDescending(x => x.CurrentPeriodEndUtc)
                .Select(x => new { x.SubscriptionId, x.Status, PlanName = x.Plan.DisplayName })
                .ToList()
                .Select(x => new AdminEditOptionViewModel {
                    Value = x.SubscriptionId.ToString(),
                    Text = "#" + x.SubscriptionId + " — " + x.PlanName + " (" + x.Status + ")"
                }).ToList();
            return new AdminApiKeyEditViewModel {
                ApiKeyId = apiKey.ApiKeyId,
                ClientId = apiKey.ClientId,
                Name = apiKey.Name,
                Scopes = apiKey.Scopes,
                Status = apiKey.Status,
                SubscriptionId = apiKey.SubscriptionId,
                ExpiresUtc = apiKey.ExpiresUtc,
                KeyPrefix = apiKey.KeyPrefix,
                SubscriptionOptions = subscriptions
            };
        }

        private static AdminClientEditViewModel MapClientEdit(Client client) {
            return new AdminClientEditViewModel {
                ClientId = client.ClientId,
                ClientNumber = client.ClientNumber,
                BusinessName = client.BusinessName,
                FirstName = client.FirstName,
                LastName = client.LastName,
                Email = client.Email,
                Phone = client.Phone,
                Status = client.Status,
                EmailVerifiedUtc = client.EmailVerifiedUtc,
                CreatedUtc = client.CreatedUtc
            };
        }

        private static AdminDemoRequestEditViewModel MapDemoRequestEdit(DealerDemoRequestRecord request) {
            return new AdminDemoRequestEditViewModel {
                RequestId = request.RequestId,
                BusinessName = request.BusinessName,
                ContactName = request.ContactName,
                Email = request.Email,
                Phone = request.Phone,
                CurrentWebsite = request.CurrentWebsite,
                LocationCount = request.LocationCount,
                InventorySize = request.InventorySize,
                PrimaryGoal = request.PrimaryGoal,
                PreferredContact = request.PreferredContact,
                Message = request.Message,
                Status = request.Status,
                CreatedUtc = request.CreatedUtc
            };
        }

        private static string ValidateSubscription(AdminSubscriptionEditViewModel model) {
            var status = (model.Status ?? string.Empty).Trim().ToLowerInvariant();
            if (status != "trialing" &&
                status != "active" &&
                status != "past_due" &&
                status != "paused" &&
                status != "canceled")
                throw new ArgumentException("Select a valid subscription status.");
            if (model.CurrentPeriodEndUtc <= model.CurrentPeriodStartUtc)
                throw new ArgumentException("The period end must be later than the period start.");
            return status;
        }

        private static IReadOnlyList<AdminEditOptionViewModel> GetSubscriptionPlanOptions(
            AutoDealerDataContext context) {
            return context.Plans
                .OrderByDescending(x => x.IsActive)
                .ThenBy(x => x.DisplayName)
                .Select(x => new { x.PlanId, x.DisplayName, x.PlanCode, x.IsActive })
                .ToList()
                .Select(x => new AdminEditOptionViewModel {
                    Value = x.PlanId.ToString(),
                    Text = x.DisplayName + " (" + x.PlanCode + ")" + (x.IsActive ? string.Empty : " — inactive")
                }).ToList();
        }

        private static AdminSubscriptionEditViewModel MapSubscriptionEdit(
            AutoDealerDataContext context,
            Subscription subscription) {
            return new AdminSubscriptionEditViewModel {
                SubscriptionId = subscription.SubscriptionId,
                ClientId = subscription.ClientId,
                PlanId = subscription.PlanId,
                Status = subscription.Status,
                CurrentPeriodStartUtc = subscription.CurrentPeriodStartUtc,
                CurrentPeriodEndUtc = subscription.CurrentPeriodEndUtc,
                CancelAtPeriodEnd = subscription.CancelAtPeriodEnd,
                ProviderSubscriptionId = subscription.ProviderSubscriptionId,
                PlanOptions = GetSubscriptionPlanOptions(context)
            };
        }

        private void EnsureDatabaseConfigured() {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("The AutoDealer.dev database connection is not configured.");
        }

        private IDictionary<long, int> GetClientEmailCounts(IReadOnlyCollection<long> clientIds) {
            var counts = new Dictionary<long, int>();
            if (clientIds == null || clientIds.Count == 0) return counts;

            using (var connection = new SqlConnection(connectionString)) {
                connection.Open();
                if (!TableExists(connection, "dbo.ClientEmailHistory")) return counts;
                const string sql = @"SELECT ClientId,COUNT(*)
                    FROM dbo.ClientEmailHistory
                    GROUP BY ClientId;";
                using (var command = new SqlCommand(sql, connection))
                using (var reader = command.ExecuteReader()) {
                    while (reader.Read()) {
                        var clientId = reader.GetInt64(0);
                        if (clientIds.Contains(clientId)) counts[clientId] = reader.GetInt32(1);
                    }
                }
            }
            return counts;
        }

        private static bool TableExists(SqlConnection connection, string tableName) {
            using (var command = new SqlCommand("SELECT OBJECT_ID(@TableName, 'U');", connection)) {
                command.Parameters.Add("@TableName", SqlDbType.NVarChar, 260).Value = tableName;
                return command.ExecuteScalar() != DBNull.Value;
            }
        }

        private IReadOnlyList<AdminDemoRequestViewModel> GetDemoRequests() {
            var requests = new List<AdminDemoRequestViewModel>();
            using (var connection = new SqlConnection(connectionString)) {
                connection.Open();
                if (!TableExists(connection, "dbo.DealerDemoRequests")) return requests;

                const string sql = @"SELECT TOP (100)
                    RequestId,BusinessName,ContactName,Email,Phone,CurrentWebsite,LocationCount,
                    InventorySize,PrimaryGoal,PreferredContact,Message,Status,CreatedUtc
                    FROM dbo.DealerDemoRequests ORDER BY CreatedUtc DESC;";
                using (var command = new SqlCommand(sql, connection))
                using (var reader = command.ExecuteReader()) {
                    while (reader.Read()) {
                        var preferredContact = reader.GetString(9);
                        var email = reader.GetString(3);
                        var phone = reader.IsDBNull(4) ? null : reader.GetString(4);
                        var website = reader.IsDBNull(5) ? null : reader.GetString(5);
                        var prefersPhone = string.Equals(
                            preferredContact,
                            "Phone",
                            StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(phone);
                        requests.Add(new AdminDemoRequestViewModel {
                            RequestId = reader.GetGuid(0),
                            BusinessName = reader.GetString(1),
                            ContactName = reader.GetString(2),
                            Email = email,
                            Phone = phone,
                            CurrentWebsite = website,
                            WebsiteHref = WebsiteHref(website),
                            LocationCount = reader.IsDBNull(6) ? (int?)null : reader.GetInt32(6),
                            InventorySize = reader.GetString(7),
                            PrimaryGoal = reader.GetString(8),
                            PreferredContact = preferredContact,
                            Message = reader.GetString(10),
                            Status = reader.GetString(11),
                            CreatedUtc = reader.GetDateTime(12),
                            ContactHref = prefersPhone
                                ? PhoneHref(phone)
                                : EmailHref(email, reader.GetString(1), reader.GetString(2)),
                            ContactAction = prefersPhone ? "Call" : "Reply"
                        });
                    }
                }
            }
            return requests;
        }

        private static string WebsiteHref(string website) {
            if (string.IsNullOrWhiteSpace(website)) return null;
            var value = website.Trim();
            return value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? value
                : "https://" + value;
        }

        private static string PhoneHref(string phone) {
            var normalized = new StringBuilder();
            foreach (var character in phone ?? string.Empty) {
                if (char.IsDigit(character) ||
                    (character == '+' && normalized.Length == 0)) {
                    normalized.Append(character);
                }
            }
            return "tel:" + normalized;
        }

        private static string EmailHref(string email, string businessName, string contactName) {
            return "mailto:" + email + "?subject=" + Uri.EscapeDataString("Your AutoDealer.dev dealer demo") +
                "&body=" + Uri.EscapeDataString(
                    "Hi " + contactName + ",\r\n\r\n" +
                    "Thank you for reaching out about " + businessName + ". ");
        }

        private static bool FixedTimeEquals(byte[] left, byte[] right) {
            if (left == null || right == null || left.Length != right.Length) return false;
            var difference = 0;
            for (var index = 0; index < left.Length; index++) difference |= left[index] ^ right[index];
            return difference == 0;
        }
    }
}
