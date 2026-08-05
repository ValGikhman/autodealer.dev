using System.ComponentModel.DataAnnotations;

namespace autodealer.dev.Models {
    public sealed class ContactInquiryViewModel {
        [Required, StringLength(40), AllowedValues("website", "api", ErrorMessage = "Please choose an inquiry type.")]
        [SanitizeInput(InputSanitizationKind.Identifier)]
        public string InquiryType { get; set; }

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
        [Display(Name = "Work email")]
        public string Email { get; set; }

        [Phone, StringLength(32), RegularExpression(@"^[0-9+().\- xX#]*$", ErrorMessage = "Please enter a valid phone number.")]
        [SanitizeInput(InputSanitizationKind.Phone)]
        public string Phone { get; set; }

        [Required, StringLength(3000, MinimumLength = 20)]
        [SanitizeInput(InputSanitizationKind.MultilineText)]
        [Display(Name = "How can we help?")]
        public string Message { get; set; }

        // Honeypot: real customers never see or complete this field.
        [StringLength(200)]
        public string CompanyWebsite { get; set; }
    }
}
