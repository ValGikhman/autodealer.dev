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
        [Display(Name = "Work email")]
        public string Email { get; set; }

        [Phone, StringLength(32)]
        public string Phone { get; set; }

        [StringLength(300)]
        [Display(Name = "Current website")]
        public string CurrentWebsite { get; set; }

        [Range(1, 1000)]
        [Display(Name = "Dealer locations")]
        public int? LocationCount { get; set; }

        [Required, StringLength(80)]
        [Display(Name = "Inventory size")]
        public string InventorySize { get; set; }

        [Required, StringLength(120)]
        [Display(Name = "Primary goal")]
        public string PrimaryGoal { get; set; }

        [Required, StringLength(3000, MinimumLength = 20)]
        [Display(Name = "What would you like to improve?")]
        public string Message { get; set; }

        [Required, StringLength(20)]
        [Display(Name = "Preferred contact")]
        public string PreferredContact { get; set; }

        // Honeypot: real users never see or complete this field.
        public string CompanyWebsite { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) {
            if (string.Equals(PreferredContact, "Phone", System.StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(Phone))
                yield return new ValidationResult("Please enter a phone number when phone is your preferred contact method.", new[] { "Phone" });
        }
    }
}
