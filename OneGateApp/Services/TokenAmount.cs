using System.Globalization;
using System.Numerics;

namespace NeoOrder.OneGate.Services;

static class TokenAmount
{
    // Generous enough for token precision, but bounds work on untrusted QR/link
    // amounts before trimming, splitting, padding or parsing a BigInteger.
    const int MaxInputLength = 1024;

    public static string Format(BigInteger units, byte decimals, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        string digits = BigInteger.Abs(units).ToString(CultureInfo.InvariantCulture).PadLeft(decimals + 1, '0');
        string whole = decimals == 0 ? digits : digits[..^decimals];
        string fraction = decimals == 0 ? "" : digits[^decimals..].TrimEnd('0');
        string sign = units.Sign < 0 ? culture.NumberFormat.NegativeSign : "";
        return sign + whole + (fraction.Length == 0 ? "" : culture.NumberFormat.NumberDecimalSeparator + fraction);
    }

    public static bool TryParse(string? text, byte decimals, out BigInteger units, CultureInfo? culture = null)
    {
        units = BigInteger.Zero;
        if (text is null || text.Length > MaxInputLength || string.IsNullOrWhiteSpace(text)) return false;
        culture ??= CultureInfo.CurrentCulture;
        string[] parts = text.Trim().Split(culture.NumberFormat.NumberDecimalSeparator, StringSplitOptions.None);
        if (parts.Length > 2) return false;
        string whole = parts[0];
        string fraction = parts.Length == 2 ? parts[1] : "";
        if (whole.Length + fraction.Length == 0) return false;
        // Amount fields accept ungrouped decimal digits only. In particular,
        // a locale's grouping separator must never change the transfer amount.
        if (!whole.All(char.IsAsciiDigit) || !fraction.All(char.IsAsciiDigit)) return false;
        fraction = fraction.TrimEnd('0');
        if (fraction.Length > decimals) return false;
        string digits = (whole.Length == 0 ? "0" : whole) + fraction.PadRight(decimals, '0');
        return BigInteger.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out units);
    }
}
