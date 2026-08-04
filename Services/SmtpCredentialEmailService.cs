using System;
using System.Configuration;
using System.Diagnostics;
using System.Text;
using System.Web;

namespace autodealer.dev.Services {
    public sealed class SmtpCredentialEmailService : ICredentialEmailService {
        public bool Send(string businessName, string firstName, string lastName, string email, string phone, string clientNumber, string apiKey, string planCode) {
            var credentialsEmailed = SendCredentials(firstName, email, clientNumber, apiKey, planCode);
            SendOwnerNotification(businessName, firstName, lastName, email, phone, clientNumber, planCode);
            return credentialsEmailed;
        }

        private static bool SendCredentials(string firstName, string email, string clientNumber, string apiKey, string planCode) {
            try {
                var body = "<div style=\"font-family:Arial,sans-serif;max-width:620px;margin:auto;color:#172033\">" +
                    "<h1>Welcome to AutoDealer.dev</h1><p>Hi " + Encode(firstName) + ", your dealer API workspace is ready.</p>" +
                    "<p><strong>Client:</strong> " + Encode(clientNumber) + "<br><strong>Plan:</strong> " + Encode(planCode) + "</p>" +
                    "<p>Your API key is shown once:</p><pre style=\"padding:16px;background:#edf2f8;border-radius:8px;overflow:auto\">" + Encode(apiKey) + "</pre>" +
                    "<p>Send it as <code>Authorization: Bearer &lt;key&gt;</code>. Store it in a secret manager and rotate it if exposed.</p></div>";

                SmtpMailSender.Send(email, firstName, "Your AutoDealer.dev API credentials", body, null, null);
                return true;
            }
            catch (Exception ex) {
                Trace.TraceError("Credential SMTP delivery failed: {0}", ex);
                return false;
            }
        }

        private static void SendOwnerNotification(string businessName, string firstName, string lastName, string email, string phone, string clientNumber, string planCode) {
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

                var body = "<!doctype html><html><body style=\"margin:0;padding:0;background:#eef0f2;font-family:Arial,sans-serif;color:#202327\">" +
                    "<table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" style=\"background:#eef0f2;padding:32px 14px\"><tr><td align=\"center\">" +
                    "<table role=\"presentation\" width=\"640\" cellspacing=\"0\" cellpadding=\"0\" style=\"max-width:640px;width:100%;overflow:hidden;border:1px solid #d5d9dd;border-radius:16px;background:#ffffff;box-shadow:0 18px 48px rgba(20,24,28,.12)\">" +
                    "<tr><td style=\"padding:34px 38px;background:linear-gradient(135deg,#111315,#353a40);color:#ffffff\"><div style=\"font-size:12px;letter-spacing:1.5px;color:#c9cdd2\">AUTODEALER.DEV / NEW API ACCOUNT</div><h1 style=\"margin:12px 0 8px;font-size:28px;font-weight:500\">API key issued</h1><p style=\"margin:0;color:#c8cdd2;font-size:15px;line-height:1.6\">A new customer account was saved and its primary API key is active.</p></td></tr>" +
                    "<tr><td style=\"padding:30px 38px\"><h2 style=\"margin:0 0 18px;font-size:19px;font-weight:500\">Account details</h2><table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\">" + rows + "</table>" +
                    "<div style=\"margin-top:24px;padding:18px;border-left:3px solid #555d65;border-radius:8px;background:#f1f3f5;color:#4c535a;font-size:13px;line-height:1.6\">For security, this notification does not include the API secret. The customer received the credential separately.</div>" +
                    "</td></tr></table></td></tr></table></body></html>";

                var recipientName = ConfigurationManager.AppSettings["AccountNotification:RecipientName"];
                var subject = "New API account — " + SafeSubject(businessName);
                SmtpMailSender.Send(recipient, recipientName, subject, body, email, ((firstName ?? string.Empty) + " " + (lastName ?? string.Empty)).Trim());
            }
            catch (Exception ex) {
                Trace.TraceError("Account notification SMTP delivery failed: {0}", ex);
            }
        }

        private static void AddRow(StringBuilder output, string label, string value) {
            if (string.IsNullOrWhiteSpace(value)) return;
            output.Append("<tr><td style=\"width:155px;padding:10px 12px 10px 0;border-bottom:1px solid #eceff1;color:#747b82;font-size:13px\">")
                .Append(Encode(label)).Append("</td><td style=\"padding:10px 0;border-bottom:1px solid #eceff1;color:#25292d;font-size:14px\">")
                .Append(Encode(value)).Append("</td></tr>");
        }

        private static string Encode(string value) { return HttpUtility.HtmlEncode(value ?? string.Empty); }

        private static string SafeSubject(string value) {
            return (value ?? "New customer").Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}
