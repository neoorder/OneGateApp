using NeoOrder.OneGate.Data;

namespace NeoOrder.OneGate.Services;

public sealed class GameDownloadStatusService(ApplicationDbContext dbContext)
{
    const string SettingsKey = "games/download-receipts";

    readonly SemaphoreSlim initializationLock = new(1, 1);
    readonly HashSet<int> downloadingGameIds = [];
    Dictionary<int, GameDownloadReceipt>? receipts;

    public async Task ApplyStatusesAsync(IEnumerable<DApp> games)
    {
        await EnsureInitializedAsync();
        foreach (DApp game in games)
            game.DownloadStatus = GetStatus(game);
    }

    public async Task MarkDownloadingAsync(DApp game)
    {
        if (!game.IsGamingApp || game.Id <= 0) return;

        await EnsureInitializedAsync();
        if (HasCurrentReceipt(game))
        {
            game.DownloadStatus = GameDownloadStatus.Downloaded;
            return;
        }

        downloadingGameIds.Add(game.Id);
        game.DownloadStatus = GameDownloadStatus.Downloading;
    }

    public async Task MarkDownloadedAsync(DApp game)
    {
        if (!game.IsGamingApp || game.Id <= 0) return;

        await EnsureInitializedAsync();
        bool receiptChanged = !HasCurrentReceipt(game);
        receipts![game.Id] = new(game.Version, game.Url);
        downloadingGameIds.Remove(game.Id);
        game.DownloadStatus = GameDownloadStatus.Downloaded;
        if (receiptChanged)
            await dbContext.Settings.PutAsync(SettingsKey, receipts);
    }

    public void ResetDownloading(DApp game)
    {
        if (receipts is null || !downloadingGameIds.Remove(game.Id)) return;
        game.DownloadStatus = GetStatus(game);
    }

    async Task EnsureInitializedAsync()
    {
        if (receipts is not null) return;

        await initializationLock.WaitAsync();
        try
        {
            receipts ??= await dbContext.Settings.GetAsync<Dictionary<int, GameDownloadReceipt>>(SettingsKey) ?? [];
        }
        finally
        {
            initializationLock.Release();
        }
    }

    GameDownloadStatus GetStatus(DApp game)
    {
        if (downloadingGameIds.Contains(game.Id))
            return GameDownloadStatus.Downloading;
        return HasCurrentReceipt(game)
            ? GameDownloadStatus.Downloaded
            : GameDownloadStatus.Required;
    }

    bool HasCurrentReceipt(DApp game)
    {
        return receipts!.TryGetValue(game.Id, out var receipt)
            && receipt.Version == game.Version
            && string.Equals(receipt.Url, game.Url, StringComparison.Ordinal);
    }

    sealed record GameDownloadReceipt(int Version, string Url);
}
