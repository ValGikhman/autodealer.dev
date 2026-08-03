using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Web.Mvc;

namespace autodealer.dev.Models {
    public class AccountRegistrationViewModel {
        public AccountRegistrationViewModel() {
            PlanOptions = new List<SelectListItem>();
        }

        [Required, StringLength(160)]
        [Display(Name = "Dealership / company")]
        public string BusinessName { get; set; }

        [Required, StringLength(80)]
        [Display(Name = "First name")]
        public string FirstName { get; set; }

        [Required, StringLength(80)]
        [Display(Name = "Last name")]
        public string LastName { get; set; }

        [Required, EmailAddress, StringLength(254)]
        public string Email { get; set; }

        [Phone, StringLength(32)]
        public string Phone { get; set; }

        [Required, StringLength(100, MinimumLength = 12)]
        [DataType(DataType.Password)]
        public string Password { get; set; }

        [Required]
        [Display(Name = "Plan")]
        public string PlanCode { get; set; }

        public IList<SelectListItem> PlanOptions { get; set; }

        // Set only by a PCI-compliant payment provider's hosted fields/checkout.
        // Raw card number and CVV must never be posted to this application.
        public string PaymentMethodToken { get; set; }

        [MustBeTrue(ErrorMessage = "Please accept the service terms.")]
        public bool AcceptTerms { get; set; }
    }

    public class AccountCreatedViewModel {
        public string ClientNumber { get; set; }
        public string ApiKey { get; set; }
        public string Email { get; set; }
        public bool CredentialsEmailed { get; set; }
    }

    public class AccountLoginViewModel {
        [Required, EmailAddress, StringLength(254)]
        public string Email { get; set; }

        [Required, DataType(DataType.Password)]
        public string Password { get; set; }

        [Display(Name = "Keep me signed in")]
        public bool RememberMe { get; set; }

        public string ReturnUrl { get; set; }
    }

    public class AccountDashboardViewModel {
        public string ClientNumber { get; set; }
        public string BusinessName { get; set; }
        public string ContactName { get; set; }
        public string Email { get; set; }
        public string PlanName { get; set; }
        public string SubscriptionStatus { get; set; }
        public int MonthlyRequestQuota { get; set; }
        public DateTime? CurrentPeriodEndUtc { get; set; }
        public int ActiveApiKeyCount { get; set; }
    }

    public sealed class MustBeTrueAttribute : ValidationAttribute, IClientValidatable {
        public override bool IsValid(object value) {
            return value is bool && (bool)value;
        }

        public IEnumerable<ModelClientValidationRule> GetClientValidationRules(ModelMetadata metadata, ControllerContext context) {
            yield return new ModelClientValidationRule {
                ErrorMessage = FormatErrorMessage(metadata.GetDisplayName()),
                ValidationType = "mustbetrue"
            };
        }
    }
}
