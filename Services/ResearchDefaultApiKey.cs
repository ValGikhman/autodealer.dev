using System;
using System.Configuration;
using System.Linq;
using System.Net.Http;
using System.Web;
using System.Web.Helpers;
using System.Web.Security;
using HttpAntiForgeryException = System.Web.Mvc.HttpAntiForgeryException;

namespace autodealer.dev.Services {
    /// <summary>Resolves the saved account key for the two authenticated Majordome tools.</summary>
    public static class ResearchDefaultApiKey {
        public const string VerificationHeaderName = "X-Research-Verification-Token";

        public static bool IsConfigured {
            get { return !string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings["Research:DefaultApiKey"]); }
        }

        public static string Resolve(HttpRequestMessage request) {
            var context = HttpContext.Current;
            return Resolve(request, context == null ? null : new HttpContextWrapper(context),
                () => ConfigurationManager.AppSettings["Research:DefaultApiKey"], AntiForgery.Validate);
        }

        // Explicit credentials retain their normal authentication result, including
        // invalid schemes, blank values and an empty access_token query parameter.
        public static bool HasExplicitCredentials(HttpRequestMessage request) {
            if (request == null) return false;
            if (request.Headers.Contains("Authorization")) return true;
            if (request.RequestUri == null) return false;
            foreach (var parameter in request.RequestUri.Query.TrimStart('?').Split('&')) {
                var equals = parameter.IndexOf('=');
                var name = equals < 0 ? parameter : parameter.Substring(0, equals);
                if (string.Equals(HttpUtility.UrlDecode(name), "access_token", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        // Dependencies are explicit so regression fixtures can exercise the same
        // eligibility rules without real credentials, cookies, databases or APIs.
        public static string Resolve(HttpRequestMessage request, HttpContextBase context,
            Func<string> readConfiguredKey, Action<string, string> validateVerification) {
            if (readConfiguredKey == null) throw new ArgumentNullException("readConfiguredKey");
            if (validateVerification == null) throw new ArgumentNullException("validateVerification");
            if (request == null || request.Method != HttpMethod.Post || request.RequestUri == null || HasExplicitCredentials(request)) return null;
            var path = request.RequestUri.AbsolutePath;
            if (!string.Equals(path, "/api/service/price-research", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(path, "/api/service/neovin", StringComparison.OrdinalIgnoreCase)) return null;

            var user = context == null ? null : context.User;
            var identity = user == null ? null : user.Identity as FormsIdentity;
            if (identity == null || !identity.IsAuthenticated || identity.Ticket == null || identity.Ticket.Expired ||
                !string.Equals(identity.Ticket.UserData, "Admin", StringComparison.Ordinal) || !user.IsInRole("Admin")) return null;

            System.Collections.Generic.IEnumerable<string> values;
            if (!request.Headers.TryGetValues(VerificationHeaderName, out values)) return null;
            var tokens = values.ToArray();
            if (tokens.Length != 1) throw new HttpAntiForgeryException("The research verification token is invalid.");
            var token = tokens[0];
            if (string.IsNullOrWhiteSpace(token)) return null;
            var key = readConfiguredKey();
            if (string.IsNullOrWhiteSpace(key)) return null;
            var cookie = context.Request.Cookies[AntiForgeryConfig.CookieName];
            validateVerification(cookie == null ? null : cookie.Value, token);
            return key.Trim();
        }
    }
}
