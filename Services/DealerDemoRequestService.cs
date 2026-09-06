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

            var phoneBlock = prefersPhone ? "<div class=\"email-phone\">" + Encode(request.Phone) + "</div>" : string.Empty;
            var body = EmailTemplateRenderer.Render(EmailTemplateName.DealerDemoOwnerNotification,
                new EmailTemplateValues()
                    .Add("BUSINESS_NAME", request.BusinessName)
                    .Add("MESSAGE", request.Message)
                    .Add("ACTION_NOTE", actionNote)
                    .Add("ACTION_TEXT", actionText)
                    .AddAttribute("ACTION_HREF", actionHref)
                    .AddHtml("DETAIL_ROWS", rows.ToString())
                    .AddHtml("PHONE_BLOCK", phoneBlock));

            var subject = "A dealership is ready to talk — " + SafeSubject(request.BusinessName);
            SmtpMailSender.Send(recipient, recipientName, subject, body, request.Email, request.ContactName);
        }

        private static bool SendCustomerConfirmation(DealerDemoRequestViewModel request, Guid requestId) {
            try {
                var body = EmailTemplateRenderer.Render(EmailTemplateName.DealerDemoCustomerConfirmation,
                    new EmailTemplateValues()
                        .Add("CONTACT_NAME", request.ContactName)
                        .Add("BUSINESS_NAME", request.BusinessName)
                        .Add("PRIMARY_GOAL", request.PrimaryGoal)
                        .Add("REQUEST_ID", requestId));

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
            output.Append("<tr><td class=\"detail-label\">")
                .Append(Encode(label)).Append("</td><td class=\"detail-value\">")
                .Append(Encode(value)).Append("</td></tr>");
        }

        private static string Encode(string value) { return HttpUtility.HtmlEncode(value ?? string.Empty); }
        private static string AttributeEncode(string value) { return HttpUtility.HtmlAttributeEncode(value ?? string.Empty); }

        private static string SafeSubject(string value) {
            return (value ?? "New dealership").Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }
}
