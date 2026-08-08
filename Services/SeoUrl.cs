using System;
using System.Configuration;

namespace autodealer.dev.Services {
    public static class SeoUrl {
        private const string DefaultSiteUrl = "https://autodealer.dev/";

        public static string BaseUrl {
            get {
                var configured = (ConfigurationManager.AppSettings["Seo:SiteUrl"] ?? string.Empty).Trim();
                Uri uri;
                if (!Uri.TryCreate(configured, UriKind.Absolute, out uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    uri = new Uri(DefaultSiteUrl);
                return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/') + "/";
            }
        }

        public static string Absolute(string path) {
            return new Uri(new Uri(BaseUrl), string.IsNullOrWhiteSpace(path) ? "/" : path).AbsoluteUri;
        }
    }
}
