using autodealer.dev.Models;
using System;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace autodealer.dev.Services {
    public sealed class MarketDemandService {
        private static readonly HttpClient SharedClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) };
        private readonly HttpClient client;
        private readonly string apiKey;
        public MarketDemandService() : this(SharedClient, null) { }
        public MarketDemandService(HttpClient client, string apiKey) { this.client = client; this.apiKey = apiKey; }
        public async Task<MarketDemandResult> GetAsync(PriceResearchRequest input, CancellationToken token) {
            input.NormalizeAndValidate();
            if (string.Equals(ConfigurationManager.AppSettings["Research:DemandEnabled"], "false", StringComparison.OrdinalIgnoreCase))
                return MarketDemandResult.Unavailable("Local demand analysis is disabled.");
            var key = apiKey ?? ConfigurationManager.AppSettings["MarketCheck:ApiKey"];
            if (string.IsNullOrWhiteSpace(key)) key = Environment.GetEnvironmentVariable("MARKETCHECK_API_KEY");
            if (string.IsNullOrWhiteSpace(key)) return MarketDemandResult.Unavailable("Local demand data is not configured.");
            int seconds;
            if (!int.TryParse(ConfigurationManager.AppSettings["Research:DemandTimeoutSeconds"], out seconds) || seconds < 1 || seconds > 30) seconds = 12;
            var url = "https://api.marketcheck.com/v2/mds/car?api_key=" + Uri.EscapeDataString(key.Trim())
                + "&vin=" + Uri.EscapeDataString(input.Vin) + "&exact=true&car_type=used&zip=" + Uri.EscapeDataString(input.Zip)
                + "&radius=" + input.Radius.ToString(CultureInfo.InvariantCulture);
            try {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token)) {
                    timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
                    using (var request = new HttpRequestMessage(HttpMethod.Get, url)) {
                        request.Headers.Accept.ParseAdd("application/json");
                        using (var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false)) {
                            if (!response.IsSuccessStatusCode) {
                                var status = (int)response.StatusCode;
                                return MarketDemandResult.Unavailable(status == 401 || status == 403 ? "MarketCheck demand access is unavailable for the configured subscription." : status == 429 ? "MarketCheck demand request limit reached. Pricing remains available." : "MarketCheck demand data is temporarily unavailable.");
                            }
                            using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                            using (var bytes = new MemoryStream()) {
                                var buffer = new byte[4096]; int read;
                                while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false)) > 0) {
                                    if (bytes.Length + read > 1000000) return MarketDemandResult.Unavailable("MarketCheck demand data could not be read.");
                                    bytes.Write(buffer, 0, read);
                                }
                                return MarketDemandResult.Parse(System.Text.Encoding.UTF8.GetString(bytes.ToArray()).TrimStart('\ufeff'));
                            }
                        }
                    }
                }
            } catch (OperationCanceledException) when (!token.IsCancellationRequested) {
                return MarketDemandResult.Unavailable("Local demand lookup timed out. Pricing remains available.");
            } catch (Exception ex) when (ex is HttpRequestException || ex is IOException || ex is JsonException || ex is FormatException) {
                return MarketDemandResult.Unavailable("MarketCheck demand data could not be read. Pricing remains available.");
            }
        }
    }
}
