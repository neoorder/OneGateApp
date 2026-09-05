using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Models;
using Xunit;

public class ForcedRefreshTests
{
    [Fact]
    public async Task AutomaticLoadHonorsTtlAndForceFetchesFreshResponse()
    {
        using var fixture = new CacheFixture();
        var cache = new CachedCollection<Banner>(fixture, fixture.Client);
        fixture.Respond(new Banner { Id = 1, ImageUrl = "https://example.com/1", TargetUrl = "https://example.com/1" });
        await cache.LoadAsync("/banners", TimeSpan.FromDays(1));
        fixture.Respond(new Banner { Id = 2, ImageUrl = "https://example.com/2", TargetUrl = "https://example.com/2" });
        await cache.LoadAsync("/banners", TimeSpan.FromDays(1));
        Assert.Equal(1, fixture.Handler.Requests);
        Assert.Equal(1, cache.Single().Id);

        await cache.LoadAsync("/banners", TimeSpan.FromDays(1), forceRefresh: true);

        Assert.Equal(2, fixture.Handler.Requests);
        Assert.Equal(2, cache.Single().Id);
    }

    [Fact]
    public async Task ForceBypassesFutureTimestampAndFailureDoesNotMarkFresh()
    {
        using var fixture = new CacheFixture();
        var future = DateTimeOffset.UtcNow.AddDays(3);
        using (var db = fixture.CreateDbContext()) await db.Settings.PutAsync("caching/last_update/banner", future);
        var cache = new CachedCollection<Banner>(fixture, fixture.Client);
        fixture.Handler.Error = new HttpRequestException("Offline");

        await Assert.ThrowsAsync<HttpRequestException>(() => cache.LoadAsync("/banners", TimeSpan.FromDays(1), forceRefresh: true));

        Assert.Equal(1, fixture.Handler.Requests);
        using var verify = fixture.CreateDbContext();
        Assert.Equal(future, await verify.Settings.GetAsync<DateTimeOffset>("caching/last_update/banner"));
    }
}
