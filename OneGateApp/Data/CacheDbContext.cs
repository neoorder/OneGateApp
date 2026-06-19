using Microsoft.EntityFrameworkCore;
using NeoOrder.OneGate.Services;

namespace NeoOrder.OneGate.Data;

public partial class CacheDbContext(DbContextOptions<CacheDbContext> options) : DbContext(options)
{
    public DbSet<Setting> Settings { get; set; }
    public DbSet<Banner> Banners { get; set; }
    public DbSet<News> News { get; set; }
    public DbSet<DApp> DApps { get; set; }
}

static class CacheDbContextFactoryExtensions
{
    static readonly SemaphoreSlim InitializationLock = new(1, 1);

    public static async Task<CacheDbContext> CreateInitializedDbContextAsync(this IDbContextFactory<CacheDbContext> dbContextFactory)
    {
        await InitializationLock.WaitAsync();
        CacheDbContext? dbContext = null;
        try
        {
            string cacheDirectory = Path.GetDirectoryName(SharedOptions.CacheDbPath)!;
            Directory.CreateDirectory(cacheDirectory);
            dbContext = await dbContextFactory.CreateDbContextAsync();
            await dbContext.Database.EnsureCreatedAsync();
            return dbContext;
        }
        catch
        {
            dbContext?.Dispose();
            throw;
        }
        finally
        {
            InitializationLock.Release();
        }
    }

    public static async Task DeleteSettingIfExistsAsync(this IDbContextFactory<CacheDbContext> dbContextFactory, string key)
    {
        if (!File.Exists(SharedOptions.CacheDbPath)) return;
        using var dbContext = await dbContextFactory.CreateInitializedDbContextAsync();
        await dbContext.Settings.DeleteAsync(key);
    }
}
