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
        public int ApiKeyCount { get; set; }
        public int SubscriptionCount { get; set; }
        public int EmailCount { get; set; }
        public DateTime CreatedUtc { get; set; }
    }

    public sealed class AdminCustomerAccountDetailViewModel {
        public IReadOnlyList<AdminApiKeyViewModel> ApiKeys { get; set; }
        public IReadOnlyList<AdminSubscriptionViewModel> Subscriptions { get; set; }
    }

    public sealed class AdminApiKeyViewModel {
        public long ApiKeyId { get; set; }
        public string Name { get; set; }
        public string KeyPrefix { get; set; }
        public string Scopes { get; set; }
        public string Status { get; set; }
        public DateTime CreatedUtc { get; set; }
        public DateTime? LastUsedUtc { get; set; }
        public DateTime? ExpiresUtc { get; set; }
        public DateTime? RevokedUtc { get; set; }
    }

    public sealed class AdminSubscriptionViewModel {
        public long SubscriptionId { get; set; }
        public string PlanName { get; set; }
        public string PlanCode { get; set; }
        public string Status { get; set; }
        public int MonthlyRequestQuota { get; set; }
        public short MaxApiKeys { get; set; }
        public DateTime CurrentPeriodStartUtc { get; set; }
        public DateTime CurrentPeriodEndUtc { get; set; }
        public bool CancelAtPeriodEnd { get; set; }
        public string ProviderSubscriptionId { get; set; }
        public DateTime CreatedUtc { get; set; }
    }

    public sealed class AdminEditOptionViewModel {
        public string Value { get; set; }
        public string Text { get; set; }
    }

    public sealed class AdminClientEditViewModel {
        public long ClientId { get; set; }
        public string ClientNumber { get; set; }

        [Required, StringLength(160)]
        public string BusinessName { get; set; }

        [Required, StringLength(80)]
        public string FirstName { get; set; }

        [Required, StringLength(80)]
        public string LastName { get; set; }

        [Required, EmailAddress, StringLength(254)]
        public string Email { get; set; }

        [Phone, StringLength(32), RegularExpression(@"^[0-9+().\- xX#]*$", ErrorMessage = "Please enter a valid phone number.")]
        [SanitizeInput(InputSanitizationKind.Phone)]
        public string Phone { get; set; }

        [Required, StringLength(20)]
        public string Status { get; set; }

        public DateTime? EmailVerifiedUtc { get; set; }
        public DateTime CreatedUtc { get; set; }
    }

    public sealed class AdminClientCreateViewModel {
        [Required, StringLength(32), RegularExpression(@"^DLR-[0-9]{6}-[A-Z0-9_-]{6}$")]
        public string ClientNumber { get; set; }

        [Required, StringLength(160)]
        public string BusinessName { get; set; }

        [Required, StringLength(80)]
        public string FirstName { get; set; }

        [Required, StringLength(80)]
        public string LastName { get; set; }

        [Required, EmailAddress, StringLength(254)]
        [SanitizeInput(InputSanitizationKind.Email)]
        public string Email { get; set; }

        [Phone, StringLength(32), RegularExpression(@"^[0-9+().\- xX#]*$", ErrorMessage = "Please enter a valid phone number.")]
        [SanitizeInput(InputSanitizationKind.Phone)]
        public string Phone { get; set; }

        [Required, StringLength(PasswordPolicy.MaximumLength, MinimumLength = PasswordPolicy.MinimumLength), PasswordPolicy]
        [PreserveInput]
        public string TemporaryPassword { get; set; }

        [Required, Compare("TemporaryPassword", ErrorMessage = "The password confirmation does not match.")]
        [PreserveInput]
        public string ConfirmTemporaryPassword { get; set; }

        [Required, StringLength(32), RegularExpression(@"^[A-Za-z0-9_-]+$")]
        public string PlanCode { get; set; }

        public IReadOnlyList<AdminEditOptionViewModel> PlanOptions { get; set; }
    }

    public sealed class AdminApiKeyEditViewModel {
        public long ApiKeyId { get; set; }
        public long ClientId { get; set; }

        [Required, StringLength(80)]
        public string Name { get; set; }

        [Required, StringLength(500)]
        public string Scopes { get; set; }

        [Required, StringLength(20)]
        public string Status { get; set; }

        public long SubscriptionId { get; set; }
        public DateTime? ExpiresUtc { get; set; }
        public string KeyPrefix { get; set; }
        public IReadOnlyList<AdminEditOptionViewModel> SubscriptionOptions { get; set; }
    }

    public sealed class AdminSubscriptionEditViewModel {
        public long SubscriptionId { get; set; }
        public long ClientId { get; set; }
        public int PlanId { get; set; }

        [Required, StringLength(20)]
        public string Status { get; set; }

        public DateTime CurrentPeriodStartUtc { get; set; }
        public DateTime CurrentPeriodEndUtc { get; set; }
        public bool CancelAtPeriodEnd { get; set; }

        [StringLength(200)]
        public string ProviderSubscriptionId { get; set; }

        public IReadOnlyList<AdminEditOptionViewModel> PlanOptions { get; set; }
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
