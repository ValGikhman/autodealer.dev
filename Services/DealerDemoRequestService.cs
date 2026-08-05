using autodealer.dev.Models;
using System;
using System.Configuration;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Text;
using System.Web;

namespace autodealer.dev.Services {
    public sealed class DealerDemoRequestService : IDealerDemoRequestService {
        private readonly string connectionString;

        public DealerDemoRequestService() {
            connectionString = AutoDealerConnectionString.Resolve();
        }

        public bool Send(DealerDemoRequestViewModel request) {
            if (request == null) throw new ArgumentNullException("request");

            var requestId = SaveRequest(request);

            try {
                SendOwnerNotification(request, requestId);
                MarkDelivery(requestId, true);
            }
            catch (Exception ex) {
                Trace.TraceError("Dealer demo owner notification failed for {0}: {1}", requestId, ex);
                throw new InvalidOperationException("Notification delivery failed after the request was saved.", ex);
            }

            if (SendCustomerConfirmation(request, requestId)) MarkDelivery(requestId, false);
            return true;
        }

        private Guid SaveRequest(DealerDemoRequestViewModel request) {
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException("The AutoDealer.dev database connection is not configured.");

            var requestId = Guid.NewGuid();
            const string sql = @"INSERT dbo.DealerDemoRequests
                (RequestId,BusinessName,ContactName,Email,Phone,CurrentWebsite,LocationCount,InventorySize,PrimaryGoal,PreferredContact,Message,Status,CreatedUtc)
                VALUES
                (@RequestId,@BusinessName,@ContactName,@Email,@Phone,@CurrentWebsite,@LocationCount,@InventorySize,@PrimaryGoal,@PreferredContact,@Message,'new',SYSUTCDATETIME());";

            using (var connection = new SqlConnection(connectionString))
            using (var command = new SqlCommand(sql, connection)) {
                command.Parameters.Add("@RequestId", SqlDbType.UniqueIdentifier).Value = requestId;
                command.Parameters.Add("@BusinessName", SqlDbType.NVarChar, 160).Value = request.BusinessName.Trim();
                command.Parameters.Add("@ContactName", SqlDbType.NVarChar, 160).Value = request.ContactName.Trim();
                command.Parameters.Add("@Email", SqlDbType.NVarChar, 254).Value = request.Email.Trim().ToLowerInvariant();
                command.Parameters.Add("@Phone", SqlDbType.NVarChar, 32).Value = DbValue(request.Phone);
                command.Parameters.Add("@CurrentWebsite", SqlDbType.NVarChar, 300).Value = DbValue(request.CurrentWebsite);
                command.Parameters.Add("@LocationCount", SqlDbType.Int).Value = request.LocationCount.HasValue ? (object)request.LocationCount.Value : DBNull.Value;
                command.Parameters.Add("@InventorySize", SqlDbType.NVarChar, 80).Value = request.InventorySize.Trim();
                command.Parameters.Add("@PrimaryGoal", SqlDbType.NVarChar, 120).Value = request.PrimaryGoal.Trim();
                command.Parameters.Add("@PreferredContact", SqlDbType.VarChar, 20).Value = PreferredContact(request);
                command.Parameters.Add("@Message", SqlDbType.NVarChar, 3000).Value = request.Message.Trim();
                connection.Open();
                if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("The dealer demo request could not be saved.");
            }

            return requestId;
        }

        private static object DbValue(string value) {
            return string.IsNullOrWhiteSpace(value) ? (object)DBNull.Value : value.Trim();
        }

        private static string PreferredContact(DealerDemoRequestViewModel request) {
            return string.Equals(request.PreferredContact, "Phone", StringComparison.OrdinalIgnoreCase) ? "Phone" : "Email";
        }

        private void MarkDelivery(Guid requestId, bool ownerNotification) {
            var column = ownerNotification ? "OwnerNotificationSentUtc" : "CustomerConfirmationSentUtc";
            try {
                using (var connection = new SqlConnection(connectionString))
                using (var command = new SqlCommand("UPDATE dbo.DealerDemoRequests SET " + column + "=SYSUTCDATETIME() WHERE RequestId=@RequestId;", connection)) {
                    command.Parameters.Add("@RequestId", SqlDbType.UniqueIdentifier).Value = requestId;
                    connection.Open();
                    command.ExecuteNonQuery();
                }
            }
            catch (Exception ex) {
                Trace.TraceError("Dealer demo delivery status update failed for {0}: {1}", requestId, ex);
            }
        }

