using System;
using System.Configuration;
using System.Diagnostics;
using System.Net;
using System.Net.Mail;
using System.Web;

namespace autodealer.dev.Services {
    public sealed class SmtpCredentialEmailService : ICredentialEmailService {
        public bool Send(string firstName, string email, string clientNumber, string apiKey, string planCode) {
            var host = ConfigurationManager.AppSettings["Smtp:Host"];
            var from = ConfigurationManager.AppSettings["Smtp:From"];
            var fromName = ConfigurationManager.AppSettings["Smtp:FromName"];
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from)) return false;

            try {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var body = "<div style=\"font-family:Arial,sans-serif;max-width:620px;margin:auto;color:#172033\">" +
                    "<h1>Welcome to AutoDealer.dev</h1><p>Hi " + HttpUtility.HtmlEncode(firstName) + ", your dealer API workspace is ready.</p>" +
                    "<p><strong>Client:</strong> " + HttpUtility.HtmlEncode(clientNumber) + "<br><strong>Plan:</strong> " + HttpUtility.HtmlEncode(planCode) + "</p>" +
                    "<p>Your API key is shown once:</p><pre style=\"padding:16px;background:#edf2f8;border-radius:8px;overflow:auto\">" + HttpUtility.HtmlEncode(apiKey) + "</pre>" +
                    "<p>Send it as <code>Authorization: Bearer &lt;key&gt;</code>. Store it in a secret manager and rotate it if exposed.</p></div>";

                var displayName = string.IsNullOrWhiteSpace(fromName) ? "AutoDealer.dev" : fromName;
                var username = ConfigurationManager.AppSettings["Smtp:Username"];
                var password = ConfigurationManager.AppSettings["Smtp:Password"];
                const string subject = "Your AutoDealer.dev API credentials";
                if (IonosAspMailSender.TrySend(displayName, from, password, firstName, email, subject, body, from)) return true;

                using (var message = new MailMessage())
                using (var smtp = new SmtpClient(host)) {
                    message.From = new MailAddress(from, displayName);
                    message.To.Add(email);
                    message.Subject = subject;
                    message.Body = body;
                    message.IsBodyHtml = true;
                    int port;
                    if (int.TryParse(ConfigurationManager.AppSettings["Smtp:Port"], out port)) smtp.Port = port;
                    smtp.EnableSsl = !string.Equals(ConfigurationManager.AppSettings["Smtp:EnableSsl"], "false", StringComparison.OrdinalIgnoreCase);
                    smtp.DeliveryMethod = SmtpDeliveryMethod.Network;
                    smtp.UseDefaultCredentials = false;
                    smtp.Timeout = 30000;
                    if (!string.IsNullOrWhiteSpace(username)) smtp.Credentials = new NetworkCredential(username, password);
                    smtp.Send(message);
                    return true;
                }
            }
            catch (Exception ex) {
                Trace.TraceError("Credential SMTP delivery failed: {0}", ex);
                return false;
            }
        }
    }
}
