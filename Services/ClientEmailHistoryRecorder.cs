using System;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.IO;

namespace autodealer.dev.Services {
    internal static class ClientEmailHistoryRecorder {
        public static void Record(long? clientId, string recipientAddress, string relatedCustomerAddress, string subject, string htmlBody) {
            try {
                var connectionString = AutoDealerConnectionString.Resolve();
                if (string.IsNullOrWhiteSpace(connectionString)) return;

                using (var connection = new SqlConnection(connectionString)) {
                    connection.Open();
                    var resolvedClientId = clientId ?? FindClientId(connection, recipientAddress, relatedCustomerAddress);
                    if (!resolvedClientId.HasValue) return;

                    const string sql = @"INSERT dbo.ClientEmailHistory (ClientId,ToEmail,Subject,HtmlBody)
                        VALUES (@ClientId,@ToEmail,@Subject,@HtmlBody);";
                    using (var command = new SqlCommand(sql, connection)) {
                        command.Parameters.Add("@ClientId", SqlDbType.BigInt).Value = resolvedClientId.Value;
                        command.Parameters.Add("@ToEmail", SqlDbType.NVarChar, 254).Value = (recipientAddress ?? string.Empty).Trim();
                        command.Parameters.Add("@Subject", SqlDbType.NVarChar, 998).Value = subject ?? string.Empty;
                        command.Parameters.Add("@HtmlBody", SqlDbType.NVarChar, -1).Value = EmbedImages(htmlBody);
                        command.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex) {
                Trace.TraceError("Client email history could not be recorded: {0}", ex);
            }
        }

        private static long? FindClientId(SqlConnection connection, string recipientAddress, string relatedCustomerAddress) {
            const string sql = @"SELECT TOP (1) ClientId
                FROM dbo.Clients
                WHERE Email = @RecipientEmail OR Email = @RelatedEmail
                ORDER BY CASE WHEN Email = @RecipientEmail THEN 0 ELSE 1 END;";
            using (var command = new SqlCommand(sql, connection)) {
                command.Parameters.Add("@RecipientEmail", SqlDbType.NVarChar, 254).Value = NormalizeEmail(recipientAddress);
                command.Parameters.Add("@RelatedEmail", SqlDbType.NVarChar, 254).Value = NormalizeEmail(relatedCustomerAddress);
                var result = command.ExecuteScalar();
                return result == null || result == DBNull.Value ? (long?)null : Convert.ToInt64(result);
            }
        }

        private static string NormalizeEmail(string value) {
            return (value ?? string.Empty).Trim().ToLowerInvariant();
        }

        private static string EmbedImages(string htmlBody) {
            var renderedHtml = htmlBody ?? string.Empty;
            const string logoContentId = "cid:autodealer-logo";
            if (renderedHtml.IndexOf(logoContentId, StringComparison.OrdinalIgnoreCase) < 0) return renderedHtml;

            try {
                var logoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Content", "images", "autodealer-logo.png");
                if (!File.Exists(logoPath)) return renderedHtml;
                var dataSource = "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(logoPath));
                return renderedHtml.Replace(logoContentId, dataSource);
            }
            catch (Exception ex) {
                Trace.TraceWarning("The email history logo could not be embedded: {0}", ex);
                return renderedHtml;
            }
        }
    }
}
