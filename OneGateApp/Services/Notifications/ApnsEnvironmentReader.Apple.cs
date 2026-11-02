#if IOS || MACCATALYST
using System.Xml;
using System.Xml.Linq;

namespace NeoOrder.OneGate.Services.Notifications;

internal static class ApnsEnvironmentReader
{
    internal static string? Read(string profilePath, string entitlementKey)
    {
        try
        {
            // Like Firebase, read the XML plist inside the CMS-wrapped profile.
            byte[] profile = File.ReadAllBytes(profilePath);
            int start = profile.AsSpan().IndexOf("<plist"u8);
            if (start < 0) return null;
            int end = profile.AsSpan(start).IndexOf("</plist>"u8);
            if (end < 0) return null;

            using var stream = new MemoryStream(profile, start, end + "</plist>"u8.Length, writable: false);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            });
            var root = XDocument.Load(reader).Root;
            if (root?.Name != "plist" || root.Elements().Count() != 1
                || root.Element("dict") is not { } dictionary) return null;

            var entitlements = GetDictionaryValue(dictionary, "Entitlements");
            if (entitlements?.Name != "dict") return null;
            var environment = GetDictionaryValue(entitlements, entitlementKey);
            if (environment?.Name != "string" || environment.HasElements) return null;
            return environment.Value is "development" or "production" ? environment.Value : null;
        }
        // App Store/TestFlight packages can omit the embedded profile.
        catch (FileNotFoundException) { return "production"; }
        catch (DirectoryNotFoundException) { return "production"; }
        // An existing but unreadable or malformed profile must remain unknown.
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (XmlException) { return null; }
    }

    static XElement? GetDictionaryValue(XElement dictionary, string key)
    {
        XElement? result = null;
        using var elements = dictionary.Elements().GetEnumerator();
        while (elements.MoveNext())
        {
            if (elements.Current.Name != "key" || elements.Current.HasElements) return null;
            string name = elements.Current.Value;
            if (!elements.MoveNext()) return null;
            if (name != key) continue;
            if (result is not null) return null;
            result = elements.Current;
        }
        return result;
    }
}
#endif
