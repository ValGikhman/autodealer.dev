using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MimeKit;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Runtime.Caching;
using System.Web;

namespace autodealer.dev.Services {
    internal class AdminInboxMessageSummary {
        public uint Uid { get; set; }
        public DateTime ReceivedUtc { get; set; }
        public string From { get; set; }
        public string FromEmail { get; set; }
        public string Subject { get; set; }
        public bool IsUnread { get; set; }
    }

    internal sealed class AdminInboxMessage : AdminInboxMessageSummary {
        public string HtmlBody { get; set; }
    }

    internal static class AdminInboxService {
        private const string UnreadCacheKey = "AdminInboxUnreadCount";
        private static readonly ObjectCache Cache = MemoryCache.Default;

        internal static IReadOnlyList<AdminInboxMessageSummary> GetInbox() {
            using (var client = Connect()) {
                var inbox = client.Inbox;
                inbox.Open(FolderAccess.ReadOnly);
                if (inbox.Count == 0) return new List<AdminInboxMessageSummary>();

                var items = inbox.Fetch(
                    0,
                    -1,
                    MessageSummaryItems.UniqueId |
                    MessageSummaryItems.Envelope |
                    MessageSummaryItems.InternalDate |
                    MessageSummaryItems.Flags);

                return items
                    .Select(ToSummary)
                    .OrderByDescending(message => message.ReceivedUtc)
                    .ToList();
            }
        }

        internal static int GetUnreadCount() {
            var cached = Cache.Get(UnreadCacheKey);
            if (cached is int) return (int)cached;

            using (var client = Connect()) {
                var inbox = client.Inbox;
                inbox.Open(FolderAccess.ReadOnly);
                var count = inbox.Search(SearchQuery.NotSeen).Count;
                Cache.Set(UnreadCacheKey, count, DateTimeOffset.UtcNow.AddSeconds(30));
                return count;
            }
        }

        internal static AdminInboxMessage GetMessage(uint uid) {
            if (uid == 0) throw new ArgumentOutOfRangeException("uid");

            using (var client = Connect()) {
                var inbox = client.Inbox;
                inbox.Open(FolderAccess.ReadWrite);
                var uniqueId = new UniqueId(uid);
                var message = inbox.GetMessage(uniqueId);
                var flags = inbox.Fetch(
                    new[] { uniqueId },
                    MessageSummaryItems.UniqueId | MessageSummaryItems.Flags)
                    .FirstOrDefault();

                if (flags == null || !flags.Flags.GetValueOrDefault().HasFlag(MessageFlags.Seen))
                    inbox.AddFlags(uniqueId, MessageFlags.Seen, true);

                Cache.Remove(UnreadCacheKey);
                var from = message.From.Mailboxes.FirstOrDefault();
                var received = message.Date == DateTimeOffset.MinValue
                    ? DateTime.UtcNow
                    : message.Date.UtcDateTime;

                return new AdminInboxMessage {
                    Uid = uid,
                    ReceivedUtc = received,
                    From = FormatMailbox(from),
                    FromEmail = from == null ? string.Empty : from.Address,
                    Subject = string.IsNullOrWhiteSpace(message.Subject) ? "(No subject)" : message.Subject,
                    IsUnread = false,
                    HtmlBody = RenderBody(message)
                };
            }
        }

        internal static int MoveToTrash(uint uid) {
            if (uid == 0) throw new ArgumentOutOfRangeException("uid");

            using (var client = Connect()) {
                var inbox = client.Inbox;
                inbox.Open(FolderAccess.ReadWrite);
                var trash = client.GetFolder(SpecialFolder.Trash);
                if (trash == null)
                    throw new InvalidOperationException("The mailbox does not expose a Trash folder.");

                inbox.MoveTo(new UniqueId(uid), trash);
                var unreadCount = inbox.Search(SearchQuery.NotSeen).Count;
                Cache.Set(UnreadCacheKey, unreadCount, DateTimeOffset.UtcNow.AddSeconds(30));
                return unreadCount;
            }
        }

        private static AdminInboxMessageSummary ToSummary(IMessageSummary item) {
            var envelope = item.Envelope;
            var from = envelope == null ? null : envelope.From.Mailboxes.FirstOrDefault();
            var received = item.InternalDate ?? (envelope == null ? null : envelope.Date);

            return new AdminInboxMessageSummary {
                Uid = item.UniqueId.Id,
                ReceivedUtc = received.HasValue ? received.Value.UtcDateTime : DateTime.MinValue,
                From = FormatMailbox(from),
                FromEmail = from == null ? string.Empty : from.Address,
                Subject = envelope == null || string.IsNullOrWhiteSpace(envelope.Subject)
                    ? "(No subject)"
                    : envelope.Subject,
                IsUnread = !item.Flags.GetValueOrDefault().HasFlag(MessageFlags.Seen)
            };
        }

        private static string FormatMailbox(MailboxAddress mailbox) {
            if (mailbox == null) return "Unknown sender";
            if (string.IsNullOrWhiteSpace(mailbox.Name) ||
                string.Equals(mailbox.Name, mailbox.Address, StringComparison.OrdinalIgnoreCase))
                return mailbox.Address;
            return mailbox.Name + " <" + mailbox.Address + ">";
        }

        private static string RenderBody(MimeMessage message) {
            if (!string.IsNullOrWhiteSpace(message.HtmlBody)) return message.HtmlBody;

            var text = HttpUtility.HtmlEncode(message.TextBody ?? "This message has no displayable body.");
            return "<!doctype html><html><head><meta charset=\"utf-8\"><style>" +
                "body{margin:0;padding:24px;color:#252a2f;background:#fff;font:15px/1.6 Arial,sans-serif}" +
                "pre{margin:0;white-space:pre-wrap;overflow-wrap:anywhere;font:inherit}</style></head>" +
                "<body><pre>" + text + "</pre></body></html>";
        }

        private static ImapClient Connect() {
            var host = ReadSetting("Imap:Host", "imap.ionos.com");
            var username = ReadSetting("Imap:Username", ReadSetting("Smtp:Username", string.Empty));
            var password = ReadSetting("Imap:Password", ReadSetting("Smtp:Password", string.Empty));
            int port;
            if (!int.TryParse(ReadSetting("Imap:Port", "993"), out port)) port = 993;

            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException("The inbox connection is not configured.");

            var client = new ImapClient { Timeout = 15000 };
            try {
                client.Connect(host.Trim(), port, SecureSocketOptions.SslOnConnect);
                client.AuthenticationMechanisms.Remove("XOAUTH2");
                client.Authenticate(username.Trim(), password);
                return client;
            }
            catch {
                client.Dispose();
                throw;
            }
        }

        private static string ReadSetting(string key, string fallback) {
            var environmentKey = "AUTODEALER_" + key.Replace(':', '_').ToUpperInvariant();
            var environmentValue = Environment.GetEnvironmentVariable(environmentKey);
            if (!string.IsNullOrWhiteSpace(environmentValue)) return environmentValue;
            var configured = ConfigurationManager.AppSettings[key];
            return string.IsNullOrWhiteSpace(configured) ? fallback : configured;
        }
    }
}