        private static void SendOwnerNotification(DealerDemoRequestViewModel request, Guid requestId) {
            var recipient = ConfigurationManager.AppSettings["DemoRequest:Recipient"];
            var recipientName = ConfigurationManager.AppSettings["DemoRequest:RecipientName"];
            var prefersPhone = string.Equals(PreferredContact(request), "Phone", StringComparison.Ordinal);
            var actionHref = prefersPhone ? PhoneHref(request.Phone) : EmailHref(request);
            var actionText = prefersPhone ? "Call " + request.ContactName : "Reply to " + request.ContactName;
            var actionNote = prefersPhone
                ? "They asked to begin by phone. Their number is ready below when you are."
                : "They asked to begin by email. One click opens a reply addressed and ready to write.";

            var rows = new StringBuilder();
            AddRow(rows, "Contact", request.ContactName);
            AddRow(rows, "Email", request.Email);
            AddRow(rows, "Phone", request.Phone);
            AddRow(rows, "Current website", request.CurrentWebsite);
            AddRow(rows, "Dealer locations", request.LocationCount.HasValue ? request.LocationCount.Value.ToString() : null);
            AddRow(rows, "Inventory size", request.InventorySize);
            AddRow(rows, "Primary goal", request.PrimaryGoal);
            AddRow(rows, "Preferred contact", PreferredContact(request));
            AddRow(rows, "Request ID", requestId.ToString());

            var body = "<!doctype html><html><body style=\"margin:0;padding:0;background:#eef0f2;font-family:Arial,sans-serif;color:#202327\">" +
                "<table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" style=\"background:#eef0f2;padding:32px 14px\"><tr><td align=\"center\">" +
                "<table role=\"presentation\" width=\"640\" cellspacing=\"0\" cellpadding=\"0\" style=\"max-width:640px;width:100%;overflow:hidden;border:1px solid #d5d9dd;border-radius:16px;background:#ffffff;box-shadow:0 18px 48px rgba(20,24,28,.12)\">" +
                "<tr><td style=\"padding:36px 40px;background:linear-gradient(135deg,#111315,#353a40);color:#ffffff\">" + SmtpMailSender.LogoToken + "<div style=\"font-size:12px;letter-spacing:1.6px;color:#c9cdd2\">A NEW CONVERSATION</div><h1 style=\"margin:13px 0 9px;font-size:29px;font-weight:500\">A dealership is ready to move.</h1><p style=\"margin:0;color:#c8cdd2;font-size:15px;line-height:1.7\">" + Encode(request.BusinessName) + " has opened the door to what comes next.</p></td></tr>" +
                "<tr><td style=\"padding:32px 40px\"><p style=\"margin:0 0 25px;color:#4c535a;font-size:15px;line-height:1.75\">Every dealership reaches a moment when the experience it has is no longer enough for the future it sees. " + Encode(request.BusinessName) + " may be standing at that threshold—and they have invited us into the conversation.</p>" +
                "<h2 style=\"margin:0 0 18px;font-size:19px;font-weight:500\">The opportunity at a glance</h2><table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\">" + rows + "</table>" +
                "<div style=\"margin-top:26px;padding:22px;border-left:3px solid #555d65;border-radius:8px;background:#f1f3f5\"><div style=\"margin-bottom:8px;font-size:12px;letter-spacing:1px;color:#687078\">IN THEIR OWN WORDS</div><div style=\"font-size:15px;line-height:1.75;color:#30343a;white-space:pre-line\">" + Encode(request.Message) + "</div></div>" +
                "<div style=\"margin-top:26px;padding:22px;border-radius:10px;background:#25292d;color:#ffffff\"><p style=\"margin:0 0 16px;color:#c8cdd2;font-size:14px;line-height:1.65\">" + Encode(actionNote) + "</p><a href=\"" + AttributeEncode(actionHref) + "\" style=\"display:inline-block;padding:12px 18px;border:1px solid #727980;border-radius:8px;color:#ffffff;background:linear-gradient(135deg,#555d65,#353b41);font-size:14px;font-weight:600;text-decoration:none\">" + Encode(actionText) + " &rarr;</a>" +
                (prefersPhone ? "<div style=\"margin-top:13px;color:#ffffff;font-size:18px\">" + Encode(request.Phone) + "</div>" : string.Empty) + "</div>" +
                "</td></tr></table></td></tr></table></body></html>";

            var subject = "A dealership is ready to talk — " + SafeSubject(request.BusinessName);
            SmtpMailSender.Send(recipient, recipientName, subject, body, request.Email, request.ContactName);
        }

