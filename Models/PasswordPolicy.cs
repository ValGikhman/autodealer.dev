using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace autodealer.dev.Models {
    public static class PasswordPolicy {
        public const int MinimumLength = 12;
        public const int MaximumLength = 100;

        public static bool IsValid(string password) {
            return !string.IsNullOrEmpty(password) &&
                password.Length >= MinimumLength && password.Length <= MaximumLength &&
                password.Any(char.IsLower) &&
                password.Any(char.IsUpper) &&
                password.Any(char.IsDigit) &&
                password.Any(character => !char.IsLetterOrDigit(character) && !char.IsWhiteSpace(character)) &&
                !password.Any(char.IsWhiteSpace);
        }
    }

    [AttributeUsage(AttributeTargets.Property)]
    public sealed class PasswordPolicyAttribute : ValidationAttribute {
        public PasswordPolicyAttribute() {
            ErrorMessage = "Use 12–100 characters with uppercase, lowercase, a number, and a symbol, without spaces.";
        }

        public override bool IsValid(object value) {
            return value == null || PasswordPolicy.IsValid(Convert.ToString(value));
        }
    }
}
