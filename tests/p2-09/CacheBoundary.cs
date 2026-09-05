using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NeoOrder.OneGate.Data;
using System.Net;
using System.Text.Json;

// Actual cache, entities, SQL settings helpers and SQLite are used. Only the
// production on-disk database location and remote HTTP responses are replaced.
public sealed class CacheFixture : IDbContextFactory<CacheDbContext>, IDisposable
{
    readonly SqliteConnection connection = new("Data Source=:memory:");
    public ResponseHandler Handler { get; } = new();
    public HttpClient Client { get; }
    public CacheFixture()
    {
        connection.Open();
        using var db = CreateDbContext();
        db.Database.EnsureCreated();
        Client = new(Handler) { BaseAddress = new("https://example.com") };
    }
    public CacheDbContext CreateDbContext() => new(new DbContextOptionsBuilder<CacheDbContext>()
        .UseSqlite(connection).UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking).Options);
    public Task<CacheDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    public void Respond<T>(params T[] values) => Handler.Json = JsonSerializer.Serialize(values, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    public void Dispose() { Client.Dispose(); connection.Dispose(); }
}
public sealed class ResponseHandler : HttpMessageHandler
{
    public string Json { get; set; } = "[]";
    public int Requests { get; private set; }
    public Exception? Error { get; set; }
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++;
        if (Error is not null) throw Error;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Json) });
    }
}
namespace NeoOrder.OneGate.Data
{
    public class CacheDbContext(DbContextOptions<CacheDbContext> options) : DbContext(options)
    {
        public DbSet<Setting> Settings => Set<Setting>();
        public DbSet<Banner> Banners => Set<Banner>();
        public DbSet<News> News => Set<News>();
        public DbSet<VersionedItem> VersionedItems => Set<VersionedItem>();
    }
    public static class CacheDbContextFactoryExtensions
    {
        public static Task<CacheDbContext> CreateInitializedDbContextAsync(this IDbContextFactory<CacheDbContext> factory) => factory.CreateDbContextAsync();
    }
}
public class VersionedItem : NeoOrder.OneGate.Models.ICachedEntity, NeoOrder.OneGate.Models.IVersioned, IComparable<VersionedItem>
{
    public int Id { get; set; }
    public int Version { get; set; }
    public string Name { get; set; } = "";
    public int CompareTo(VersionedItem? other) => other is null ? 1 : Id.CompareTo(other.Id);
}
namespace NeoOrder.OneGate.Services
{
    public static class SharedOptions
    {
        public const string OneGateDomain = "example.com";
        public static JsonSerializerOptions JsonSerializerOptions { get; } = new(JsonSerializerDefaults.Web);
    }
}
