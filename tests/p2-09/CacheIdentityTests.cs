using Microsoft.EntityFrameworkCore;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Models;
using Xunit;

public class CacheIdentityTests
{
    static News Article(int id, DateTimeOffset published, string title) => new()
    {
        Id = id, PublishDate = published, Title = title, Category = "Test", Language = "en",
        Guid = id.ToString(), Url = "https://example.com/" + id, Authors = "Author", Keywords = [], Summary = title, Content = title
    };

    [Fact]
    public async Task DifferentNewsIdsWithIdenticalPublicationTimeSurviveRefreshAndRestart()
    {
        using var fixture = new CacheFixture();
        var published = DateTimeOffset.UtcNow;
        fixture.Respond(Article(1, published, "First"), Article(2, published, "Second"));
        var news = new CachedCollection<News>(fixture, fixture.Client);

        await news.LoadAsync("/news", TimeSpan.Zero);

        Assert.Equal(2, news.Count);
        using (var db = fixture.CreateDbContext()) Assert.Equal(2, await db.News.CountAsync());
        var restarted = new CachedCollection<News>(fixture, fixture.Client);
        await restarted.LoadAsync("/news", TimeSpan.FromDays(1));
        Assert.Equal([1, 2], restarted.Select(item => item.Id).Order().ToArray());
    }

    [Fact]
    public async Task SameIdNewsEditsReplaceContentAndRepositionWhenPublicationTimeChanges()
    {
        using var fixture = new CacheFixture();
        var earlier = DateTimeOffset.UtcNow.AddHours(-1);
        var news = new CachedCollection<News>(fixture, fixture.Client);
        fixture.Respond(Article(1, earlier, "Old"), Article(2, earlier.AddMinutes(30), "Second"));
        await news.LoadAsync("/news", TimeSpan.Zero);
        fixture.Respond(Article(1, earlier.AddHours(1), "Corrected"), Article(2, earlier.AddMinutes(30), "Second"));

        await news.LoadAsync("/news", TimeSpan.Zero);

        Assert.Equal(2, news.Count);
        Assert.Equal(1, news[0].Id);
        Assert.Equal("Corrected", news[0].Content);
        using var db = fixture.CreateDbContext();
        Assert.Equal("Corrected", (await db.News.SingleAsync(item => item.Id == 1)).Title);
    }

    [Fact]
    public async Task SameTimeNewsCorrectionsAndBannerLinkEditsArePersisted()
    {
        using var fixture = new CacheFixture();
        var published = DateTimeOffset.UtcNow;
        var news = new CachedCollection<News>(fixture, fixture.Client);
        fixture.Respond(Article(1, published, "Old"));
        await news.LoadAsync("/news", TimeSpan.Zero);
        fixture.Respond(Article(1, published, "Corrected"));
        await news.LoadAsync("/news", TimeSpan.Zero);
        Assert.Equal("Corrected", news.Single().Title);

        var banners = new CachedCollection<Banner>(fixture, fixture.Client);
        fixture.Respond(new Banner { Id = 1, ImageUrl = "https://example.com/old.png", TargetUrl = "https://example.com/old" });
        await banners.LoadAsync("/banners", TimeSpan.Zero);
        fixture.Respond(new Banner { Id = 1, ImageUrl = "https://example.com/new.png", TargetUrl = "https://example.com/new" });
        await banners.LoadAsync("/banners", TimeSpan.Zero);
        Assert.EndsWith("/new", banners.Single().TargetUrl);
        using var db = fixture.CreateDbContext();
        Assert.Equal("Corrected", (await db.News.SingleAsync()).Title);
        Assert.EndsWith("/new.png", (await db.Banners.SingleAsync()).ImageUrl);
    }

    [Fact]
    public async Task RemovingOneOfTwoSameTimeArticlesDoesNotRemoveTheOther()
    {
        using var fixture = new CacheFixture();
        var published = DateTimeOffset.UtcNow;
        using (var db = fixture.CreateDbContext())
        {
            db.News.AddRange(Article(1, published, "First"), Article(2, published, "Second"));
            await db.SaveChangesAsync();
        }
        fixture.Respond(Article(2, published, "Second updated"), Article(3, published, "Third"));
        var news = new CachedCollection<News>(fixture, fixture.Client);

        await news.LoadAsync("/news", TimeSpan.Zero);

        Assert.Equal([2, 3], news.Select(item => item.Id).Order().ToArray());
        using var verify = fixture.CreateDbContext();
        int[] persistedIds = await verify.News.OrderBy(item => item.Id).Select(item => item.Id).ToArrayAsync();
        Assert.Equal([2, 3], persistedIds);
    }

    [Fact]
    public async Task VersionedEntitiesRetainSameVersionAndReplaceNewVersion()
    {
        using var fixture = new CacheFixture();
        var cache = new CachedCollection<VersionedItem>(fixture, fixture.Client);
        fixture.Respond(new VersionedItem { Id = 1, Version = 1, Name = "First" });
        await cache.LoadAsync("/versioned", TimeSpan.Zero);
        var original = cache.Single();
        fixture.Respond(new VersionedItem { Id = 1, Version = 1, Name = "Same version" });
        await cache.LoadAsync("/versioned", TimeSpan.Zero);
        Assert.Same(original, cache.Single());
        fixture.Respond(new VersionedItem { Id = 1, Version = 2, Name = "New version" });
        await cache.LoadAsync("/versioned", TimeSpan.Zero);
        Assert.Equal("New version", cache.Single().Name);
        using var db = fixture.CreateDbContext();
        Assert.Equal(2, (await db.VersionedItems.SingleAsync()).Version);
    }

    [Fact]
    public async Task DuplicateRemoteIdsDoNotPartiallyMutateCache()
    {
        using var fixture = new CacheFixture();
        var published = DateTimeOffset.UtcNow;
        var news = new CachedCollection<News>(fixture, fixture.Client);
        fixture.Respond(Article(1, published, "Original"));
        await news.LoadAsync("/news", TimeSpan.Zero);
        fixture.Respond(Article(2, published, "Two"), Article(2, published.AddHours(1), "Duplicate"));

        await Assert.ThrowsAsync<ArgumentException>(() => news.LoadAsync("/news", TimeSpan.Zero));

        Assert.Equal("Original", news.Single().Title);
        using var db = fixture.CreateDbContext();
        Assert.Equal("Original", (await db.News.SingleAsync()).Title);
    }

    [Fact]
    public async Task CacheDatabaseRecreationIsSeededFromRefreshedEntities()
    {
        using var fixture = new CacheFixture();
        var cache = new CachedCollection<Banner>(fixture, fixture.Client);
        fixture.Respond(new Banner { Id = 1, ImageUrl = "https://example.com/1", TargetUrl = "https://example.com/1" });
        await cache.LoadAsync("/banners", TimeSpan.Zero);
        using (var db = fixture.CreateDbContext())
        {
            await db.Database.EnsureDeletedAsync();
            await db.Database.EnsureCreatedAsync();
        }

        await cache.LoadAsync("/banners", TimeSpan.Zero);

        using var verify = fixture.CreateDbContext();
        Assert.Single(cache);
        Assert.Equal(1, await verify.Banners.CountAsync());
    }
}
