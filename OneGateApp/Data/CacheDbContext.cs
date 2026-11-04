using Microsoft.EntityFrameworkCore;
using NeoOrder.OneGate.Services;

namespace NeoOrder.OneGate.Data;

public partial class CacheDbContext(DbContextOptions<CacheDbContext> options) : DbContext(options)
{
    public DbSet<CacheState> CacheStates { get; set; }
    public DbSet<Banner> Banners { get; set; }
    public DbSet<News> News { get; set; }
    public DbSet<DApp> DApps { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Keep database defaults for old rows while always writing explicitly supplied CLR values.
        var dApp = modelBuilder.Entity<DApp>();
        dApp.Property(p => p.Warnings).HasDefaultValue(ContentWarnings.None).ValueGeneratedNever();
        dApp.Property(p => p.IsHiddenFromCatalog).HasDefaultValue(false).ValueGeneratedNever();
        dApp.Property(p => p.IsInDevelopment).HasDefaultValue(false).ValueGeneratedNever();
        dApp.Property(p => p.SupportedPlatforms).HasDefaultValue(DAppPlatforms.All).ValueGeneratedNever();
    }
}

static class CacheDbContextFactoryExtensions
{
    public static async Task<CacheDbContext> CreateInitializedDbContextAsync(this IDbContextFactory<CacheDbContext> dbContextFactory)
    {
        CacheDbContext? dbContext = null;
        try
        {
            string cacheDirectory = Path.GetDirectoryName(SharedOptions.CacheDbPath)!;
            Directory.CreateDirectory(cacheDirectory);
            dbContext = await dbContextFactory.CreateDbContextAsync();
            await dbContext.EnsureSchemaAsync();
            return dbContext;
        }
        catch
        {
            dbContext?.Dispose();
            throw;
        }
    }
}
