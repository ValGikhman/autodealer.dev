using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace autodealer.dev.Models {
    public sealed class DealerDemoRequestViewModel : IValidatableObject {
        [Required, StringLength(160)]
        [Display(Name = "Dealership / company")]
        public string BusinessName { get; set; }

        [Required, StringLength(160)]
        [Display(Name = "Your name")]
        public string ContactName { get; set; }

        [Required, EmailAddress, StringLength(254)]
        [SanitizeInput(InputSanitizationKind.Email)]
        [Display(Name = "Work email")]
        public string Email { get; set; }

        [Phone, StringLength(32), RegularExpression(@"^[0-9+().\- xX#]*$", ErrorMessage = "Please enter a valid phone number.")]
        [SanitizeInput(InputSanitizationKind.Phone)]
        public string Phone { get; set; }

        [StringLength(300), SafeHttpUrl(ErrorMessage = "Please enter a valid HTTP or HTTPS website address.")]
        [SanitizeInput(InputSanitizationKind.Url)]
        [Display(Name = "Current website")]
        public string CurrentWebsite { get; set; }

        [Range(1, 1000)]
        [Display(Name = "Dealer locations")]
        public int? LocationCount { get; set; }

        [Required, StringLength(80), AllowedValues("Under 50 vehicles", "50-149 vehicles", "150-499 vehicles", "500+ vehicles", ErrorMessage = "Please choose an inventory size.")]
        [Display(Name = "Inventory size")]
        public string InventorySize { get; set; }

        [Required, StringLength(120), AllowedValues("Replace our dealer website", "Improve inventory merchandising", "Generate more qualified leads", "Connect vehicle data by API", "Support multiple locations", "Explore the complete platform", ErrorMessage = "Please choose a primary goal.")]
        [Display(Name = "Primary goal")]
        public string PrimaryGoal { get; set; }

        [Required, StringLength(3000, MinimumLength = 20)]
        [SanitizeInput(InputSanitizationKind.MultilineText)]
        [Display(Name = "What would you like to improve?")]
        public string Message { get; set; }

        [Required, StringLength(20), AllowedValues("Email", "Phone", ErrorMessage = "Please choose a preferred contact method.")]
        [SanitizeInput(InputSanitizationKind.Identifier)]
        [Display(Name = "Preferred contact")]
        public string PreferredContact { get; set; }

        // Honeypot: real users never see or complete this field.
        [StringLength(200)]
        public string CompanyWebsite { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) {
            if (string.Equals(PreferredContact, "Phone", System.StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(Phone))
                yield return new ValidationResult("Please enter a phone number when phone is your preferred contact method.", new[] { "Phone" });
        }
    }
}
