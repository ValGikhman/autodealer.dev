using System;
using System.Configuration;
using System.Net;
using System.Net.Mail;
using System.Text;

namespace autodealer.dev.Services {
    internal static class SmtpMailSender {
        public static void Send(string recipientAddress, string recipientName, string subject, string htmlBody, string replyToAddress, string replyToName) {
            var host = ConfigurationManager.AppSettings["Smtp:Host"];
            var fromAddress = ConfigurationManager.AppSettings["Smtp:From"];
            var fromName = ConfigurationManager.AppSettings["Smtp:FromName"];
            var username = ConfigurationManager.AppSettings["Smtp:Username"];
            var password = ConfigurationManager.AppSettings["Smtp:Password"];

            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(fromAddress) || string.IsNullOrWhiteSpace(recipientAddress))
                throw new InvalidOperationException("SMTP host, sender, and recipient must be configured.");

            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var message = new MailMessage())
            using (var smtp = new SmtpClient(host.Trim())) {
                message.From = new MailAddress(fromAddress.Trim(), string.IsNullOrWhiteSpace(fromName) ? "AutoDealer.dev" : fromName.Trim());
                message.To.Add(new MailAddress(recipientAddress.Trim(), recipientName ?? string.Empty));
                message.Subject = subject;
                message.SubjectEncoding = Encoding.UTF8;
                message.Body = htmlBody;
                message.BodyEncoding = Encoding.UTF8;
                message.IsBodyHtml = true;
                if (!string.IsNullOrWhiteSpace(replyToAddress))
                    message.ReplyToList.Add(new MailAddress(replyToAddress.Trim(), replyToName ?? string.Empty));

                int port;
                smtp.Port = int.TryParse(ConfigurationManager.AppSettings["Smtp:Port"], out port) ? port : 587;
                smtp.EnableSsl = !string.Equals(ConfigurationManager.AppSettings["Smtp:EnableSsl"], "false", StringComparison.OrdinalIgnoreCase);
                smtp.DeliveryMethod = SmtpDeliveryMethod.Network;
                smtp.UseDefaultCredentials = false;
                smtp.Timeout = 30000;
                if (!string.IsNullOrWhiteSpace(username)) smtp.Credentials = new NetworkCredential(username.Trim(), password ?? string.Empty);
                smtp.Send(message);
            }
        }
    }
}
