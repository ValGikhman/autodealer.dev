using autodealer.dev.Data;
using autodealer.dev.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Security.Cryptography;

namespace autodealer.dev.Services {
    public sealed class AdminService : IAdminService {
        private readonly string connectionString;

        public AdminService() {
            var setting = ConfigurationManager.ConnectionStrings["AutoDealer.dev"];
            connectionString = setting == null ? null : setting.ConnectionString;
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
                    Customers = customerRows
                };
            }
        }

        private static bool FixedTimeEquals(byte[] left, byte[] right) {
            if (left == null || right == null || left.Length != right.Length) return false;
            var difference = 0;
            for (var index = 0; index < left.Length; index++) difference |= left[index] ^ right[index];
            return difference == 0;
        }
    }
}
