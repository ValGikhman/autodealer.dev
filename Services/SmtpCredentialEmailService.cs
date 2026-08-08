using System;
using System.Configuration;
using System.Diagnostics;
using System.Text;
using System.Web;

namespace autodealer.dev.Services {
    public sealed class SmtpCredentialEmailService : ICredentialEmailService {
        public bool SendVerification(long clientId, string firstName, string email, string verificationUrl) {
            try {
                var body = EmailTemplateRenderer.Render(EmailTemplateName.EmailVerification,
                    new EmailTemplateValues()
                        .Add("FIRST_NAME", firstName)
                        .AddAttribute("VERIFICATION_URL", verificationUrl));
                SmtpMailSender.SendForClient(clientId, email, firstName, "Confirm your AutoDealer.dev email", body, null, null);
                return true;
            }
            catch (Exception ex) {
                Trace.TraceError("Email verification SMTP delivery failed: {0}", ex);
                return false;
            }
        }

        public bool SendCredentials(long clientId, string businessName, string firstName, string lastName, string email, string phone, string clientNumber, string apiKey, string planCode, bool createdByAdmin) {
            try {
                var rows = new StringBuilder();
                AddRow(rows, "Client number", clientNumber);
                AddRow(rows, "Sign-in email", email);
                AddRow(rows, "Plan", planCode);
                var body = EmailTemplateRenderer.Render(EmailTemplateName.ApiCredentials,
                    new EmailTemplateValues()
                        .Add("FIRST_NAME", firstName)
                        .Add("API_KEY", apiKey)
                        .Add("PASSWORD_GUIDANCE", createdByAdmin
                            ? "Use the temporary password selected when this workspace was created. For security, passwords are never repeated by email; change it after your first sign-in."
                            : "Use the password you chose during registration. For security, passwords are never repeated by email.")
                        .AddAttribute("DOCUMENTATION_URL", SeoUrl.Absolute("documentation"))
                        .AddAttribute("LOGIN_URL", SeoUrl.Absolute("account/login"))
                        .AddHtml("DETAIL_ROWS", rows.ToString()));

                SmtpMailSender.SendForClient(clientId, email, firstName, "Your AutoDealer.dev API credentials", body, null, null);
                SendOwnerNotification(clientId, businessName, firstName, lastName, email, phone, clientNumber, planCode);
                return true;
            }
            catch (Exception ex) {
                Trace.TraceError("Credential SMTP delivery failed: {0}", ex);
                return false;
            }
        }

        private static void SendOwnerNotification(long clientId, string businessName, string firstName, string lastName, string email, string phone, string clientNumber, string planCode) {
            var recipient = ConfigurationManager.AppSettings["AccountNotification:Recipient"];
            if (string.IsNullOrWhiteSpace(recipient)) return;

            try {
                var rows = new StringBuilder();
                AddRow(rows, "Company", businessName);
                AddRow(rows, "Contact", ((firstName ?? string.Empty) + " " + (lastName ?? string.Empty)).Trim());
                AddRow(rows, "Email", email);
                AddRow(rows, "Phone", phone);
                AddRow(rows, "Client number", clientNumber);
                AddRow(rows, "Plan", planCode);
                AddRow(rows, "Created", DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'"));

                var body = EmailTemplateRenderer.Render(EmailTemplateName.NewApiAccount,
                    new EmailTemplateValues().AddHtml("DETAIL_ROWS", rows.ToString()));
                var recipientName = ConfigurationManager.AppSettings["AccountNotification:RecipientName"];
                var subject = "New API account — " + SafeSubject(businessName);
                SmtpMailSender.SendForClient(clientId, recipient, recipientName, subject, body, email, ((firstName ?? string.Empty) + " " + (lastName ?? string.Empty)).Trim());
            }
            catch (Exception ex) {
                Trace.TraceError("Account notification SMTP delivery failed: {0}", ex);
            }
        }

        private static void AddRow(StringBuilder output, string label, string value) {
            if (string.IsNullOrWhiteSpace(value)) return;
            output.Append("<tr><td class=\"detail-label\">").Append(Encode(label))
                .Append("</td><td class=\"detail-value\">").Append(Encode(value)).Append("</td></tr>");
        }

        private static string Encode(string value) { return HttpUtility.HtmlEncode(value ?? string.Empty); }
        private static string SafeSubject(string value) { return (value ?? "New customer").Replace("\r", " ").Replace("\n", " ").Trim(); }
    }
}
