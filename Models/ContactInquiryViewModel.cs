using System.ComponentModel.DataAnnotations;

namespace autodealer.dev.Models {
    public sealed class ContactInquiryViewModel {
        [Required, StringLength(40)]
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
        [Display(Name = "Work email")]
        public string Email { get; set; }

        [Phone, StringLength(32)]
        public string Phone { get; set; }

        [Required, StringLength(3000, MinimumLength = 20)]
        [Display(Name = "How can we help?")]
        public string Message { get; set; }

        // Honeypot: real customers never see or complete this field.
        public string CompanyWebsite { get; set; }
    }
}
