using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web;
using System.Web.Script.Serialization;

namespace autodealer.dev.Services {
    public static class RegistrationCaptchaService {
        public static string SiteKey { get { return Setting("SiteKey"); } }

        private static string Setting(string name) {
            var environmentValue = Environment.GetEnvironmentVariable("AUTODEALER_TURNSTILE_" + name.ToUpperInvariant());
            return (string.IsNullOrWhiteSpace(environmentValue)
                ? ConfigurationManager.AppSettings["Turnstile:" + name] ?? string.Empty
                : environmentValue).Trim();
        }

        // Called at the shared account-creation boundary, never trusted from a posted boolean.
        public static void Validate(HttpContext context) {
            var secret = Setting("SecretKey");
            var hosts = Setting("Hostnames").Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
            if (SiteKey.Length == 0 || secret.Length == 0 || hosts.Length == 0)
                throw new InvalidOperationException("Registration verification is not configured. Please contact our team.");
            var token = context.Request.Form["cf-turnstile-response"];
            if (string.IsNullOrWhiteSpace(token) || token.Length > 2048)
                throw new ArgumentException("Please complete the security check and submit again.");

            var body = Encoding.UTF8.GetBytes("secret=" + HttpUtility.UrlEncode(secret) +
                "&response=" + HttpUtility.UrlEncode(token) +
                "&remoteip=" + HttpUtility.UrlEncode(context.Request.UserHostAddress));
            var request = (HttpWebRequest)WebRequest.Create("https://challenges.cloudflare.com/turnstile/v0/siteverify");
            request.Method = "POST";
            request.ContentType = "application/x-www-form-urlencoded";
            request.ContentLength = body.Length;
            request.Timeout = 10000;
            request.ReadWriteTimeout = 10000;
            request.AllowAutoRedirect = false;
            bool valid;
            try {
                using (var stream = request.GetRequestStream()) stream.Write(body, 0, body.Length);
                using (var response = request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream()))
                    valid = IsValidResponse(reader.ReadToEnd(), hosts);
            }
            catch (Exception ex) when (ex is WebException || ex is IOException || ex is ArgumentException || ex is InvalidOperationException) {
                // Never log the secret, challenge token, or provider response.
                throw new InvalidOperationException("Security verification is temporarily unavailable. Please try again.");
            }
            if (!valid)
                throw new ArgumentException("The security check failed or expired. Please complete it again.");
        }

        private static bool IsValidResponse(string json, string[] hosts) {
            var result = new JavaScriptSerializer { MaxJsonLength = 16384 }.Deserialize<Verification>(json);
            return result != null && result.success && result.action == "register" &&
                hosts.Contains(result.hostname, StringComparer.OrdinalIgnoreCase);
        }

        private sealed class Verification {
            public bool success { get; set; }
            public string hostname { get; set; }
            public string action { get; set; }
        }
    }
}
