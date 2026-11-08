using System.Globalization;
using System.Resources;
using System.Text;
using System.Text.RegularExpressions;

namespace NeoOrder.OneGate.Services.Translation;

public static partial class TranslationLanguages
{
    internal static string ResolveInterfaceLanguage(ResourceManager resources, CultureInfo culture)
    {
        var resourceSet = resources.GetResourceSet(culture, createIfNotExists: true, tryParents: true);
        // ResourceManager can cache the same resource set for a locale and its fallback.
        while (culture.Name.Length != 0
            && ReferenceEquals(resourceSet, resources.GetResourceSet(culture.Parent, createIfNotExists: true, tryParents: true)))
            culture = culture.Parent;
        return culture.Name.Length == 0 ? "en" : culture.Name;
    }

    public static string? Match(string requested, IEnumerable<string> supported)
    {
        string normalized = Normalize(requested);
        var languages = supported.ToArray();
        var exact = languages.FirstOrDefault(language => Normalize(language).Equals(normalized, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;
        string root = normalized.Split('-')[0];
        // Never silently replace a requested script (notably Traditional Chinese).
        string? script = normalized.Split('-').Skip(1).FirstOrDefault(part => part.Length == 4);
        return languages.FirstOrDefault(language => Normalize(language).Equals(
            script is null ? root : root + "-" + script, StringComparison.OrdinalIgnoreCase));
    }

    public static string Normalize(string language)
    {
        string tag = language.Replace('_', '-');
        if (tag.Equals("zh", StringComparison.OrdinalIgnoreCase)) return "zh-Hans";
        if (tag.Equals("zh-CN", StringComparison.OrdinalIgnoreCase) || tag.Equals("zh-SG", StringComparison.OrdinalIgnoreCase)) return "zh-Hans";
        if (tag.Equals("zh-TW", StringComparison.OrdinalIgnoreCase) || tag.Equals("zh-HK", StringComparison.OrdinalIgnoreCase)
            || tag.Equals("zh-MO", StringComparison.OrdinalIgnoreCase)) return "zh-Hant";
        return tag;
    }

    internal static (string Language, string Model) ResolveMlKitSource(string? tag, IReadOnlyList<string> supported)
    {
        if (!string.IsNullOrWhiteSpace(tag) && !tag.Equals("und", StringComparison.OrdinalIgnoreCase))
        {
            // The detector's bare Chinese tag carries no script information.
            string source = tag.Equals("zh", StringComparison.OrdinalIgnoreCase) ? "zh" : Normalize(tag);
            if ((source == "zh" || source.Equals("zh-Hant", StringComparison.OrdinalIgnoreCase))
                && Match("zh-Hans", supported) is { } chineseModel) return (source, chineseModel);
            if (Match(source, supported) is { } match) return (match, match);
            throw new ChatTranslationException(TranslationFailure.UnsupportedSourceLanguage, [source]);
        }
        throw new ChatTranslationException(TranslationFailure.SourceLanguageRequired);
    }

    public static string DisplayName(string language)
    {
        try { return CultureInfo.GetCultureInfo(language).NativeName; }
        catch (CultureNotFoundException) { return language; }
    }

    public static bool HasText(string text) => LinkPattern().Replace(text, "").EnumerateRunes().Any(Rune.IsLetter);

    [GeneratedRegex(@"https?://\S+|www\.\S+", RegexOptions.IgnoreCase)]
    private static partial Regex LinkPattern();
}
