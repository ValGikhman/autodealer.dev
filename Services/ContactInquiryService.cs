using autodealer.dev.Models;
using System;
using System.Configuration;
using System.Text;
using System.Web;

namespace autodealer.dev.Services {
    public sealed class ContactInquiryService : IContactInquiryService {
        public void Send(ContactInquiryViewModel inquiry) {
            if (inquiry == null) throw new ArgumentNullException("inquiry");

            var recipient = ConfigurationManager.AppSettings["ContactInquiry:Recipient"];
            var recipientName = ConfigurationManager.AppSettings["ContactInquiry:RecipientName"];
            if (string.IsNullOrWhiteSpace(recipient)) recipient = ConfigurationManager.AppSettings["DemoRequest:Recipient"];
            if (string.IsNullOrWhiteSpace(recipientName)) recipientName = ConfigurationManager.AppSettings["DemoRequest:RecipientName"];

            var inquiryLabel = string.Equals(inquiry.InquiryType, "api", StringComparison.OrdinalIgnoreCase)
                ? "API & vehicle data"
                : "Dealer website";
            var contactName = (inquiry.FirstName + " " + inquiry.LastName).Trim();
            var replyHref = "mailto:" + inquiry.Email.Trim() + "?subject=" + Uri.EscapeDataString("Re: " + inquiryLabel + " inquiry for " + inquiry.BusinessName.Trim());
            var phoneHref = PhoneHref(inquiry.Phone);

            var actions = new StringBuilder();
            actions.Append("<a class=\"email-button\" href=\"").Append(AttributeEncode(replyHref)).Append("\">Reply to ")
                .Append(Encode(inquiry.FirstName)).Append(" &rarr;</a>");
            if (!string.IsNullOrWhiteSpace(phoneHref))
                actions.Append("<a class=\"email-button\" href=\"").Append(AttributeEncode(phoneHref)).Append("\">Call ")
                    .Append(Encode(inquiry.Phone)).Append(" &rarr;</a>");

            var rows = Row("Inquiry", inquiryLabel) + Row("Dealership / company", inquiry.BusinessName) +
                Row("Contact", contactName) + Row("Email", inquiry.Email) + Row("Phone", inquiry.Phone);
            var body = EmailTemplateRenderer.Render(EmailTemplateName.ContactInquiryNotification,
                new EmailTemplateValues()
                    .Add("INQUIRY_LABEL", inquiryLabel.ToUpperInvariant())
                    .Add("CONTACT_NAME", contactName)
                    .Add("BUSINESS_NAME", inquiry.BusinessName)
                    .Add("MESSAGE", inquiry.Message)
                    .AddHtml("DETAIL_ROWS", rows)
                    .AddHtml("ACTIONS", actions.ToString()));

            var subject = "New " + inquiryLabel + " inquiry — " + SafeSubject(inquiry.BusinessName);
            SmtpMailSender.Send(recipient, recipientName, subject, body, inquiry.Email, contactName);
        }

        private static string Row(string label, string value) {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return "<tr><td class=\"detail-label\">" + Encode(label) + "</td><td class=\"detail-value\">" + Encode(value) + "</td></tr>";
        }

        private static string PhoneHref(string phone) {
            if (string.IsNullOrWhiteSpace(phone)) return null;
            var normalized = new StringBuilder();
            foreach (var character in phone) {
                if (char.IsDigit(character) || (character == '+' && normalized.Length == 0)) normalized.Append(character);
            }
            return normalized.Length == 0 ? null : "tel:" + normalized;
        }

        private static string Encode(string value) { return HttpUtility.HtmlEncode(value ?? string.Empty); }
        private static string AttributeEncode(string value) { return HttpUtility.HtmlAttributeEncode(value ?? string.Empty); }
        private static string SafeSubject(string value) { return (value ?? "New contact").Replace("\r", " ").Replace("\n", " ").Trim(); }
    }
}
