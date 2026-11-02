using System.Text.RegularExpressions;

namespace NeoOrder.OneGate.Models;

static partial class ChatMessageLinks
{
    // Treat message content as plain text. Only explicit web addresses are actionable.
    [GeneratedRegex("""https?://[^\s<>"'`，。；！？、【】（）《》“”‘’]+""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex WebAddressPattern();

    public static IEnumerable<(int Start, int Length, Uri Uri)> FindOccurrences(string? text)
    {
        if (string.IsNullOrEmpty(text)) yield break;
        foreach (Match match in WebAddressPattern().Matches(text))
        {
            string value = TrimPunctuation(match.Value);
            if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
                && uri.IsWellFormedOriginalString() && uri.Host.Length > 0)
                yield return (match.Index, value.Length, uri);
        }
    }

    static string TrimPunctuation(string value)
    {
        // Keep balanced parentheses in URLs, such as Wikipedia article names.
        int parentheses = 0, brackets = 0, braces = 0;
        foreach (char c in value)
        {
            parentheses += c == '(' ? 1 : c == ')' ? -1 : 0;
            brackets += c == '[' ? 1 : c == ']' ? -1 : 0;
            braces += c == '{' ? 1 : c == '}' ? -1 : 0;
        }
        int length = value.Length;
        while (length > 0)
        {
            char last = value[length - 1];
            if (last is '.' or ',' or ';' or ':' or '!' or '?' or '…') { }
            else if (last == ')' && parentheses < 0) parentheses++;
            else if (last == ']' && brackets < 0) brackets++;
            else if (last == '}' && braces < 0) braces++;
            else break;
            length--;
        }
        return value[..length];
    }
}
