using System;
using System.Configuration;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace autodealer.dev.Services {
    internal sealed class MarketCheckQuota {
        private static MarketCheckQuota latest;
        private static readonly HttpClient Sidecar = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) {
            Timeout = TimeSpan.FromSeconds(2)
        };

        public long? Remaining { get; private set; }
        public long? Limit { get; private set; }
        public DateTimeOffset? ResetAt { get; private set; }
        public DateTimeOffset ObservedAt { get; private set; }

        internal static MarketCheckQuota Latest => Volatile.Read(ref latest);

        internal static void Capture(HttpResponseHeaders headers) {
            var snapshot = Parse(headers, DateTimeOffset.UtcNow);
            if (snapshot != null) Volatile.Write(ref latest, snapshot);
        }

        internal static async Task<MarketCheckQuota> GetLatestAsync() {
            if (!Uri.TryCreate(ConfigurationManager.AppSettings["MarketCheck:McpUrl"], UriKind.Absolute, out var endpoint)
                || !endpoint.IsLoopback || (endpoint.Scheme != "http" && endpoint.Scheme != "https")
                || !string.IsNullOrEmpty(endpoint.UserInfo)) return Latest;

            try {
                using (var response = await Sidecar.GetAsync(new Uri(endpoint, "/marketcheck-quota"))) {
                    if (response.IsSuccessStatusCode) {
                        var observedAt = ReadDate(response.Headers, "Quota-Observed-At");
                        var sidecarQuota = observedAt.HasValue ? Parse(response.Headers, observedAt.Value) : null;
                        var directQuota = Latest;
                        return sidecarQuota != null && (directQuota == null || sidecarQuota.ObservedAt > directQuota.ObservedAt)
                            ? sidecarQuota : directQuota;
                    }
                }
            } catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException) {
                // Direct MarketCheck calls can still supply quota when the MCP host is unavailable.
            }
            return Latest;
        }

        private static MarketCheckQuota Parse(HttpResponseHeaders headers, DateTimeOffset observedAt) {
            var remaining = ReadCount(headers, "Quota-Remaining");
            var limit = ReadCount(headers, "Quota-Limit");
            if (!remaining.HasValue && !limit.HasValue) return null;

            return new MarketCheckQuota {
                Remaining = remaining, Limit = limit, ResetAt = ReadDate(headers, "Quota-Reset-Time"), ObservedAt = observedAt
            };
        }

        private static DateTimeOffset? ReadDate(HttpResponseHeaders headers, string name) {
            var value = Read(headers, name);
            if (value?.EndsWith(" UTC", StringComparison.OrdinalIgnoreCase) == true)
                value = value.Substring(0, value.Length - 4);
            return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var date) ? (DateTimeOffset?)date : null;
        }

        private static long? ReadCount(HttpResponseHeaders headers, string name) {
            return long.TryParse(Read(headers, name), NumberStyles.None, CultureInfo.InvariantCulture, out var count)
                ? (long?)count : null;
        }

        private static string Read(HttpResponseHeaders headers, string name) {
            return headers.TryGetValues(name, out var values) ? values.FirstOrDefault()?.Trim() : null;
        }
    }
}
