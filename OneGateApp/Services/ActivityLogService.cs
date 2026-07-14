using Neo;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NeoOrder.OneGate.Data;

namespace NeoOrder.OneGate.Services;

public class ActivityLogService(IServiceScopeFactory scopeFactory)
{
    const int MaxRecords = 50;

    public async Task<IReadOnlyList<ActivityRecord>> GetRecentAsync()
    {
        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            ApplicationDbContext dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            return await dbContext.ActivityRecords
                .Where(p => p.CreatedAt != default)
                .OrderByDescending(p => p.CreatedAt)
                .ThenByDescending(p => p.Id)
                .Take(MaxRecords)
                .ToArrayAsync();
        }
        catch
        {
            // Activity history is diagnostic. Corrupt local data must not break the app.
            return [];
        }
    }

    public async Task RecordDAppConnectionAsync(DApp dapp)
    {
        await RecordAsync(ActivityRecordKind.DAppConnection, dapp);
    }

    public async Task RecordWalletAuthorizationAsync(DApp dapp)
    {
        await RecordAsync(ActivityRecordKind.WalletAuthorization, dapp);
    }

    public async Task RecordSignatureAsync(DApp dapp)
    {
        await RecordAsync(ActivityRecordKind.Signature, dapp);
    }

    public async Task RecordTransactionAsync(DApp dapp, UInt256 transactionHash)
    {
        await RecordAsync(ActivityRecordKind.Transaction, dapp, transactionHash.ToString());
    }

    async Task RecordAsync(ActivityRecordKind kind, DApp dapp, string? transactionHash = null)
    {
        try
        {
            using IServiceScope scope = scopeFactory.CreateScope();
            ApplicationDbContext dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await dbContext.ActivityRecords.AddAsync(new()
            {
                Kind = kind,
                CreatedAt = DateTimeOffset.UtcNow,
                DAppId = dapp.Id > 0 ? dapp.Id : null,
                DAppName = dapp.NameLocalizer.Localize(),
                DAppHost = TryGetHost(dapp.Url),
                TransactionHash = transactionHash
            });
            await dbContext.SaveChangesAsync();

            int[] staleIds = await dbContext.ActivityRecords
                .OrderByDescending(p => p.CreatedAt)
                .ThenByDescending(p => p.Id)
                .Skip(MaxRecords)
                .Select(p => p.Id)
                .ToArrayAsync();
            if (staleIds.Length > 0)
                await dbContext.ActivityRecords.Where(p => staleIds.Contains(p.Id)).ExecuteDeleteAsync();
        }
        catch
        {
            // Activity logging is diagnostic and must never block wallet operations.
        }
    }

    static string? TryGetHost(string? url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            return uri.Host;
        return null;
    }
}
