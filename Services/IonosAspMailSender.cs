using System;
using System.Runtime.InteropServices;
using System.Web;

namespace autodealer.dev.Services {
    internal static class IonosAspMailSender {
        public static bool TrySend(string fromName, string fromAddress, string password, string recipientName, string recipientAddress, string subject, string htmlBody, string replyTo) {
            var server = HttpContext.Current == null ? null : HttpContext.Current.Server;
            if (server == null) return false;

            object instance = null;
            try {
                try {
                    instance = server.CreateObject("SMTPsvg.Mailer");
                }
                catch (Exception ex) when (ex is COMException || ex is HttpException) {
                    return false;
                }

                dynamic mailer = instance;
                mailer.FromName = fromName;
                mailer.FromAddress = fromAddress;
                mailer.Username = fromAddress;
                mailer.Password = password;
                mailer.RemoteHost = "mrelay.perfora.net";
                mailer.Port = 465;
                if (!string.IsNullOrWhiteSpace(replyTo)) mailer.ReplyTo = replyTo;

                if (!(bool)mailer.AddRecipient(recipientName, recipientAddress))
                    throw new InvalidOperationException("IONOS ASP Mail rejected the recipient address.");

                mailer.Subject = subject;
                mailer.ContentType = "text/html";
                mailer.BodyText = htmlBody;
                if (!(bool)mailer.SendMail)
                    throw new InvalidOperationException("IONOS ASP Mail error: " + Convert.ToString(mailer.Response));
                return true;
            }
            finally {
                if (instance != null && Marshal.IsComObject(instance)) Marshal.FinalReleaseComObject(instance);
            }
        }
    }
}
