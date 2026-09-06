using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Pages;
using NeoOrder.OneGate.Services;
using Xunit;

public class OfflineCatalogTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BothPagesPresentASharedAlreadyLoadedCacheWhenOffline(bool gamesFirst)
    {
        var catalog = new CachedCollection<DApp>
        {
            new() { Id = 1, IsGamingApp = true }, new() { Id = 2, IsGamingApp = false }
        };
        catalog.Load = () => throw new HttpRequestException("Offline");
        int notifications = 0;
        catalog.CollectionLoaded += (_, _) => notifications++;
        var services = new TestServices(catalog);
        GamingPage gaming;
        DAppsPage apps;
        if (gamesFirst) { gaming = new(services, new()); apps = new(services, new()); }
        else { apps = new(services, new()); gaming = new(services, new()); }

        Assert.Equal(1, notifications);
        Assert.Equal(1, Assert.Single(gaming.GamesFiltered).Id);
        Assert.Equal(2, Assert.Single(apps.DAppsFiltered).Id);
        Assert.True(gaming.LoadingService.HasError && apps.LoadingService.HasError);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TighteningPreferencesImmediatelyFiltersAnOfflineLoadedPage(bool games)
    {
        var (catalog, database) = await PermissiveCatalog(games);
        object page = games ? new GamingPage(new TestServices(catalog), database) : new DAppsPage(new TestServices(catalog), database);
        Assert.Equal(3, Items(page).Length);
        await database.Settings.PutAsync(DAppCatalogPolicy.AllowRestrictedContentKey, false);
        await database.Settings.PutAsync(DAppCatalogPolicy.DeveloperModeKey, false);
        var response = new TaskCompletionSource();
        catalog.Load = () => response.Task;

        Loading(page).BeginLoad();

        Assert.Equal(1, Assert.Single(Items(page)).Id);
        Assert.Equal(1, Assert.Single(Recent(page)).Id);
        response.SetException(new HttpRequestException("Offline"));
        await Idle(Loading(page));
        Assert.True(Loading(page).HasError);
        Assert.Equal(1, Assert.Single(Items(page)).Id);
    }

    [Theory]
    [InlineData(true, DAppCatalogPolicy.AllowRestrictedContentKey)]
    [InlineData(false, DAppCatalogPolicy.AllowRestrictedContentKey)]
    [InlineData(true, DAppCatalogPolicy.DeveloperModeKey)]
    [InlineData(false, DAppCatalogPolicy.DeveloperModeKey)]
    public async Task SettingsFailureDoesNotReusePreviouslyPermissiveFlags(bool games, string failureKey)
    {
        var (catalog, database) = await PermissiveCatalog(games);
        object page = games ? new GamingPage(new TestServices(catalog), database) : new DAppsPage(new TestServices(catalog), database);
        Assert.Equal(3, Items(page).Length);
        var settings = new TaskCompletionSource();
        database.Settings.ReadGate = settings.Task;
        database.Settings.FailureKey = failureKey;

        Loading(page).BeginLoad();

        // A slow/failing preference read must not leave old forbidden cards tappable.
        Assert.Equal(1, Assert.Single(Items(page)).Id);
        settings.SetResult();
        await Idle(Loading(page));
        Assert.True(Loading(page).HasError);
        Assert.Equal(1, Assert.Single(Items(page)).Id);
        Assert.Equal(1, Assert.Single(Recent(page)).Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SettingsFailureStillLoadsDiskCatalogWithRestrictedDefaults(bool games)
    {
        var catalog = new CachedCollection<DApp>();
        catalog.Load = () =>
        {
            catalog.Add(new DApp { Id = 7, IsGamingApp = games });
            catalog.Add(new DApp { Id = 8, IsGamingApp = games, Warnings = (ContentWarnings)1 });
            return Task.CompletedTask;
        };
        var database = new ApplicationDbContext();
        database.Settings.FailureKey = DAppCatalogPolicy.AllowRestrictedContentKey;
        object page = games ? new GamingPage(new TestServices(catalog), database) : new DAppsPage(new TestServices(catalog), database);

        await Idle(Loading(page));

        Assert.True(Loading(page).HasError);
        Assert.Equal(7, Assert.Single(Items(page)).Id);
    }

    static async Task<(CachedCollection<DApp>, ApplicationDbContext)> PermissiveCatalog(bool games)
    {
        var catalog = new CachedCollection<DApp>
        {
            new() { Id = 1, IsGamingApp = games },
            new() { Id = 2, IsGamingApp = games, Warnings = (ContentWarnings)1 },
            new() { Id = 3, IsGamingApp = games, IsInDevelopment = true }
        };
        var database = new ApplicationDbContext();
        await database.Settings.PutAsync(DAppCatalogPolicy.AllowRestrictedContentKey, true);
        await database.Settings.PutAsync(DAppCatalogPolicy.DeveloperModeKey, true);
        await database.Settings.PutAsync("dapps/recent", new List<int> { 1, 2, 3 });
        return (catalog, database);
    }
    static DApp[] Items(object page) => page is GamingPage gaming ? gaming.GamesFiltered : ((DAppsPage)page).DAppsFiltered;
    static IEnumerable<DApp> Recent(object page) => page is GamingPage gaming ? gaming.GamesRecent : ((DAppsPage)page).DAppsRecent;
    static LoadingService Loading(object page) => page is GamingPage gaming ? gaming.LoadingService : ((DAppsPage)page).LoadingService;
    static async Task Idle(LoadingService loading)
    {
        for (int i = 0; loading.IsLoading && i < 100; i++) await Task.Delay(5);
        Assert.False(loading.IsLoading);
    }

    [Fact]
    public void GamesShowExpiredCachedItemsWhenNetworkRefreshFails()
    {
        var catalog = new CachedCollection<DApp> { new() { Id = 1, IsGamingApp = true } };
        catalog.Load = () => throw new HttpRequestException("Offline");

        var page = new GamingPage(new TestServices(catalog), new());

        Assert.Equal(1, Assert.Single(page.GamesFiltered).Id);
        Assert.False(page.LoadingService.IsLoading);
        Assert.True(page.LoadingService.HasError);
    }

    [Fact]
    public void AppsShowExpiredCachedItemsWhenNetworkRefreshFails()
    {
        var catalog = new CachedCollection<DApp> { new() { Id = 2, IsGamingApp = false } };
        catalog.Load = () => throw new HttpRequestException("Offline");

        var page = new DAppsPage(new TestServices(catalog), new());

        Assert.Equal(2, Assert.Single(page.DAppsFiltered).Id);
        Assert.False(page.LoadingService.IsLoading);
        Assert.True(page.LoadingService.HasError);
    }

    [Fact]
    public async Task CacheAppearsBeforeSlowNetworkRequestCompletes()
    {
        var request = new TaskCompletionSource();
        var catalog = new CachedCollection<DApp> { new() { Id = 3, IsGamingApp = true } };
        catalog.Load = () => request.Task;
        var page = new GamingPage(new TestServices(catalog), new());
        try { Assert.Single(page.GamesFiltered); }
        finally { request.SetResult(); await Task.Delay(20); }
    }

    [Fact]
    public void FailedEmptyCatalogAndSuccessfulEmptyCatalogHaveDifferentState()
    {
        var catalog = new CachedCollection<DApp> { Load = () => throw new HttpRequestException() };
        var page = new GamingPage(new TestServices(catalog), new());
        Assert.Empty(page.GamesFiltered);
        Assert.True(page.LoadingService.HasError);

        catalog.Load = () => Task.CompletedTask;
        page.LoadingService.BeginLoad();

        Assert.Empty(page.GamesFiltered);
        Assert.False(page.LoadingService.HasError);
    }
}
