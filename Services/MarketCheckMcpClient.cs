using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace autodealer.dev.Services {
    internal sealed class MarketCheckMcpClient {
        private const string RequestedVersion = "2025-06-18";
        private static readonly HttpClient Http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) {
            MaxResponseContentBufferSize = 2 * 1024 * 1024
        };
        private readonly Uri endpoint;
        private string version;
        private string sessionId;
        private int requestId;

        public MarketCheckMcpClient() {
            var url = ConfigurationManager.AppSettings["MarketCheck:McpUrl"];
            if (!Uri.TryCreate(url, UriKind.Absolute, out endpoint)
                || !endpoint.IsLoopback || (endpoint.Scheme != "http" && endpoint.Scheme != "https")
                || !string.IsNullOrEmpty(endpoint.UserInfo) || !string.IsNullOrEmpty(endpoint.Query))
                throw new ConfigurationErrorsException("Configure a loopback MarketCheck MCP URL.");
        }

        public async Task InitializeAsync(CancellationToken token) {
            var result = await SendAsync("initialize", new JObject {
                ["protocolVersion"] = RequestedVersion,
                ["capabilities"] = new JObject(),
                ["clientInfo"] = new JObject { ["name"] = "AutoDealer.dev", ["version"] = "1.0" }
            }, token);
            version = (string)result["protocolVersion"];
            if (version != RequestedVersion && version != "2025-03-26" && version != "2025-11-25")
                throw new HttpRequestException("Unsupported MarketCheck MCP protocol version.");
            await SendAsync("notifications/initialized", null, token, notification: true);
        }

        public async Task<JObject> CallAsync(string name, JObject arguments, CancellationToken token) {
            var result = await SendAsync("tools/call", new JObject { ["name"] = name, ["arguments"] = arguments }, token);
            if ((bool?)result["isError"] == true) throw new HttpRequestException("MarketCheck MCP rejected the tool call.");

            // The official self-hosted tools return JSON strings in MCP text content.
            var structured = result["structuredContent"] as JObject;
            var data = structured?["result"] as JObject;
            if (data == null) {
                var text = (result["content"] as JArray)?.OfType<JObject>()
                    .FirstOrDefault(item => (string)item["type"] == "text")?["text"];
                var encoded = structured?["result"]?.Type == JTokenType.String ? (string)structured["result"] : (string)text;
                data = encoded == null ? structured : JObject.Parse(encoded);
            }
            if (data == null || data["error"] != null || (bool?)data["success"] == false)
                throw new HttpRequestException("MarketCheck did not complete the lookup.");
            return data["data"] as JObject ?? data;
        }

        public async Task CloseAsync() {
            if (sessionId == null) return;
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
            using (var request = new HttpRequestMessage(HttpMethod.Delete, endpoint)) {
                AddProtocolHeaders(request);
                try {
                    using (await Http.SendAsync(request, timeout.Token)) { }
                } catch (HttpRequestException) { }
                catch (OperationCanceledException) { }
            }
        }

        private async Task<JObject> SendAsync(string method, JObject parameters, CancellationToken token, bool notification = false) {
            var id = ++requestId;
            var payload = new JObject { ["jsonrpc"] = "2.0", ["method"] = method };
            if (parameters != null) payload["params"] = parameters;
            if (!notification) payload["id"] = id;

            using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint)) {
                AddProtocolHeaders(request);
                request.Headers.Accept.ParseAdd("application/json");
                request.Headers.Accept.ParseAdd("text/event-stream");
                request.Content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");
                using (var response = await Http.SendAsync(request, token)) {
                    response.EnsureSuccessStatusCode();
                    if (method == "initialize" && response.Headers.TryGetValues("Mcp-Session-Id", out var sessions))
                        sessionId = sessions.Single();
                    if (notification) return null;
                    var body = await response.Content.ReadAsStringAsync();
                    var envelope = response.Content.Headers.ContentType?.MediaType == "text/event-stream"
                        ? ReadEvent(body, id) : JObject.Parse(body);
                    var result = envelope?["result"] as JObject;
                    if ((int?)envelope?["id"] != id || result == null)
                        throw new HttpRequestException("MarketCheck MCP returned an invalid response.");
                    return result;
                }
            }
        }

        private void AddProtocolHeaders(HttpRequestMessage request) {
            if (version != null) request.Headers.Add("MCP-Protocol-Version", version);
            if (sessionId != null) request.Headers.Add("Mcp-Session-Id", sessionId);
        }

        private static JObject ReadEvent(string body, int id) {
            using (var reader = new StringReader(body)) {
                var data = new StringBuilder();
                string line;
                while ((line = reader.ReadLine()) != null) {
                    if (line.StartsWith("data:", StringComparison.Ordinal))
                        data.AppendLine(line.Substring(5).TrimStart(' '));
                    if (line.Length != 0 || data.Length == 0) continue;
                    var message = JObject.Parse(data.ToString());
                    if ((int?)message["id"] == id) return message;
                    data.Clear();
                }
            }
            return null;
        }
    }
}
