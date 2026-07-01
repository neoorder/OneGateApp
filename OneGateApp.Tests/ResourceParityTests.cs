using System.Xml.Linq;

namespace OneGateApp.Tests;

public sealed class ResourceParityTests
{
    [Fact]
    public void LocalizedStringResourcesExposeSameKeysAsNeutralResource()
    {
        string resourceDirectory = Path.Combine(TestPaths.RepositoryRoot, "OneGateApp", "Properties");
        string neutralPath = Path.Combine(resourceDirectory, "Strings.resx");
        string[] localizedPaths = Directory.GetFiles(resourceDirectory, "Strings.*.resx");

        SortedSet<string> neutralKeys = LoadResourceKeys(neutralPath);
        Assert.NotEmpty(localizedPaths);

        foreach (string localizedPath in localizedPaths)
        {
            SortedSet<string> localizedKeys = LoadResourceKeys(localizedPath);
            string[] missing = neutralKeys.Except(localizedKeys).ToArray();
            string[] extra = localizedKeys.Except(neutralKeys).ToArray();

            Assert.True(missing.Length == 0 && extra.Length == 0,
                $"{Path.GetFileName(localizedPath)} resource keys differ. Missing: {string.Join(", ", missing)}. Extra: {string.Join(", ", extra)}.");
        }
    }

    static SortedSet<string> LoadResourceKeys(string path)
    {
        XDocument document = XDocument.Load(path);
        IEnumerable<string> keys = document.Root!
            .Elements("data")
            .Select(element => element.Attribute("name")?.Value)
            .Where(name => !string.IsNullOrWhiteSpace(name))!;
        return new SortedSet<string>(keys, StringComparer.Ordinal);
    }
}
