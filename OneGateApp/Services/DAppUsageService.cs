using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeoOrder.OneGate.Data;

namespace NeoOrder.OneGate.Services;

public sealed class DAppUsageService(IServiceScopeFactory scopeFactory)
{
    const int MaxRecentRecordsPerCategory = 10;
    readonly SemaphoreSlim writeLock = new(1, 1);

    public async Task<List<int>> GetFavoriteIdsAsync(CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await database.DAppFavorites
            .OrderByDescending(p => p.CreatedAt)
            .ThenBy(p => p.DAppId)
            .Select(p => p.DAppId)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> IsFavoriteAsync(int dAppId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await database.DAppFavorites.AnyAsync(p => p.DAppId == dAppId, cancellationToken);
    }

    public async Task<bool> ToggleFavoriteAsync(int dAppId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dAppId);
        await writeLock.WaitAsync(cancellationToken);
        try
        {
            using var scope = scopeFactory.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (await database.DAppFavorites.Where(p => p.DAppId == dAppId).ExecuteDeleteAsync(cancellationToken) > 0)
                return false;
            database.DAppFavorites.Add(new() { DAppId = dAppId, CreatedAt = DateTime.UtcNow });
            await database.SaveChangesAsync(cancellationToken);
            return true;
        }
        finally { writeLock.Release(); }
    }

    public async Task<List<int>> GetRecentIdsAsync(bool isGame, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await database.DAppRecentVisits
            .Where(p => p.IsGame == isGame)
            .OrderByDescending(p => p.LastOpenedAt)
            .ThenBy(p => p.DAppId)
            .Select(p => p.DAppId)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> IsRecentAsync(int dAppId, CancellationToken cancellationToken = default)
    {
        using var scope = scopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await database.DAppRecentVisits.AnyAsync(p => p.DAppId == dAppId, cancellationToken);
    }

    public async Task RecordOpenedAsync(int dAppId, bool isGame, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(dAppId);
        await writeLock.WaitAsync(cancellationToken);
        try
        {
            using var scope = scopeFactory.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
            await database.Database.ExecuteSqlAsync($"""
                INSERT INTO [DAppRecentVisits]([DAppId], [IsGame], [LastOpenedAt])
                VALUES({dAppId}, {isGame}, {DateTime.UtcNow})
                ON CONFLICT([DAppId]) DO UPDATE SET
                    [IsGame] = excluded.[IsGame], [LastOpenedAt] = excluded.[LastOpenedAt]
                """, cancellationToken);
            await database.DAppRecentVisits
                .Where(p => p.IsGame == isGame)
                .OrderByDescending(p => p.LastOpenedAt)
                .ThenBy(p => p.DAppId)
                .Skip(MaxRecentRecordsPerCategory)
                .ExecuteDeleteAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        finally { writeLock.Release(); }
    }
}
