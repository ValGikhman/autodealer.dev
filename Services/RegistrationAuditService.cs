using autodealer.dev.Data;
using autodealer.dev.Models;
using System;
using System.Data;
using System.Data.SqlClient;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using System.Web.Security;

namespace autodealer.dev.Services {
    public sealed class RegistrationBlockedException : InvalidOperationException {
        public RegistrationBlockedException() : base("Registration from this connection is temporarily blocked. Please contact our team if this is a mistake.") { }
    }

    public sealed class RegistrationAuditService {
        private const string CookieName = "AutoDealer.Visitor";
        private const string Purpose = "AutoDealer.RegistrationVisitor.v1";
        private const string AuditItem = "AutoDealer.RegistrationAudit";
        private readonly string connectionString;
        public RegistrationAuditService() : this(AutoDealerConnectionString.Resolve()) { }
        public RegistrationAuditService(string connectionString) { this.connectionString = connectionString; }

        // This is a non-authentication visitor cookie, never an ASP.NET/authentication session ID.
        public static byte[] EnsureVisitor(HttpContext context) {
            if (context == null) return null;
            context.Response.Cache.SetCacheability(HttpCacheability.NoCache);
            context.Response.Cache.SetNoStore();
            var value = context.Request.Cookies[CookieName] == null ? null : context.Request.Cookies[CookieName].Value;
            var valid = false;
            if (!string.IsNullOrEmpty(value) && value.Length <= 1024) {
                try {
                    var bytes = MachineKey.Unprotect(Convert.FromBase64String(value), Purpose);
                    var parts = bytes == null ? new string[0] : Encoding.UTF8.GetString(bytes).Split('|');
                    long ticks;
                    Guid visitor;
                    valid = parts.Length == 2 && Guid.TryParseExact(parts[0], "N", out visitor) &&
                        long.TryParse(parts[1], out ticks) && ticks > DateTime.UtcNow.Ticks && ticks <= DateTime.UtcNow.AddDays(31).Ticks;
                }
                catch (Exception ex) when (ex is CryptographicException || ex is FormatException || ex is ArgumentException) { }
            }
            if (!valid) {
                var expires = DateTime.UtcNow.AddDays(30);
                value = Convert.ToBase64String(MachineKey.Protect(Encoding.UTF8.GetBytes(Guid.NewGuid().ToString("N") + "|" + expires.Ticks), Purpose));
                context.Response.Cookies.Add(new HttpCookie(CookieName, value) {
                    HttpOnly = true, Secure = true, SameSite = SameSiteMode.Lax, Path = "/", Expires = expires
                });
            }
            using (var sha = SHA256.Create()) return sha.ComputeHash(Encoding.UTF8.GetBytes(value));
        }

        public Guid Begin(HttpContext context, string planCode, bool createdByAdmin) {
            if (context != null && context.Items[AuditItem] is Guid) return (Guid)context.Items[AuditItem];
            var request = context == null ? null : context.Request;
            var ip = request == null ? null : NormalizeIp(request.UserHostAddress);
            if (request != null && ip == null) throw new InvalidOperationException("The registration source could not be verified.");
            var record = new ClientRegistrationAuditRecord {
                AuditId = Guid.NewGuid(), RecordedUtc = DateTime.UtcNow, EventType = "submission",
                Source = context == null ? "system" : createdByAdmin ? "admin" : "public",
                SourceIp = ip, PeerIp = ip,
                ForwardedFor = request == null ? null : NormalizeForwarded(request.Headers["X-Forwarded-For"]),
                UserAgent = request == null ? null : Limit(request.UserAgent, 512),
                AcceptLanguage = request == null ? null : Limit(request.Headers["Accept-Language"], 256),
                VisitorHash = EnsureVisitor(context), RequestPath = request == null ? null : Limit(request.Path, 300),
                PlanCode = Limit(planCode, 32),
                Actor = context != null && context.User != null && context.User.Identity != null && context.User.Identity.IsAuthenticated
                    ? Limit(context.User.Identity.Name, 254) : null
            };
            var allowed = InTransaction(db => {
                var blocked = record.Source == "public" && IsBlocked(db, ip, DateTime.UtcNow);
                record.Outcome = blocked ? "blocked" : "started";
                record.CompletedUtc = blocked ? (DateTime?)DateTime.UtcNow : null;
                db.GetTable<ClientRegistrationAuditRecord>().InsertOnSubmit(record);
                return !blocked;
            });
            if (context != null) context.Response.AppendHeader("X-Registration-Id", record.AuditId.ToString());
            if (!allowed) throw new RegistrationBlockedException();
            if (context != null) context.Items[AuditItem] = record.AuditId;
            return record.AuditId;
        }

        // Uses the SAME DataContext/transaction as customer creation.
        public void Complete(Guid id, long clientId, AutoDealerDataContext db) {
            var record = db.GetTable<ClientRegistrationAuditRecord>().SingleOrDefault(x =>
                x.AuditId == id && x.EventType == "submission" && x.Outcome == "started");
            if (record == null) throw new InvalidOperationException("Registration security could not be recorded. Please try again later.");
            if (record.Source == "public" && IsBlocked(db, record.SourceIp, DateTime.UtcNow))
                throw new RegistrationBlockedException();
            record.ClientId = clientId;
            record.Outcome = "created";
            record.CompletedUtc = DateTime.UtcNow;
            db.SubmitChanges();
        }

