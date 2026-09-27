using System.Runtime.CompilerServices;
using System.Xml.Linq;
using Xunit;

public class NftPagingLocalizationTests
{
    [Theory]
    [InlineData("de")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("id")]
    [InlineData("it")]
    [InlineData("ja")]
    [InlineData("ko")]
    [InlineData("nl")]
    [InlineData("pt-BR")]
    [InlineData("ru")]
    [InlineData("tr")]
    [InlineData("vi")]
    [InlineData("zh-Hant")]
    public void ExistingLocaleDefinesBothNftPagingStringsWithoutEnglishFallback(string culture)
    {
        string resources = Path.GetFullPath(Path.Combine(TestDirectory(), "../../OneGateApp/Properties"));
        XDocument neutral = XDocument.Load(Path.Combine(resources, "Strings.resx"));
        XDocument translated = XDocument.Load(Path.Combine(resources, $"Strings.{culture}.resx"));
        foreach (string key in new[] { "LoadMoreNFTs", "NFTLoadFailed" })
        {
            XElement entry = Assert.Single(translated.Root!.Elements("data"), p => (string?)p.Attribute("name") == key);
            string? value = (string?)entry.Element("value");
            Assert.False(string.IsNullOrWhiteSpace(value));
            string? english = (string?)neutral.Root!.Elements("data").Single(p => (string?)p.Attribute("name") == key).Element("value");
            Assert.NotEqual(english, value);
        }
    }

    static string TestDirectory([CallerFilePath] string source = "") => Path.GetDirectoryName(source)!;
}
