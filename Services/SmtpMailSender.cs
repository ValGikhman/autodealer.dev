using System;
using System.Configuration;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;

namespace autodealer.dev.Services {
    internal static class SmtpMailSender {
        internal const string LogoToken = "{{AUTODEALER_LOGO}}";

        public static void Send(string recipientAddress, string recipientName, string subject, string htmlBody, string replyToAddress, string replyToName) {
            var host = ConfigurationManager.AppSettings["Smtp:Host"];
            var fromAddress = ConfigurationManager.AppSettings["Smtp:From"];
            var fromName = ConfigurationManager.AppSettings["Smtp:FromName"];
            var username = ConfigurationManager.AppSettings["Smtp:Username"];
            var password = ConfigurationManager.AppSettings["Smtp:Password"];

            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(fromAddress) || string.IsNullOrWhiteSpace(recipientAddress))
                throw new InvalidOperationException("SMTP host, sender, and recipient must be configured.");

            var logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content", "images", "autodealer-logo.png");
            var hasLogo = File.Exists(logoPath);
            var logoMarkup = hasLogo
                ? "<div style=\"margin:0 0 18px\"><img src=\"cid:autodealer-logo\" width=\"240\" alt=\"AutoDealer.dev\" style=\"display:block;width:240px;max-width:100%;height:auto;border:0\"></div>"
                : "<div style=\"margin:0 0 18px;color:#ffffff;font-size:20px;font-weight:700\">AutoDealer.dev</div>";
            var renderedBody = (htmlBody ?? string.Empty).Replace(LogoToken, logoMarkup);

            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            using (var message = new MailMessage())
            using (var smtp = new SmtpClient(host.Trim())) {
                message.From = new MailAddress(fromAddress.Trim(), string.IsNullOrWhiteSpace(fromName) ? "AutoDealer.dev" : fromName.Trim());
                message.To.Add(new MailAddress(recipientAddress.Trim(), recipientName ?? string.Empty));
                message.Subject = subject;
                message.SubjectEncoding = Encoding.UTF8;
                message.Body = renderedBody;
                message.BodyEncoding = Encoding.UTF8;
                message.IsBodyHtml = true;
                if (hasLogo) {
                    var htmlView = AlternateView.CreateAlternateViewFromString(renderedBody, Encoding.UTF8, MediaTypeNames.Text.Html);
                    var logo = new LinkedResource(logoPath, "image/png") {
                        ContentId = "autodealer-logo",
                        TransferEncoding = TransferEncoding.Base64
                    };
                    htmlView.LinkedResources.Add(logo);
                    message.AlternateViews.Add(htmlView);
                }
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
