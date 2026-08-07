using System;
using System.Collections.Generic;
using System.Configuration;
using System.Net.Http;
using System.Runtime.Caching;
using System.Security.Cryptography;
using System.Text;
using System.Web;

namespace autodealer.dev.Models {
    public sealed class DemoQuotaResult {
        public bool Allowed { get; set; }
        public int Limit { get; set; }
        public int Used { get; set; }
        public int Remaining { get { return Math.Max(0, Limit - Used); } }
    }

    public static class DemoQuotaService {
        private const int DemoLimit = 5;
        private static readonly MemoryCache Cache = MemoryCache.Default;

        public static DemoQuotaResult Take(string clientIdentity, string vin) {
            var key = "autodealer-vin-demo:" + Hash(clientIdentity ?? "unknown");
            var fresh = new Counter();
            var now = DateTimeOffset.UtcNow;
            var nextUtcDay = new DateTimeOffset(now.UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
            var existing = Cache.AddOrGetExisting(key, fresh, nextUtcDay) as Counter;
            var counter = existing ?? fresh;
            lock (counter.SyncRoot) {
                if (counter.Vins.Contains(vin))
                    return new DemoQuotaResult { Allowed = true, Limit = DemoLimit, Used = counter.Vins.Count };
                if (counter.Vins.Count >= DemoLimit)
                    return new DemoQuotaResult { Allowed = false, Limit = DemoLimit, Used = counter.Vins.Count };
                counter.Vins.Add(vin);
                return new DemoQuotaResult { Allowed = true, Limit = DemoLimit, Used = counter.Vins.Count };
            }
        }

        private static string Hash(string value) {
            using (var sha = SHA256.Create()) {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
                return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
            }
        }

        private sealed class Counter {
            public readonly object SyncRoot = new object();
            public readonly HashSet<string> Vins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public static class DemoApiAccess {
        public static bool IsDemoRequest(HttpRequestMessage request) {
            if (request == null || request.Headers.Authorization == null ||
                !string.Equals(request.Headers.Authorization.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
                return false;

            var expected = ConfigurationManager.AppSettings["DemoApi:Key"];
            return !string.IsNullOrWhiteSpace(expected) && FixedTimeEquals(request.Headers.Authorization.Parameter, expected);
        }

        public static DemoQuotaResult Take(HttpRequestMessage request, string vin) {
            return IsDemoRequest(request) ? DemoQuotaService.Take(ClientIdentity(), vin) : null;
        }

        private static string ClientIdentity() {
            var request = HttpContext.Current == null ? null : HttpContext.Current.Request;
            if (request == null) return "unknown";
            var forwarded = request.Headers["X-Forwarded-For"];
            var ip = string.IsNullOrWhiteSpace(forwarded) ? request.UserHostAddress : forwarded.Split(',')[0].Trim();
            return ip + "|" + (request.UserAgent ?? string.Empty);
        }

        private static bool FixedTimeEquals(string left, string right) {
            if (left == null || right == null) return false;
            var a = Encoding.UTF8.GetBytes(left);
            var b = Encoding.UTF8.GetBytes(right);
            if (a.Length == 0 || b.Length == 0) return a.Length == b.Length;
            var difference = a.Length ^ b.Length;
            for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
                difference |= a[i % a.Length] ^ b[i % b.Length];
            return difference == 0;
        }
    }
}
