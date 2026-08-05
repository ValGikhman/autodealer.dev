using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace autodealer.dev.Models {
    public sealed class AdminLoginViewModel {
        [Required, StringLength(100)]
        [Display(Name = "User ID")]
        public string UserId { get; set; }

        [Required, StringLength(256), DataType(DataType.Password)]
        [PreserveInput]
        public string Password { get; set; }

        [Display(Name = "Keep me signed in")]
        public bool RememberMe { get; set; }

        [StringLength(2048)]
        public string ReturnUrl { get; set; }
    }

    public sealed class AdminDashboardViewModel {
        public int TotalCustomers { get; set; }
        public int ActiveCustomers { get; set; }
        public int TrialingSubscriptions { get; set; }
        public int ActiveApiKeys { get; set; }
        public IReadOnlyList<AdminCustomerViewModel> Customers { get; set; }
        public IReadOnlyList<AdminDemoRequestViewModel> DemoRequests { get; set; }
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
        public int EmailCount { get; set; }
        public DateTime CreatedUtc { get; set; }
    }

    public sealed class AdminClientEmailViewModel {
        public long ClientEmailHistoryId { get; set; }
        public long ClientId { get; set; }
        public DateTime SentUtc { get; set; }
        public string ToEmail { get; set; }
        public string Subject { get; set; }
        public string HtmlBody { get; set; }
    }

    public sealed class AdminDemoRequestViewModel {
        public Guid RequestId { get; set; }
        public string BusinessName { get; set; }
        public string ContactName { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string CurrentWebsite { get; set; }
        public string WebsiteHref { get; set; }
        public int? LocationCount { get; set; }
        public string InventorySize { get; set; }
        public string PrimaryGoal { get; set; }
        public string PreferredContact { get; set; }
        public string Message { get; set; }
        public string Status { get; set; }
        public DateTime CreatedUtc { get; set; }
        public string ContactHref { get; set; }
        public string ContactAction { get; set; }
    }
}
