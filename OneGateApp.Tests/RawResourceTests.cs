using System.Text.Json;
using System.Text.RegularExpressions;

namespace OneGateApp.Tests;

public sealed partial class RawResourceTests
{
    [GeneratedRegex("^0x[0-9a-fA-F]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex UInt160HashPattern();

    [Theory]
    [InlineData("protocol.json")]
    [InlineData("tokens.json")]
    [InlineData("nft.json")]
    public void RawJsonResourcesAreValidJson(string fileName)
    {
        using JsonDocument document = LoadRawJson(fileName);

        Assert.NotEqual(JsonValueKind.Undefined, document.RootElement.ValueKind);
    }

    [Fact]
    public void ProtocolResourceContainsExpectedNeoMainnetConfiguration()
    {
        using JsonDocument document = LoadRawJson("protocol.json");
        JsonElement configuration = document.RootElement.GetProperty("ProtocolConfiguration");

        Assert.Equal(860833102, configuration.GetProperty("Network").GetInt32());
        Assert.Equal(53, configuration.GetProperty("AddressVersion").GetInt32());
        Assert.True(configuration.GetProperty("MillisecondsPerBlock").GetInt32() > 0);
        Assert.True(configuration.GetProperty("StandbyCommittee").GetArrayLength() >= 7);
        Assert.True(configuration.GetProperty("SeedList").GetArrayLength() > 0);
    }

    [Theory]
    [InlineData("tokens.json", true)]
    [InlineData("nft.json", false)]
    public void TokenCatalogEntriesHaveRequiredFields(string fileName, bool requiresDecimals)
    {
        using JsonDocument document = LoadRawJson(fileName);
        JsonElement catalog = document.RootElement;

        Assert.Equal(JsonValueKind.Array, catalog.ValueKind);
        Assert.True(catalog.GetArrayLength() > 0);

        foreach (JsonElement token in catalog.EnumerateArray())
        {
            string hash = token.GetProperty("hash").GetString()!;
            string name = token.GetProperty("name").GetString()!;
            string symbol = token.GetProperty("symbol").GetString()!;

            Assert.Matches(UInt160HashPattern(), hash);
            Assert.False(string.IsNullOrWhiteSpace(name));
            Assert.False(string.IsNullOrWhiteSpace(symbol));

            if (requiresDecimals)
            {
                int decimals = token.GetProperty("decimals").GetInt32();
                Assert.InRange(decimals, 0, 18);
            }
        }
    }

    static JsonDocument LoadRawJson(string fileName)
    {
        string path = Path.Combine(TestPaths.RepositoryRoot, "OneGateApp", "Resources", "Raw", fileName);
        return JsonDocument.Parse(File.ReadAllText(path));
    }
}
