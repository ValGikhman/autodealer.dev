using System;
using System.Security.Cryptography;

namespace autodealer.dev.Services {
    internal static class ClientNumberGenerator {
        public static string Generate() {
            return "DLR-" + DateTime.UtcNow.ToString("yyMMdd") + "-" + RandomToken(4).ToUpperInvariant();
        }

        public static string GenerateTemporaryPassword() {
            return RandomToken(12) + "!aA1";
        }

        private static string RandomToken(int byteCount) {
            var bytes = new byte[byteCount];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }
}
