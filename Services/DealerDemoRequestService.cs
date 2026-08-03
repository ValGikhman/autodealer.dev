using autodealer.dev.Models;
using System;
using System.Configuration;
using System.Diagnostics;
using System.Net;
using System.Net.Mail;
using System.Text;
using System.Web;

namespace autodealer.dev.Services {
    public sealed class DealerDemoRequestService : IDealerDemoRequestService {
        public bool Send(DealerDemoRequestViewModel request) {
            if (request == null) throw new ArgumentNullException("request");

            var host = ConfigurationManager.AppSettings["Smtp:Host"];
            var from = ConfigurationManager.AppSettings["Smtp:From"];
            var fromName = ConfigurationManager.AppSettings["Smtp:FromName"];
            var recipient = ConfigurationManager.AppSettings["DemoRequest:Recipient"];
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(recipient))
                return false;

            try {
                var displayName = string.IsNullOrWhiteSpace(fromName) ? "AutoDealer.dev" : fromName;
                var username = ConfigurationManager.AppSettings["Smtp:Username"];
                var password = ConfigurationManager.AppSettings["Smtp:Password"];
                var subject = "Dealer demo request — " + SafeSubject(request.BusinessName);
                var body = BuildBody(request);
                if (IonosAspMailSender.TrySend(displayName, from, password, "Valentin Gikhman", recipient, subject, body, request.Email))
                    return true;

                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                using (var message = new MailMessage())
                using (var smtp = new SmtpClient(host)) {
                    message.From = new MailAddress(from, displayName);
                    message.To.Add(recipient);
                    message.Subject = subject;
                    message.Body = body;
                    message.IsBodyHtml = true;
                    message.ReplyToList.Add(new MailAddress(request.Email, request.ContactName));

                    int port;
                    if (int.TryParse(ConfigurationManager.AppSettings["Smtp:Port"], out port)) smtp.Port = port;
                    smtp.EnableSsl = !string.Equals(ConfigurationManager.AppSettings["Smtp:EnableSsl"], "false", StringComparison.OrdinalIgnoreCase);
                    smtp.DeliveryMethod = SmtpDeliveryMethod.Network;
                    smtp.UseDefaultCredentials = false;
                    smtp.Timeout = 30000;
                    if (!string.IsNullOrWhiteSpace(username))
                        smtp.Credentials = new NetworkCredential(username, password);
                    smtp.Send(message);
                    return true;
                }
            }
            catch (Exception ex) {
                Trace.TraceError("Dealer demo SMTP delivery failed: {0}", ex);
                throw new InvalidOperationException("IONOS SMTP delivery failed.", ex);
            }
        }

        private static string BuildBody(DealerDemoRequestViewModel request) {
            var rows = new StringBuilder();
            AddRow(rows, "Contact", request.ContactName);
            AddRow(rows, "Email", request.Email);
            AddRow(rows, "Phone", request.Phone);
            AddRow(rows, "Current website", request.CurrentWebsite);
            AddRow(rows, "Dealer locations", request.LocationCount.HasValue ? request.LocationCount.Value.ToString() : null);
            AddRow(rows, "Inventory size", request.InventorySize);
            AddRow(rows, "Primary goal", request.PrimaryGoal);
            AddRow(rows, "Preferred contact", request.PreferredContact);

            return "<!doctype html><html><body style=\"margin:0;padding:0;background:#eef0f2;font-family:Arial,sans-serif;color:#202327\">" +
                "<table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" style=\"background:#eef0f2;padding:32px 14px\"><tr><td align=\"center\">" +
                "<table role=\"presentation\" width=\"640\" cellspacing=\"0\" cellpadding=\"0\" style=\"max-width:640px;width:100%;overflow:hidden;border:1px solid #d5d9dd;border-radius:16px;background:#ffffff;box-shadow:0 18px 48px rgba(20,24,28,.12)\">" +
                "<tr><td style=\"padding:34px 38px;background:linear-gradient(135deg,#111315,#353a40);color:#ffffff\"><div style=\"font-size:11px;letter-spacing:1.6px;color:#c9cdd2\">AUTODEALER.DEV / NEW OPPORTUNITY</div><h1 style=\"margin:12px 0 8px;font-size:28px;font-weight:500\">Dealer demo requested</h1><p style=\"margin:0;color:#c8cdd2;line-height:1.6\">" + Encode(request.BusinessName) + " would like to discuss a modern dealer experience.</p></td></tr>" +
                "<tr><td style=\"padding:30px 38px\"><h2 style=\"margin:0 0 18px;font-size:18px;font-weight:500\">Opportunity details</h2><table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\">" + rows + "</table>" +
                "<div style=\"margin-top:26px;padding:22px;border-left:3px solid #555d65;border-radius:8px;background:#f1f3f5\"><div style=\"margin-bottom:8px;font-size:11px;letter-spacing:1px;color:#687078\">WHAT THEY WANT TO IMPROVE</div><div style=\"font-size:15px;line-height:1.7;color:#30343a;white-space:pre-line\">" + Encode(request.Message) + "</div></div>" +
                "<p style=\"margin:26px 0 0;font-size:12px;color:#747b82\">Reply directly to this email to contact " + Encode(request.ContactName) + ".</p></td></tr></table></td></tr></table></body></html>";
        }

        private static void AddRow(StringBuilder output, string label, string value) {
            if (string.IsNullOrWhiteSpace(value)) return;
            output.Append("<tr><td style=\"width:155px;padding:10px 12px 10px 0;border-bottom:1px solid #eceff1;color:#747b82;font-size:12px\">")
                .Append(Encode(label)).Append("</td><td style=\"padding:10px 0;border-bottom:1px solid #eceff1;color:#25292d;font-size:14px\">")
                .Append(Encode(value)).Append("</td></tr>");
        }

        private static string Encode(string value) { return HttpUtility.HtmlEncode(value ?? string.Empty); }

        private static string SafeSubject(string value) {
            return (value ?? "New dealership").Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}
