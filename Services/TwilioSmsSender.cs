using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;

namespace autodealer.dev.Services {
    internal static class TwilioSmsSender {
        private const int MaxBodyLength = 320;
        private static readonly HttpClient HttpClient = CreateHttpClient();
        private static int configurationWarningLogged;

        public static bool TrySendNewCustomer(string businessName, string contactName, string email, string phone, string clientNumber, string planCode) {
            return TrySend("new customer", Compose(
                "New customer",
                businessName,
                contactName,
                phone,
                email,
                Prefix("Client", clientNumber),
                Prefix("Plan", planCode)));
        }

        public static bool TrySendDemoRequest(string businessName, string contactName, string email, string phone, string preferredContact) {
            return TrySend("demo request", Compose(
                "New demo request",
                businessName,
                contactName,
                phone,
                email,
                Prefix("Prefers", preferredContact)));
        }

        public static bool TrySendTest(string businessName, string contactName, out string failureMessage) {
            return TrySend("admin test", Compose(
                "AutoDealer.dev test SMS",
                Prefix("Customer", businessName),
                Prefix("Contact", contactName)),
                out failureMessage);
        }

        private static bool TrySend(string eventName, string body) {
            string failureMessage;
            return TrySend(eventName, body, out failureMessage);
        }

        private static bool TrySend(string eventName, string body, out string failureMessage) {
            failureMessage = string.Empty;
            try {
                var accountSid = ReadSetting("Twilio:AccountSid");
                var authToken = ReadSetting("Twilio:AuthToken");
                if (string.IsNullOrWhiteSpace(authToken))
                    authToken = ReadSetting("Twilio:ApiKeySecret"); // Backward-compatible migration fallback.
                var fromNumber = ReadSetting("Twilio:FromNumber");
                var alertToNumber = ReadSetting("Twilio:AlertToNumber");

                if (string.IsNullOrWhiteSpace(accountSid) && string.IsNullOrWhiteSpace(authToken) &&
                    string.IsNullOrWhiteSpace(fromNumber) && string.IsNullOrWhiteSpace(alertToNumber)) {
                    failureMessage = "Twilio SMS is not configured.";
                    return false;
                }

                if (!IsAccountSid(accountSid) || string.IsNullOrWhiteSpace(authToken) ||
                    !IsE164Number(fromNumber) || !IsE164Number(alertToNumber)) {
                    LogConfigurationWarningOnce();
                    failureMessage = "Twilio SMS configuration is incomplete or invalid.";
                    return false;
                }

                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var endpoint = "https://api.twilio.com/2010-04-01/Accounts/" + accountSid.Trim() + "/Messages.json";
                using (var request = new HttpRequestMessage(HttpMethod.Post, endpoint)) {
                    var credentials = Convert.ToBase64String(Encoding.ASCII.GetBytes(accountSid.Trim() + ":" + authToken));
                    request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
                    request.Content = new FormUrlEncodedContent(new[] {
                        new KeyValuePair<string, string>("To", alertToNumber.Trim()),
                        new KeyValuePair<string, string>("From", fromNumber.Trim()),
                        new KeyValuePair<string, string>("Body", Limit(body, MaxBodyLength))
                    });

                    using (var response = HttpClient.SendAsync(request).GetAwaiter().GetResult()) {
                        if (response.IsSuccessStatusCode) return true;
                        Trace.TraceError("Twilio SMS delivery failed for {0}: HTTP {1} {2}.",
                            eventName, (int)response.StatusCode, response.ReasonPhrase);
                        failureMessage = ReadTwilioFailure(response);
                        return false;
                    }
                }
            }
            catch (Exception ex) {
                Trace.TraceError("Twilio SMS delivery failed for {0}: {1}", eventName, ex);
                failureMessage = "The Twilio SMS request could not be completed.";
                return false;
            }
        }

        private static HttpClient CreateHttpClient() {
            return new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        }

        private static string ReadTwilioFailure(HttpResponseMessage response) {
            try {
                var responseBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                var payload = JObject.Parse(responseBody);
                var code = Clean((string)payload["code"]);
                var message = Limit((string)payload["message"], 300);
                if (message.Length > 0)
                    return (code.Length > 0 ? "Twilio error " + code + ": " : "Twilio: ") + message;
            }
            catch (Exception ex) {
                Trace.TraceWarning("The Twilio error response could not be parsed: {0}", ex.Message);
            }

            return "Twilio rejected the SMS request (HTTP " +
                (int)response.StatusCode + " " + response.ReasonPhrase + ").";
        }

        private static string ReadSetting(string key) {
            var environmentKey = "AUTODEALER_" + key.Replace(':', '_').ToUpperInvariant();
            var environmentValue = Environment.GetEnvironmentVariable(environmentKey);
            if (!string.IsNullOrWhiteSpace(environmentValue)) return environmentValue.Trim();

            var standardEnvironmentKey = StandardTwilioEnvironmentKey(key);
            if (standardEnvironmentKey != null) {
                environmentValue = Environment.GetEnvironmentVariable(standardEnvironmentKey);
                if (!string.IsNullOrWhiteSpace(environmentValue)) return environmentValue.Trim();
            }

            return (ConfigurationManager.AppSettings[key] ?? string.Empty).Trim();
        }

        private static string StandardTwilioEnvironmentKey(string key) {
            switch (key) {
                case "Twilio:AccountSid": return "TWILIO_ACCOUNT_SID";
                case "Twilio:AuthToken": return "TWILIO_AUTH_TOKEN";
                case "Twilio:FromNumber": return "TWILIO_PHONE_NUMBER";
                case "Twilio:AlertToNumber": return "TWILIO_ALERT_TO_NUMBER";
                default: return null;
            }
        }

        private static bool IsAccountSid(string value) {
            return IsSid(value, 'A', 'C');
        }

        private static bool IsSid(string value, char firstCharacter, char secondCharacter) {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 34 ||
                value[0] != firstCharacter || value[1] != secondCharacter) return false;
            for (var index = 2; index < value.Length; index++) {
                if (!Uri.IsHexDigit(value[index])) return false;
            }
            return true;
        }

        private static bool IsE164Number(string value) {
            if (string.IsNullOrWhiteSpace(value) || value.Length < 9 || value.Length > 16 || value[0] != '+') return false;
            if (value[1] == '0') return false;
            for (var index = 1; index < value.Length; index++) {
                if (!char.IsDigit(value[index])) return false;
            }
            return true;
        }

        private static void LogConfigurationWarningOnce() {
            if (Interlocked.Exchange(ref configurationWarningLogged, 1) != 0) return;
            Trace.TraceWarning("Twilio SMS alerts are disabled because AccountSid, AuthToken, FromNumber, or AlertToNumber is missing or invalid.");
        }

        private static string Compose(string title, params string[] values) {
            var parts = new List<string> { title };
            foreach (var value in values) {
                var cleaned = Clean(value);
                if (cleaned.Length > 0) parts.Add(cleaned);
            }
            return string.Join(". ", parts.ToArray()) + ".";
        }

        private static string Prefix(string label, string value) {
            var cleaned = Clean(value);
            return cleaned.Length == 0 ? string.Empty : label + " " + cleaned;
        }

        private static string Clean(string value) {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return string.Join(" ", value.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));
        }

        private static string Limit(string value, int maximumLength) {
            var cleaned = Clean(value);
            if (cleaned.Length <= maximumLength) return cleaned;
            return cleaned.Substring(0, maximumLength - 3).TrimEnd() + "...";
        }
    }
}
