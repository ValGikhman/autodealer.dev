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

            var userMatches = string.Equals((userId ?? string.Empty).Trim(), configuredUser, StringComparison.OrdinalIgnoreCase);
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
            using (var derive = new Rfc2898DeriveBytes(password ?? string.Empty, salt, iterations, HashAlgorithmName.SHA256))
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

                var keyCounts = context.ApiKeys
                    .Where(x => clientIds.Contains(x.ClientId) && x.Status == "active")
                    .GroupBy(x => x.ClientId)
                    .Select(x => new { ClientId = x.Key, Count = x.Count() })
                    .ToDictionary(x => x.ClientId, x => x.Count);

                var customerRows = new List<AdminCustomerViewModel>();
                foreach (var client in clients) {
                    var subscription = subscriptions.ContainsKey(client.ClientId) ? subscriptions[client.ClientId] : null;
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

        private IReadOnlyList<AdminDemoRequestViewModel> GetDemoRequests() {
            var requests = new List<AdminDemoRequestViewModel>();
            using (var connection = new SqlConnection(connectionString)) {
                connection.Open();
                using (var exists = new SqlCommand("SELECT OBJECT_ID('dbo.DealerDemoRequests', 'U');", connection)) {
                    if (exists.ExecuteScalar() == DBNull.Value) return requests;
                }

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
                        var prefersPhone = string.Equals(preferredContact, "Phone", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(phone);
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
                            ContactHref = prefersPhone ? PhoneHref(phone) : EmailHref(email, reader.GetString(1), reader.GetString(2)),
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
            return value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                ? value
                : "https://" + value;
        }

        private static string PhoneHref(string phone) {
            var normalized = new StringBuilder();
            foreach (var character in phone ?? string.Empty) {
                if (char.IsDigit(character) || (character == '+' && normalized.Length == 0)) normalized.Append(character);
            }
            return "tel:" + normalized;
        }

        private static string EmailHref(string email, string businessName, string contactName) {
            return "mailto:" + email + "?subject=" + Uri.EscapeDataString("Your AutoDealer.dev dealer demo") +
                "&body=" + Uri.EscapeDataString("Hi " + contactName + ",\r\n\r\nThank you for reaching out about " + businessName + ". ");
        }

        private static bool FixedTimeEquals(byte[] left, byte[] right) {
            if (left == null || right == null || left.Length != right.Length) return false;
            var difference = 0;
            for (var index = 0; index < left.Length; index++) difference |= left[index] ^ right[index];
            return difference == 0;
        }
    }
}
