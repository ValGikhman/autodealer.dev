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
            actions.Append("<a href=\"").Append(AttributeEncode(replyHref)).Append("\" style=\"display:inline-block;margin:0 8px 8px 0;padding:12px 18px;border:1px solid #727980;border-radius:8px;color:#fff;background:linear-gradient(135deg,#555d65,#353b41);font-size:14px;font-weight:600;text-decoration:none\">Reply to ")
                .Append(Encode(inquiry.FirstName)).Append(" &rarr;</a>");
            if (!string.IsNullOrWhiteSpace(phoneHref))
                actions.Append("<a href=\"").Append(AttributeEncode(phoneHref)).Append("\" style=\"display:inline-block;margin:0 0 8px;padding:12px 18px;border:1px solid #727980;border-radius:8px;color:#fff;background:linear-gradient(135deg,#555d65,#353b41);font-size:14px;font-weight:600;text-decoration:none\">Call ")
                    .Append(Encode(inquiry.Phone)).Append(" &rarr;</a>");

            var body = "<!doctype html><html><body style=\"margin:0;padding:0;background:#eef0f2;font-family:Arial,sans-serif;color:#202327\">" +
                "<table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" style=\"background:#eef0f2;padding:32px 14px\"><tr><td align=\"center\">" +
                "<table role=\"presentation\" width=\"640\" cellspacing=\"0\" cellpadding=\"0\" style=\"max-width:640px;width:100%;overflow:hidden;border:1px solid #d5d9dd;border-radius:16px;background:#fff;box-shadow:0 18px 48px rgba(20,24,28,.12)\">" +
                "<tr><td style=\"padding:32px 38px;background:linear-gradient(135deg,#111315,#353a40);color:#fff\">" + SmtpMailSender.LogoToken + "<div style=\"font-size:12px;letter-spacing:1.5px;color:#c9cdd2\">NEW " + Encode(inquiryLabel.ToUpperInvariant()) + " INQUIRY</div><h1 style=\"margin:12px 0 8px;font-size:28px;font-weight:500\">A new conversation is waiting.</h1><p style=\"margin:0;color:#c8cdd2;font-size:15px;line-height:1.65\">" + Encode(contactName) + " from " + Encode(inquiry.BusinessName) + " reached out through the Contact page.</p></td></tr>" +
                "<tr><td style=\"padding:30px 38px\"><table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\">" +
                Row("Inquiry", inquiryLabel) + Row("Dealership / company", inquiry.BusinessName) + Row("Contact", contactName) + Row("Email", inquiry.Email) + Row("Phone", inquiry.Phone) +
                "</table><div style=\"margin-top:24px;padding:21px;border-left:3px solid #555d65;border-radius:8px;background:#f1f3f5\"><div style=\"margin-bottom:8px;font-size:12px;letter-spacing:1px;color:#687078\">THEIR MESSAGE</div><div style=\"font-size:15px;line-height:1.75;color:#30343a;white-space:pre-line\">" + Encode(inquiry.Message) + "</div></div>" +
                "<div style=\"margin-top:24px;padding:20px;border-radius:10px;background:#25292d;color:#fff\"><p style=\"margin:0 0 14px;color:#c8cdd2;font-size:14px;line-height:1.6\">The door is open. Continue the conversation while the idea is still warm.</p>" + actions + "</div>" +
                "</td></tr></table></td></tr></table></body></html>";

            var subject = "New " + inquiryLabel + " inquiry — " + SafeSubject(inquiry.BusinessName);
            SmtpMailSender.Send(recipient, recipientName, subject, body, inquiry.Email, contactName);
        }

        private static string Row(string label, string value) {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return "<tr><td style=\"width:155px;padding:10px 12px 10px 0;border-bottom:1px solid #eceff1;color:#747b82;font-size:13px\">" + Encode(label) + "</td><td style=\"padding:10px 0;border-bottom:1px solid #eceff1;color:#25292d;font-size:14px\">" + Encode(value) + "</td></tr>";
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
