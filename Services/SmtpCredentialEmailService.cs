using System;
using System.Configuration;
using System.Net;
using System.Net.Mail;
using System.Web;

namespace autodealer.dev.Services {
    public sealed class SmtpCredentialEmailService : ICredentialEmailService {
        public bool Send(string firstName, string email, string clientNumber, string apiKey, string planCode) {
            var host = ConfigurationManager.AppSettings["Smtp:Host"];
            var from = ConfigurationManager.AppSettings["Smtp:From"];
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from)) return false;

            try {
                var body = "<div style=\"font-family:Arial,sans-serif;max-width:620px;margin:auto;color:#172033\">" +
                    "<h1>Welcome to AutoDealer.dev</h1><p>Hi " + HttpUtility.HtmlEncode(firstName) + ", your dealer API workspace is ready.</p>" +
                    "<p><strong>Client:</strong> " + HttpUtility.HtmlEncode(clientNumber) + "<br><strong>Plan:</strong> " + HttpUtility.HtmlEncode(planCode) + "</p>" +
                    "<p>Your API key is shown once:</p><pre style=\"padding:16px;background:#edf2f8;border-radius:8px;overflow:auto\">" + HttpUtility.HtmlEncode(apiKey) + "</pre>" +
                    "<p>Send it as <code>Authorization: Bearer &lt;key&gt;</code>. Store it in a secret manager and rotate it if exposed.</p></div>";

                using (var message = new MailMessage(from, email))
                using (var smtp = new SmtpClient(host)) {
                    message.Subject = "Your AutoDealer.dev API credentials";
                    message.Body = body;
                    message.IsBodyHtml = true;
                    int port;
                    if (int.TryParse(ConfigurationManager.AppSettings["Smtp:Port"], out port)) smtp.Port = port;
                    smtp.EnableSsl = !string.Equals(ConfigurationManager.AppSettings["Smtp:EnableSsl"], "false", StringComparison.OrdinalIgnoreCase);
                    var user = ConfigurationManager.AppSettings["Smtp:Username"];
                    if (!string.IsNullOrWhiteSpace(user)) smtp.Credentials = new NetworkCredential(user, ConfigurationManager.AppSettings["Smtp:Password"]);
                    smtp.Send(message);
                    return true;
                }
            }
            catch (Exception) { return false; }
        }
    }
}
