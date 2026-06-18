namespace NeoOrder.OneGate.Services;

public enum PasswordPolicyFailure
{
    None,
    Required,
    TooShort,
    TooLong,
    TooSimple,
    TooCommon,
    RepeatedPattern,
    LeadingOrTrailingWhitespace
}

public sealed record PasswordPolicyResult(bool IsValid, PasswordPolicyFailure Failure, int Score);

public static class PasswordPolicy
{
    public const int MinLength = 10;
    public const int MaxLength = 128;

    static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "123456",
        "12345678",
        "123456789",
        "password",
        "password1",
        "qwerty",
        "qwerty123",
        "111111",
        "000000",
        "letmein",
        "wallet",
        "onegate",
        "neo123456",
        "gas123456"
    };

    public static PasswordPolicyResult Evaluate(string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
            return Invalid(PasswordPolicyFailure.Required);
        if (password.Length != password.Trim().Length)
            return Invalid(PasswordPolicyFailure.LeadingOrTrailingWhitespace);
        if (password.Length < MinLength)
            return Invalid(PasswordPolicyFailure.TooShort);
        if (password.Length > MaxLength)
            return Invalid(PasswordPolicyFailure.TooLong);

        string normalized = password;
        string lower = normalized.ToLowerInvariant();
        if (CommonPasswords.Contains(lower))
            return Invalid(PasswordPolicyFailure.TooCommon);
        if (HasRepeatedPattern(lower) || HasSequentialRun(lower, 6))
            return Invalid(PasswordPolicyFailure.RepeatedPattern);

        int classes = 0;
        if (normalized.Any(char.IsLower)) classes++;
        if (normalized.Any(char.IsUpper)) classes++;
        if (normalized.Any(char.IsDigit)) classes++;
        if (normalized.Any(c => !char.IsLetterOrDigit(c))) classes++;

        int score = Math.Min(normalized.Length, 32) + classes * 8;
        if (classes < 2 || (normalized.Length < 14 && classes < 3))
            return new(false, PasswordPolicyFailure.TooSimple, score);

        return new(true, PasswordPolicyFailure.None, score);
    }

    static PasswordPolicyResult Invalid(PasswordPolicyFailure failure)
    {
        return new(false, failure, 0);
    }

    static bool HasRepeatedPattern(string value)
    {
        if (value.Length < 6) return false;
        if (value.Distinct().Count() <= 2) return true;
        for (int size = 1; size <= Math.Min(4, value.Length / 2); size++)
        {
            if (value.Length % size != 0) continue;
            string pattern = value[..size];
            bool repeated = true;
            for (int index = size; index < value.Length; index += size)
            {
                if (value.AsSpan(index, size).SequenceEqual(pattern)) continue;
                repeated = false;
                break;
            }
            if (repeated) return true;
        }
        return false;
    }

    static bool HasSequentialRun(string value, int length)
    {
        const string ascending = "abcdefghijklmnopqrstuvwxyz0123456789";
        const string descending = "zyxwvutsrqponmlkjihgfedcba9876543210";
        for (int index = 0; index <= value.Length - length; index++)
        {
            string slice = value.Substring(index, length);
            if (ascending.Contains(slice, StringComparison.Ordinal) ||
                descending.Contains(slice, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}
