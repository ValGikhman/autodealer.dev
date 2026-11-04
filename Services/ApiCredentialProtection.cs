using System;
using System.Security.Cryptography;
using System.Text;
using System.Web.Security;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Web;

namespace autodealer.dev.Services {
    public static class ApiCredentialProtection {
        public static string RecoverFromEmails(string keyId, byte[] secretHash, IEnumerable<string> emailBodies) {
            if (string.IsNullOrEmpty(keyId) || secretHash == null) return null;
            var pattern = @"(?<![A-Za-z0-9_-])" + Regex.Escape(keyId) + @"\.([A-Za-z0-9_-]{43})(?![A-Za-z0-9_-])";
            foreach (var body in emailBodies) {
                foreach (Match match in Regex.Matches(HttpUtility.HtmlDecode(body ?? ""), pattern)) {
                    using (var sha = SHA256.Create()) {
                        var actual = sha.ComputeHash(Encoding.UTF8.GetBytes(match.Groups[1].Value));
                        if (actual.SequenceEqual(secretHash)) return match.Value;
                    }
                }
            }
            return null;
        }

        public static string Protect(string credential) {
            return Convert.ToBase64String(MachineKey.Protect(Encoding.UTF8.GetBytes(credential), "AutoDealer.ApiCredential.v1"));
        }
        public static string Reveal(string protectedCredential) {
            if (string.IsNullOrWhiteSpace(protectedCredential)) return null;
            try {
                var bytes = MachineKey.Unprotect(Convert.FromBase64String(protectedCredential), "AutoDealer.ApiCredential.v1");
                return bytes == null ? null : Encoding.UTF8.GetString(bytes);
            }
            catch (CryptographicException) { return null; }
            catch (FormatException) { return null; }
        }
    }
}
