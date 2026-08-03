using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace autodealer.dev.Models {
    public sealed class AdminLoginViewModel {
        [Required]
        [Display(Name = "User ID")]
        public string UserId { get; set; }

        [Required, DataType(DataType.Password)]
        public string Password { get; set; }

        [Display(Name = "Keep me signed in")]
        public bool RememberMe { get; set; }

        public string ReturnUrl { get; set; }
    }

    public sealed class AdminDashboardViewModel {
        public int TotalCustomers { get; set; }
        public int ActiveCustomers { get; set; }
        public int TrialingSubscriptions { get; set; }
        public int ActiveApiKeys { get; set; }
        public IReadOnlyList<AdminCustomerViewModel> Customers { get; set; }
    }

    public sealed class AdminCustomerViewModel {
        public long ClientId { get; set; }
        public string ClientNumber { get; set; }
        public string BusinessName { get; set; }
        public string ContactName { get; set; }
        public string Email { get; set; }
        public string PlanName { get; set; }
        public string SubscriptionStatus { get; set; }
        public DateTime? PeriodEndUtc { get; set; }
        public int ActiveApiKeyCount { get; set; }
        public DateTime CreatedUtc { get; set; }
    }
}
