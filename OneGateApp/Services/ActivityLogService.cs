using Neo;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Models;

namespace NeoOrder.OneGate.Services;

public class ActivityLogService(ApplicationDbContext dbContext)
{
    const string RecordsKey = "activity/records";
    const int MaxRecords = 50;

    public async Task<IReadOnlyList<ActivityRecord>> GetRecentAsync()
    {
        try
        {
            List<ActivityRecord>? records = await dbContext.Settings.GetAsync<List<ActivityRecord>>(RecordsKey);
            return records?
                .Where(p => p.CreatedAt != default)
                .OrderByDescending(p => p.CreatedAt)
                .Take(MaxRecords)
                .ToArray() ?? [];
        }
        catch
        {
            // Activity history is diagnostic. Corrupt local data must not break Settings.
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
        ActivityRecordKind kind = IsOneGateVault(dapp) ? ActivityRecordKind.OneGateVaultTransaction : ActivityRecordKind.Transaction;
        await RecordAsync(kind, dapp, transactionHash.ToString());
    }

    async Task RecordAsync(ActivityRecordKind kind, DApp dapp, string? transactionHash = null)
    {
        try
        {
            List<ActivityRecord> records = await dbContext.Settings.GetAsync<List<ActivityRecord>>(RecordsKey) ?? [];
            records.Insert(0, new()
            {
                Kind = kind,
                CreatedAt = DateTimeOffset.UtcNow,
                DAppId = dapp.Id > 0 ? dapp.Id : null,
                DAppName = dapp.NameLocalizer.Localize(),
                DAppHost = TryGetHost(dapp.Url),
                TransactionHash = transactionHash
            });
            if (records.Count > MaxRecords)
                records.RemoveRange(MaxRecords, records.Count - MaxRecords);
            await dbContext.Settings.PutAsync(RecordsKey, records);
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

    static bool IsOneGateVault(DApp dapp)
    {
        if (dapp.Url.Contains("miniapp-gas-lucky-pool", StringComparison.OrdinalIgnoreCase))
            return true;
        return dapp.NameLocalizer.Values.Any(p => p.Contains("OneGate Vault", StringComparison.OrdinalIgnoreCase));
    }
}
