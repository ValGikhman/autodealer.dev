using System;
using System.Configuration;
using System.Web;

namespace autodealer.dev.Services {
    public static class BillingPaymentUrlResolver {
        private const string SettingPrefix = "Billing:PaymentUrl:";

        public static string Resolve(string planCode) {
            var normalizedPlanCode = (planCode ?? string.Empty).Trim().ToUpperInvariant();
            if (normalizedPlanCode.Length == 0) return string.Empty;

            var configuredUrl = (ConfigurationManager.AppSettings[SettingPrefix + normalizedPlanCode] ?? string.Empty).Trim();
            Uri paymentUri;
            if (!Uri.TryCreate(configuredUrl, UriKind.Absolute, out paymentUri) ||
                !string.Equals(paymentUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                return string.Empty;

            return paymentUri.AbsoluteUri;
        }

        public static string Resolve(string planCode, string clientReferenceId, string email) {
            var paymentUrl = Resolve(planCode);
            if (paymentUrl.Length == 0) return string.Empty;

            var paymentUri = new UriBuilder(paymentUrl);
            var query = HttpUtility.ParseQueryString(paymentUri.Query);
            var normalizedReference = (clientReferenceId ?? string.Empty).Trim();
            var normalizedEmail = (email ?? string.Empty).Trim();
            if (normalizedReference.Length > 0)
                query["client_reference_id"] = normalizedReference.Substring(0, Math.Min(normalizedReference.Length, 200));
            if (normalizedEmail.Length > 0)
                query["prefilled_email"] = normalizedEmail;
            paymentUri.Query = query.ToString();
            return paymentUri.Uri.AbsoluteUri;
        }
    }
}
