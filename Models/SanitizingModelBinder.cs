using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using System.Web.Mvc;

namespace autodealer.dev.Models {
    public enum InputSanitizationKind {
        PlainText,
        MultilineText,
        Email,
        Phone,
        Url,
        Identifier,
        Token
    }

    [AttributeUsage(AttributeTargets.Property)]
    public sealed class SanitizeInputAttribute : Attribute {
        public SanitizeInputAttribute(InputSanitizationKind kind) { Kind = kind; }
        public InputSanitizationKind Kind { get; private set; }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public sealed class PreserveInputAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Property)]
    public sealed class AllowedValuesAttribute : ValidationAttribute {
        private readonly ISet<string> allowedValues;

        public AllowedValuesAttribute(params string[] values) {
            allowedValues = new HashSet<string>(values ?? new string[0], StringComparer.OrdinalIgnoreCase);
        }

        public override bool IsValid(object value) {
            return value == null || allowedValues.Contains(Convert.ToString(value));
        }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public sealed class SafeHttpUrlAttribute : ValidationAttribute {
        public override bool IsValid(object value) {
            var input = value as string;
            if (string.IsNullOrWhiteSpace(input)) return true;

            Uri uri;
            if (Uri.TryCreate(input, UriKind.Absolute, out uri)) return IsHttpUrl(uri);
            return Uri.TryCreate("https://" + input, UriKind.Absolute, out uri) && IsHttpUrl(uri);
        }

        private static bool IsHttpUrl(Uri uri) {
            return uri != null && !string.IsNullOrWhiteSpace(uri.Host) && string.IsNullOrEmpty(uri.UserInfo) &&
                (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
        }
    }

    public sealed class SanitizingModelBinder : DefaultModelBinder {
        protected override void SetProperty(ControllerContext controllerContext, ModelBindingContext bindingContext, PropertyDescriptor propertyDescriptor, object value) {
            if (value is string && propertyDescriptor.Attributes[typeof(PreserveInputAttribute)] == null) {
                var attribute = propertyDescriptor.Attributes[typeof(SanitizeInputAttribute)] as SanitizeInputAttribute;
                var kind = attribute == null ? InputSanitizationKind.PlainText : attribute.Kind;
                value = InputSanitizer.Sanitize((string)value, kind);
            }
            base.SetProperty(controllerContext, bindingContext, propertyDescriptor, value);
        }
    }

    public static class InputSanitizer {
        private static readonly ISet<char> PhoneCharacters = new HashSet<char>("+-.() xX#".ToCharArray());
        private static readonly Regex DangerousElement = new Regex(
            @"<\s*(script|style|iframe|object|embed|svg|math)\b[^>]*>[\s\S]*?<\s*/\s*\1\s*>",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));
        private static readonly Regex Markup = new Regex(
            @"<[^>]*>",
            RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));

        public static string Sanitize(string value, InputSanitizationKind kind) {
            if (value == null) return null;
            var multiline = kind == InputSanitizationKind.MultilineText;
            var sanitized = SanitizePlainText(value, multiline);

            switch (kind) {
                case InputSanitizationKind.Email:
                    return sanitized.ToLowerInvariant();
                case InputSanitizationKind.Phone:
                    return new string(sanitized.Where(character => char.IsDigit(character) || PhoneCharacters.Contains(character)).ToArray()).Trim();
                case InputSanitizationKind.Identifier:
                case InputSanitizationKind.Token:
                case InputSanitizationKind.Url:
                case InputSanitizationKind.PlainText:
                case InputSanitizationKind.MultilineText:
                default:
                    return sanitized;
            }
        }

        private static string SanitizePlainText(string value, bool multiline) {
            var decoded = value;
            for (var pass = 0; pass < 4; pass++) {
                var next = HttpUtility.HtmlDecode(decoded);
                if (string.Equals(next, decoded, StringComparison.Ordinal)) break;
                decoded = next;
            }

            for (var pass = 0; pass < 8; pass++) {
                var withoutDangerousElements = DangerousElement.Replace(decoded, string.Empty);
                var withoutMarkup = Markup.Replace(withoutDangerousElements, string.Empty);
                if (string.Equals(withoutMarkup, decoded, StringComparison.Ordinal)) break;
                decoded = withoutMarkup;
            }

            try { decoded = decoded.Normalize(NormalizationForm.FormKC); }
            catch (ArgumentException) { /* Invalid surrogate characters are discarded below. */ }

            var output = new StringBuilder(decoded.Length);
            for (var index = 0; index < decoded.Length; index++) {
                var character = decoded[index];
                if (character == '<' || character == '>') continue;
                if (char.IsHighSurrogate(character)) {
                    if (index + 1 < decoded.Length && char.IsLowSurrogate(decoded[index + 1])) {
                        output.Append(character).Append(decoded[++index]);
                    }
                    continue;
                }
                if (char.IsLowSurrogate(character)) continue;

                var category = char.GetUnicodeCategory(character);
                if (category == UnicodeCategory.Format || category == UnicodeCategory.Surrogate) continue;
                if (char.IsControl(character)) {
                    if (multiline && (character == '\r' || character == '\n' || character == '\t')) output.Append(character);
                    continue;
                }
                output.Append(character);
            }
            return output.ToString().Trim();
        }
    }
}
