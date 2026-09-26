using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Pages;
using NeoOrder.OneGate.Services;
using Xunit;

public class SearchContentPolicyTests
{
    [Theory]
    [InlineData("asset")]
    [InlineData("catalog")]
    [InlineData("settings")]
    public async Task TightenedPreferencesRemoveOldResultsEvenWhenReloadFails(string failure)
    {
        var catalog = new CachedCollection<DApp>
        {
            new() { Name = "App safe" },
            new() { Name = "App restricted", Warnings = ContentWarnings.Violence },
            new() { Name = "App developing", IsInDevelopment = true }
        };
        var database = new ApplicationDbContext();
        database.Settings.Values[DAppCatalogPolicy.AllowRestrictedContentKey] = true;
        database.Settings.Values[DAppCatalogPolicy.DeveloperModeKey] = true;
        var tokens = new TokenManager();
        var page = new GlobalSearchPage(new TestServices(catalog), database, tokens);
        await page.LoadForTestAsync();
        page.ChangeQueryForTest("App"); page.Dispatcher.Flush();
        Assert.Equal(3, page.Results.Length);
        GlobalSearchResult stale = page.Results.Single(p => p.DApp!.IsInDevelopment);
        database.Settings.Values[DAppCatalogPolicy.AllowRestrictedContentKey] = false;
        database.Settings.Values[DAppCatalogPolicy.DeveloperModeKey] = false;
        if (failure == "asset") tokens.Load = () => throw new HttpRequestException("offline");
        if (failure == "catalog") catalog.Load = () => throw new HttpRequestException("offline");
        if (failure == "settings") database.Settings.Failure = new IOException("settings unavailable");
        await Assert.ThrowsAnyAsync<Exception>(() => page.LoadForTestAsync());
        Assert.Single(page.Results);
        Assert.Equal("App safe", page.Results[0].Title);
        Commands.LaunchDApp.Last = null;
        await page.OpenForTestAsync(stale);
        Assert.Null(Commands.LaunchDApp.Last);
    }

    [Fact]
    public async Task PreviousPrivilegedResultsAreHiddenBeforeSettingsReadCompletes()
    {
        var catalog = new CachedCollection<DApp> { new() { Name = "App developing", IsInDevelopment = true } };
        var database = new ApplicationDbContext();
        database.Settings.Values[DAppCatalogPolicy.DeveloperModeKey] = true;
        var page = new GlobalSearchPage(new TestServices(catalog), database, new());
        await page.LoadForTestAsync(); page.ChangeQueryForTest("App"); page.Dispatcher.Flush();
        Assert.Single(page.Results);
        var blocked = new TaskCompletionSource();
        database.Settings.BeforeRead = () => blocked.Task;
        Task load = page.LoadForTestAsync();
        try { Assert.Empty(page.Results); }
        finally { blocked.SetResult(); await load; }
    }

    [Theory]
    [InlineData(false, false, "App safe")]
    [InlineData(true, false, "App restricted,App safe")]
    [InlineData(false, true, "App developing,App safe")]
    [InlineData(true, true, "App developing,App restricted,App safe")]
    public async Task GlobalSearchUsesSameContentAndDiscoveryPolicyAsCatalog(bool restricted, bool developer, string expected)
    {
        var catalog = new CachedCollection<DApp>
        {
            new() { Name = "App safe" },
            new() { Name = "App restricted", Warnings = ContentWarnings.Violence },
            new() { Name = "App developing", IsInDevelopment = true },
            new() { Name = "App hidden", IsHiddenFromCatalog = true },
            new() { Name = "App unsupported", SupportedPlatforms = DAppPlatforms.iOS },
            new() { Name = "App game", IsRegularApp = false }
        };
        var database = new ApplicationDbContext();
        database.Settings.Values[DAppCatalogPolicy.AllowRestrictedContentKey] = restricted;
        database.Settings.Values[DAppCatalogPolicy.DeveloperModeKey] = developer;
        var page = new GlobalSearchPage(new TestServices(catalog), database, new());

        await page.LoadForTestAsync();
        page.ChangeQueryForTest("App");
        page.Dispatcher.Flush();

        Assert.Equal(expected, string.Join(',', page.Results.Select(item => item.Title)));
    }
}