        private static bool SendCustomerConfirmation(DealerDemoRequestViewModel request, Guid requestId) {
            try {
                var body = "<!doctype html><html><body style=\"margin:0;padding:0;background:#eef0f2;font-family:Arial,sans-serif;color:#202327\">" +
                    "<table role=\"presentation\" width=\"100%\" cellspacing=\"0\" cellpadding=\"0\" style=\"background:#eef0f2;padding:32px 14px\"><tr><td align=\"center\">" +
                    "<table role=\"presentation\" width=\"640\" cellspacing=\"0\" cellpadding=\"0\" style=\"max-width:640px;width:100%;overflow:hidden;border:1px solid #d5d9dd;border-radius:16px;background:#ffffff;box-shadow:0 18px 48px rgba(20,24,28,.12)\">" +
                    "<tr><td style=\"padding:34px 38px;background:linear-gradient(135deg,#111315,#353a40);color:#ffffff\">" + SmtpMailSender.LogoToken + "<div style=\"font-size:12px;letter-spacing:1.5px;color:#c9cdd2\">REQUEST RECEIVED</div><h1 style=\"margin:12px 0 8px;font-size:28px;font-weight:500\">Your dealer demo is in motion.</h1><p style=\"margin:0;color:#c8cdd2;font-size:15px;line-height:1.6\">Thank you, " + Encode(request.ContactName) + ". We received the request for " + Encode(request.BusinessName) + ".</p></td></tr>" +
                    "<tr><td style=\"padding:30px 38px\"><h2 style=\"margin:0 0 12px;font-size:19px;font-weight:500\">What happens next</h2>" +
                    "<p style=\"margin:0;color:#555d65;font-size:15px;line-height:1.75\">Our team will review your goals, current website, inventory needs, and preferred contact method before reaching out. That preparation helps us keep the first conversation focused on your dealership instead of giving you a generic product tour.</p>" +
                    "<div style=\"margin-top:24px;padding:18px;border-left:3px solid #555d65;border-radius:8px;background:#f1f3f5;color:#4c535a;font-size:14px;line-height:1.6\"><strong style=\"color:#25292d\">Your primary goal</strong><br>" + Encode(request.PrimaryGoal) + "</div>" +
                    "<p style=\"margin:24px 0 0;color:#747b82;font-size:13px;line-height:1.6\">Reference: " + Encode(requestId.ToString()) + ". You can reply directly to this email if there is anything else you would like us to know.</p></td></tr></table></td></tr></table></body></html>";

                SmtpMailSender.Send(request.Email, request.ContactName, "We received your AutoDealer.dev demo request", body, null, null);
                return true;
            }
            catch (Exception ex) {
                Trace.TraceError("Dealer demo customer confirmation delivery failed for {0}: {1}", requestId, ex);
                return false;
            }
        }

        private static string EmailHref(DealerDemoRequestViewModel request) {
            return "mailto:" + request.Email.Trim() + "?subject=" + Uri.EscapeDataString("Your AutoDealer.dev dealer demo") +
                "&body=" + Uri.EscapeDataString("Hi " + request.ContactName + ",\r\n\r\nThank you for reaching out about " + request.BusinessName + ". ");
        }

        private static string PhoneHref(string phone) {
            var normalized = new StringBuilder();
            foreach (var character in phone ?? string.Empty) {
                if (char.IsDigit(character) || (character == '+' && normalized.Length == 0)) normalized.Append(character);
            }
            return "tel:" + normalized;
        }

        private static void AddRow(StringBuilder output, string label, string value) {
            if (string.IsNullOrWhiteSpace(value)) return;
            output.Append("<tr><td style=\"width:155px;padding:10px 12px 10px 0;border-bottom:1px solid #eceff1;color:#747b82;font-size:13px\">")
                .Append(Encode(label)).Append("</td><td style=\"padding:10px 0;border-bottom:1px solid #eceff1;color:#25292d;font-size:14px\">")
                .Append(Encode(value)).Append("</td></tr>");
        }

        private static string Encode(string value) { return HttpUtility.HtmlEncode(value ?? string.Empty); }
        private static string AttributeEncode(string value) { return HttpUtility.HtmlAttributeEncode(value ?? string.Empty); }

        private static string SafeSubject(string value) {
            return (value ?? "New dealership").Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}