        public void Reject(Guid id, bool blocked = false) {
            try {
                InTransaction(db => {
                    var record = db.GetTable<ClientRegistrationAuditRecord>().SingleOrDefault(x => x.AuditId == id && x.Outcome == "started");
                    if (record != null) {
                        record.Outcome = blocked ? "blocked" : "rejected";
                        record.CompletedUtc = DateTime.UtcNow;
                    }
                    return 0;
                });
            }
            catch (SqlException ex) { Trace.TraceError("Registration outcome could not be recorded. Audit={0}, SQL={1}", id, ex.Number); }
        }

        public ClientRegistrationAuditViewModel GetForClient(long clientId) {
            if (clientId <= 0) throw new ArgumentException("Select a valid customer.");
            using (var db = new AutoDealerDataContext(connectionString)) {
                var rows = db.GetTable<ClientRegistrationAuditRecord>();
                var record = rows.Where(x => x.ClientId == clientId && x.EventType == "submission" && x.Outcome == "created")
                    .OrderByDescending(x => x.RecordedUtc).FirstOrDefault();
                if (record == null) return null;
                var block = db.GetTable<ClientRegistrationIpBlockRecord>().SingleOrDefault(x => x.SourceIp == record.SourceIp);
                var since = DateTime.UtcNow.AddDays(-7);
                return new ClientRegistrationAuditViewModel {
                    AuditId = record.AuditId, RecordedUtc = Utc(record.RecordedUtc), Source = record.Source,
                    SourceIp = record.SourceIp, PeerIp = record.PeerIp, ForwardedFor = record.ForwardedFor,
                    TrustedProxy = record.TrustedProxy, UserAgent = record.UserAgent, AcceptLanguage = record.AcceptLanguage,
                    VisitorReference = record.VisitorHash == null ? null : BitConverter.ToString(record.VisitorHash.ToArray()).Replace("-", "").Substring(0, 16),
                    CountryCode = record.CountryCode, RequestPath = record.RequestPath, PlanCode = record.PlanCode, Actor = record.Actor,
                    BlockedUntilUtc = block != null && block.BlockedUntilUtc > DateTime.UtcNow ? Utc(block.BlockedUntilUtc) : null,
                    RecentAttempts = record.SourceIp == null ? 0 : rows.LongCount(x => x.EventType == "submission" && x.SourceIp == record.SourceIp && x.RecordedUtc >= since)
                };
            }
        }

        public void SetBlock(Guid auditId, bool block, string actor, string peerIp) {
            if (string.IsNullOrWhiteSpace(actor)) throw new ArgumentException("An administrator identity is required.");
            InTransaction(db => {
                var record = db.GetTable<ClientRegistrationAuditRecord>().SingleOrDefault(x =>
                    x.AuditId == auditId && x.EventType == "submission" && x.Outcome == "created" && x.Source == "public");
                if (record == null || string.IsNullOrEmpty(record.SourceIp))
                    throw new ArgumentException("This account has no recorded public registration IP to block.");
                var now = DateTime.UtcNow;
                var blocks = db.GetTable<ClientRegistrationIpBlockRecord>();
                var ipBlock = blocks.SingleOrDefault(x => x.SourceIp == record.SourceIp);
                if (ipBlock == null) {
                    ipBlock = new ClientRegistrationIpBlockRecord { SourceIp = record.SourceIp };
                    blocks.InsertOnSubmit(ipBlock);
                }
                ipBlock.BlockedUntilUtc = block ? now.AddHours(24) : now;
                ipBlock.UpdatedUtc = now;
                ipBlock.UpdatedBy = Limit(actor, 254);
                db.GetTable<ClientRegistrationAuditRecord>().InsertOnSubmit(new ClientRegistrationAuditRecord {
                    AuditId = Guid.NewGuid(), ClientId = record.ClientId, RecordedUtc = now, CompletedUtc = now,
                    EventType = block ? "block" : "unblock", Source = "admin", Outcome = "completed",
                    SourceIp = record.SourceIp, PeerIp = NormalizeIp(peerIp), Actor = Limit(actor, 254)
                });
                return 0;
            });
        }

        private T InTransaction<T>(Func<AutoDealerDataContext, T> action) {
            using (var db = new AutoDealerDataContext(connectionString)) {
                db.CommandTimeout = 15;
                db.Connection.Open();
                using (var transaction = db.Connection.BeginTransaction(IsolationLevel.Serializable)) {
                    db.Transaction = transaction;
                    try {
                        var result = action(db);
                        db.SubmitChanges();
                        transaction.Commit();
                        return result;
                    }
                    catch { transaction.Rollback(); throw; }
                }
            }
        }
        private static bool IsBlocked(AutoDealerDataContext db, string ip, DateTime now) {
            return ip != null && db.GetTable<ClientRegistrationIpBlockRecord>().Any(x => x.SourceIp == ip && x.BlockedUntilUtc > now);
        }
        public static string NormalizeIp(string value) {
            IPAddress address;
            if (!IPAddress.TryParse((value ?? "").Trim(), out address)) return null;
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            return address.ToString();
        }
        private static string NormalizeForwarded(string value) {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (value.Length > 1024) return "[invalid]";
            var parts = value.Split(',');
            if (parts.Length > 16) return "[invalid]";
            var addresses = parts.Select(NormalizeIp).ToArray();
            return addresses.Any(x => x == null) ? "[invalid]" : string.Join(", ", addresses);
        }
        private static string Limit(string value, int length) {
            if (value == null) return null;
            value = value.Replace('\r', ' ').Replace('\n', ' ');
            return value.Length > length ? value.Substring(0, length) : value;
        }
        private static string Utc(DateTime value) { return value.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture); }
    }
}