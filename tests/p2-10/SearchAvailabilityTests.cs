using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Pages;
using NeoOrder.OneGate.Services;
using Xunit;

public class SearchAvailabilityTests
{
    [Fact]
    public async Task SettingsFailureDropsOldAppIndexAndRetryRebuildsWithCurrentPolicy()
    {
        var catalog = new CachedCollection<DApp> { new() { Name = "Find developer", IsInDevelopment = true }, new() { Name = "Find safe" } };
        var database = new ApplicationDbContext();
        database.Settings.Values["developer"] = true;
        database.Contacts.Add(new() { Label = "Find contact" });
        var page = new GlobalSearchPage(new TestServices(catalog), database, new());
        page.ChangeQueryForTest("Find");
        await page.LoadForTestAsync();
        Assert.Equal(3, page.Results.Length);
        GlobalSearchResult stale = page.Results.Single(p => p.DApp?.IsInDevelopment == true);
        database.Settings.Failure = new IOException("settings unavailable");
        await page.LoadForTestAsync();
        Assert.DoesNotContain(page.Results, p => p.Type == GlobalSearchResultType.DApp);
        Assert.Contains(page.Results, p => p.Type == GlobalSearchResultType.Contact);
        Assert.True(page.HasSearchErrors);
        Commands.LaunchDApp.Last = null;
        await page.OpenForTestAsync(stale);
        Assert.Null(Commands.LaunchDApp.Last);
        database.Settings.Failure = null;
        database.Settings.Values["developer"] = false;
        await page.LoadForTestAsync();
        Assert.False(page.HasSearchErrors);
        Assert.Equal(2, page.Results.Length);
        Assert.DoesNotContain(page.Results, p => p.Title == "Find developer");
    }

    [Fact]
    public async Task SettingsPendingCannotExposePreviousDeveloperIndex()
    {
        var catalog = new CachedCollection<DApp> { new() { Name = "Find developer", IsInDevelopment = true } };
        var database = new ApplicationDbContext();
        database.Settings.Values["developer"] = true;
        var page = new GlobalSearchPage(new TestServices(catalog), database, new());
        page.ChangeQueryForTest("Find"); await page.LoadForTestAsync(); Assert.Single(page.Results);
        var blocked = new TaskCompletionSource(); database.Settings.BeforeRead = () => blocked.Task;
        Task load = page.LoadForTestAsync();
        try { Assert.Empty(page.Results); }
        finally { blocked.SetResult(); await load; }
    }

    [Fact]
    public async Task AssetFailureDoesNotHideLocalContactsOrApplications()
    {
        var catalog = new CachedCollection<DApp> { new() { Name = "Find application" } };
        var database = new ApplicationDbContext();
        database.Contacts.Add(new() { Label = "Find contact" });
        var tokens = new TokenManager { Load = () => throw new HttpRequestException("RPC unavailable") };
        var page = new GlobalSearchPage(new TestServices(catalog), database, tokens);
        page.ChangeQueryForTest("Find");

        await page.LoadForTestAsync();

        Assert.Equal(2, page.Results.Length);
        Assert.Contains(page.Results, result => result.Type == GlobalSearchResultType.Contact);
        Assert.Contains(page.Results, result => result.Type == GlobalSearchResultType.DApp);
    }

    [Fact]
    public async Task LocalResultsAppearWhileAssetRequestIsStillPending()
    {
        var pendingAssets = new TaskCompletionSource<IReadOnlyList<AssetInfo>>();
        var catalog = new CachedCollection<DApp> { new() { Name = "Find application" } };
        var database = new ApplicationDbContext();
        database.Contacts.Add(new() { Label = "Find contact" });
        var page = new GlobalSearchPage(new TestServices(catalog), database, new() { Load = () => pendingAssets.Task });
        page.ChangeQueryForTest("Find");

        Task load = page.LoadForTestAsync();
        try
        {
            Assert.Contains(page.Results, result => result.Type == GlobalSearchResultType.Contact);
            Assert.Contains(page.Results, result => result.Type == GlobalSearchResultType.DApp);
        }
        finally
        {
            pendingAssets.SetResult([]);
            await load;
        }
    }

    [Fact]
    public async Task CatalogFailureRetainsCachedDAppsAndOtherGroups()
    {
        var catalog = new CachedCollection<DApp> { new() { Name = "Find cached application" } };
        catalog.Load = () => throw new HttpRequestException("Catalog unavailable");
        var page = new GlobalSearchPage(new TestServices(catalog), new(), new() { Load = () => Task.FromResult<IReadOnlyList<AssetInfo>>([new()]) });

        await page.LoadForTestAsync();
        page.ChangeQueryForTest("Find");
        page.Dispatcher.Flush();
        Assert.Single(page.Results);
        page.ChangeQueryForTest("NEO");
        page.Dispatcher.Flush();
        Assert.Single(page.Results);
    }

    [Fact]
    public async Task FailedGroupsDoNotPretendToBeAnEmptySuccessfulSearch()
    {
        var catalog = new CachedCollection<DApp> { Load = () => throw new HttpRequestException() };
        var page = new GlobalSearchPage(new TestServices(catalog), new(), new() { Load = () => throw new HttpRequestException() });
        page.ChangeQueryForTest("anything");

        await page.LoadForTestAsync();

        Assert.Empty(page.Results);
        Assert.False(page.IsEmpty);
        Assert.True(page.HasSearchErrors);
    }

    [Fact]
    public async Task ConcurrentGroupFailuresKeepEachErrorMessage()
    {
        var catalog = new CachedCollection<DApp> { Load = () => throw new HttpRequestException() };
        var tokens = new TokenManager { Load = () => throw new HttpRequestException() };
        var page = new GlobalSearchPage(new TestServices(catalog), new(), tokens);
        page.ChangeQueryForTest("anything");

        await page.LoadForTestAsync();

        Assert.Contains("Asset: Unavailable", page.SearchErrorText);
        Assert.Contains("Apps: Unavailable", page.SearchErrorText);
    }

    [Fact]
    public async Task RetryClearsErrorsAndSuccessfulNoMatchesShowsEmptyState()
    {
        var tokens = new TokenManager { Load = () => throw new HttpRequestException() };
        var page = new GlobalSearchPage(new TestServices(new()), new(), tokens);
        page.ChangeQueryForTest("missing");
        await page.LoadForTestAsync();
        Assert.True(page.HasSearchErrors);

        tokens.Load = () => Task.FromResult<IReadOnlyList<AssetInfo>>([]);
        await page.LoadForTestAsync();

        Assert.False(page.HasSearchErrors);
        Assert.True(page.IsEmpty);
    }
}
