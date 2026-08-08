using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Web.Mvc;

namespace autodealer.dev.Models {
    [Bind(Exclude = "PlanOptions")]
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
        [SanitizeInput(InputSanitizationKind.Email)]
        public string Email { get; set; }

        [Phone, StringLength(32), RegularExpression(@"^[0-9+().\- xX#]*$", ErrorMessage = "Please enter a valid phone number.")]
        [SanitizeInput(InputSanitizationKind.Phone)]
        public string Phone { get; set; }

        [Required, StringLength(PasswordPolicy.MaximumLength, MinimumLength = PasswordPolicy.MinimumLength), PasswordPolicy]
        [DataType(DataType.Password)]
        [PreserveInput]
        public string Password { get; set; }

        [Required, DataType(DataType.Password), System.ComponentModel.DataAnnotations.Compare("Password", ErrorMessage = "The password confirmation does not match.")]
        [PreserveInput]
        [Display(Name = "Confirm password")]
        public string ConfirmPassword { get; set; }

        [Required, StringLength(32), RegularExpression(@"^[A-Za-z0-9_-]+$", ErrorMessage = "Please choose a valid plan.")]
        [SanitizeInput(InputSanitizationKind.Identifier)]
        [Display(Name = "Plan")]
        public string PlanCode { get; set; }

        public IList<SelectListItem> PlanOptions { get; set; }

        // Set only by a PCI-compliant payment provider's hosted fields/checkout.
        // Raw card number and CVV must never be posted to this application.
        [StringLength(300), RegularExpression(@"^[A-Za-z0-9._~:+/=\-]*$", ErrorMessage = "The payment token is invalid.")]
        [SanitizeInput(InputSanitizationKind.Token)]
        public string PaymentMethodToken { get; set; }

        [MustBeTrue(ErrorMessage = "Please accept the service terms.")]
        public bool AcceptTerms { get; set; }
    }

    public class AccountCreatedViewModel {
        public string ClientNumber { get; set; }
        public string Email { get; set; }
        public bool VerificationEmailSent { get; set; }
    }

    public enum EmailVerificationStatus {
        Verified,
        AlreadyVerified,
        Invalid,
        Expired,
        DeliveryFailed
    }

    public class EmailVerificationViewModel {
        public EmailVerificationStatus Status { get; set; }
        public string Email { get; set; }
        public string RetryUrl { get; set; }
        public bool IsVerified {
            get { return Status == EmailVerificationStatus.Verified || Status == EmailVerificationStatus.AlreadyVerified; }
        }
    }

    public class AccountLoginViewModel {
        [Required, EmailAddress, StringLength(254)]
        [SanitizeInput(InputSanitizationKind.Email)]
        public string Email { get; set; }

        [Required, StringLength(256), DataType(DataType.Password)]
        [PreserveInput]
        public string Password { get; set; }

        [Display(Name = "Keep me signed in")]
        public bool RememberMe { get; set; }

        [StringLength(2048)]
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
        public bool PaymentRequired { get; set; }
        public string PaymentUrl { get; set; }
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
