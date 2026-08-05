using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;

namespace autodealer.dev.Services {
    internal static class EmailTemplateRenderer {
        private const string CssToken = "{{EMAIL_CSS}}";
        private static readonly Regex VariableToken = new Regex(@"\{\{[A-Z_]+\}\}", RegexOptions.Compiled);

        private static readonly IDictionary<EmailTemplateName, string> TemplateFiles =
            new Dictionary<EmailTemplateName, string> {
                { EmailTemplateName.ApiCredentials, "api-credentials.html" },
                { EmailTemplateName.NewApiAccount, "new-api-account.html" },
                { EmailTemplateName.DealerDemoOwnerNotification, "dealer-demo-owner.html" },
                { EmailTemplateName.DealerDemoCustomerConfirmation, "dealer-demo-confirmation.html" },
                { EmailTemplateName.ContactInquiryNotification, "contact-inquiry.html" }
            };

        public static string Render(EmailTemplateName templateName, EmailTemplateValues values) {
            string templateFile;
            if (!TemplateFiles.TryGetValue(templateName, out templateFile))
                throw new ArgumentOutOfRangeException("templateName", templateName, "Unknown email template.");

            var baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            var templatePath = Path.Combine(baseDirectory, "Templates", "Emails", templateFile);
            var cssPath = Path.Combine(baseDirectory, "Content", "email.css");
            var body = ReadToString(templatePath).Replace(CssToken, ReadToString(cssPath));

            var replacements = (values ?? new EmailTemplateValues()).Items;
            body = VariableToken.Replace(body, match => {
                if (match.Value == SmtpMailSender.LogoToken) return match.Value;
                var name = match.Value.Substring(2, match.Value.Length - 4);
                string value;
                if (!replacements.TryGetValue(name, out value))
                    throw new InvalidDataException("Email template variable was not supplied: " + name);
                return value;
            });

            return body;
        }

        private static string ReadToString(string path) {
            if (!File.Exists(path)) throw new FileNotFoundException("Email asset was not found.", path);
            using (var stream = File.OpenRead(path))
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
                return reader.ReadToEnd();
        }
    }

    internal sealed class EmailTemplateValues {
        private readonly IDictionary<string, string> items = new Dictionary<string, string>(StringComparer.Ordinal);

        internal IDictionary<string, string> Items { get { return items; } }

        public EmailTemplateValues Add(string name, object value) {
            items[name] = HttpUtility.HtmlEncode(value == null ? string.Empty : Convert.ToString(value));
            return this;
        }

        public EmailTemplateValues AddAttribute(string name, string value) {
            items[name] = HttpUtility.HtmlAttributeEncode(value ?? string.Empty);
            return this;
        }

        public EmailTemplateValues AddHtml(string name, string trustedHtml) {
            items[name] = trustedHtml ?? string.Empty;
            return this;
        }
    }
}
