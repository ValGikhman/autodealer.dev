using System;
using System.Diagnostics;
using System.Web;

namespace autodealer.dev.Services {
    public sealed class SmtpCredentialEmailService : ICredentialEmailService {
        public bool Send(string firstName, string email, string clientNumber, string apiKey, string planCode) {
            try {
                var body = "<div style=\"font-family:Arial,sans-serif;max-width:620px;margin:auto;color:#172033\">" +
                    "<h1>Welcome to AutoDealer.dev</h1><p>Hi " + HttpUtility.HtmlEncode(firstName) + ", your dealer API workspace is ready.</p>" +
                    "<p><strong>Client:</strong> " + HttpUtility.HtmlEncode(clientNumber) + "<br><strong>Plan:</strong> " + HttpUtility.HtmlEncode(planCode) + "</p>" +
                    "<p>Your API key is shown once:</p><pre style=\"padding:16px;background:#edf2f8;border-radius:8px;overflow:auto\">" + HttpUtility.HtmlEncode(apiKey) + "</pre>" +
                    "<p>Send it as <code>Authorization: Bearer &lt;key&gt;</code>. Store it in a secret manager and rotate it if exposed.</p></div>";

                const string subject = "Your AutoDealer.dev API credentials";
                SmtpMailSender.Send(email, firstName, subject, body, null, null);
                return true;
            }
            catch (Exception ex) {
                Trace.TraceError("Credential SMTP delivery failed: {0}", ex);
                return false;
            }
        }
    }
}
