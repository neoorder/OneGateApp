using Meshline.Models.Client;
using Microsoft.EntityFrameworkCore;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Properties;
using System.Diagnostics;
using System.Text.Json;

namespace NeoOrder.OneGate.Services.Messaging;

public sealed partial class MessagingService
{
    readonly object draftsLock = new();
    // Keep editing in memory; persist changed conversations in the background.
    readonly Dictionary<(string AccountId, string Address), StoredChatDraft> chatDrafts = [];

    public (string Text, MessageRef? ReplyTo) GetChatDraft(string localAccountId, string address)
    {
        lock (draftsLock)
            return chatDrafts.TryGetValue((localAccountId, address.Trim()), out var draft) ? (draft.Text, draft.ReplyTo) : ("", null);
    }

    public DateTimeOffset? GetChatDraftUpdatedAt(string localAccountId, string address)
    {
        lock (draftsLock)
            return chatDrafts.GetValueOrDefault((localAccountId, address.Trim()))?.UpdatedAt;
    }

    Dictionary<string, (string Text, DateTimeOffset UpdatedAt)> GetChatDrafts(string localAccountId)
    {
        lock (draftsLock)
            return chatDrafts.Where(p => p.Key.AccountId == localAccountId
                    && (!string.IsNullOrWhiteSpace(p.Value.Text) || p.Value.ReplyTo is not null))
                .ToDictionary(p => p.Key.Address,
                    p => (string.IsNullOrWhiteSpace(p.Value.Text) ? Strings.ChatQuotedMessage : p.Value.Text, p.Value.UpdatedAt),
                    StringComparer.Ordinal);
    }

    public void SetChatDraft(string localAccountId, string address, string text, MessageRef? replyTo = null)
    {
        lock (draftsLock)
        {
            var key = (localAccountId, address.Trim());
            if (text.Length == 0 && replyTo is null)
            {
                if (!chatDrafts.Remove(key)) return;
                pendingDraftWrites[key] = null;
            }
            else
            {
                if (chatDrafts.TryGetValue(key, out var draft) && draft.Text == text && draft.ReplyTo == replyTo) return;
                var updated = new StoredChatDraft(text, DateTimeOffset.Now, replyTo);
                chatDrafts[key] = updated;
                pendingDraftWrites[key] = updated;
            }
            ScheduleDraftSave();
        }
    }

    sealed record StoredChatDraft(string Text, DateTimeOffset UpdatedAt, MessageRef? ReplyTo = null);

    readonly SemaphoreSlim draftStorageLock = new(1, 1);
    readonly HashSet<string> loadedDraftAccounts = [];
    readonly HashSet<string> draftLoadErrors = [];
    readonly HashSet<string> draftSaveErrors = [];
    readonly Dictionary<(string AccountId, string Address), StoredChatDraft?> pendingDraftWrites = [];
    CancellationTokenSource? draftSaveDelay;

    public event EventHandler? DraftStorageChanged;

    public bool HasDraftStorageError(string localAccountId)
    {
        lock (draftsLock) return draftLoadErrors.Contains(localAccountId) || draftSaveErrors.Contains(localAccountId);
    }

    public async Task RetryChatDraftStorageAsync(string localAccountId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Commit local edits/deletions before retrying restoration. Canceling the wait must not discard a save.
        await FlushChatDraftsAsync().WaitAsync(cancellationToken);
        await Task.Run(() => LoadChatDraftsAsync(localAccountId, cancellationToken, force: true), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        NotifyChanged();
    }

    string GetDraftKeyPrefix(string accountId) => $"chat/drafts/{registry.Context.Registry}/{accountId}/";

    async Task LoadChatDraftsAsync(string accountId, CancellationToken cancellationToken, bool force = false)
    {
        lock (draftsLock)
            if (!force && loadedDraftAccounts.Contains(accountId)) return;
        await draftStorageLock.WaitAsync(cancellationToken);
        try
        {
            lock (draftsLock)
                if (!force && loadedDraftAccounts.Contains(accountId)) return;
            using var scope = scopeFactory.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            string prefix = GetDraftKeyPrefix(accountId);
            var rows = await database.Settings.AsNoTracking().Where(p => p.Key.StartsWith(prefix)).ToArrayAsync(cancellationToken);
            var drafts = new Dictionary<string, StoredChatDraft>(StringComparer.Ordinal);
            bool invalidDrafts = false;
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var draft = JsonSerializer.Deserialize<StoredChatDraft>(row.Value ?? "null");
                    if (draft is { Text: not null } && draft.UpdatedAt != default)
                        drafts[row.Key[prefix.Length..]] = draft;
                    else
                    {
                        Debug.WriteLine("[Meshline] Stored draft has missing or invalid fields.");
                        invalidDrafts = true;
                    }
                }
                catch (Exception ex) when (ex is JsonException or ArgumentException or NotSupportedException)
                {
                    Debug.WriteLine($"[Meshline] Stored draft could not be read: {ex}");
                    invalidDrafts = true;
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            lock (draftsLock)
                foreach (var (address, draft) in drafts)
                {
                    var key = (accountId, address);
                    // An edit or deletion made while reading always wins over the stored snapshot.
                    if (!chatDrafts.ContainsKey(key) && !pendingDraftWrites.ContainsKey(key))
                        chatDrafts[key] = draft;
                }
            // Keep valid drafts usable without repeatedly reading a damaged row on every chat operation.
            // Explicit retry can reread the snapshot; malformed records remain untouched on disk.
            lock (draftsLock) loadedDraftAccounts.Add(accountId);
            SetDraftStorageError(accountId, loading: true, failed: invalidDrafts);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Meshline] Draft loading failed: {ex}");
            // Leave both disk and in-memory drafts intact; a later opening can retry the read.
            SetDraftStorageError(accountId, loading: true, failed: true);
        }
        finally { draftStorageLock.Release(); }
    }

    // Called under draftsLock. Debounce keystrokes without delaying editor updates.
    void ScheduleDraftSave()
    {
        draftSaveDelay?.Cancel();
        draftSaveDelay?.Dispose();
        draftSaveDelay = new();
        var token = draftSaveDelay.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(350, token);
                await SaveChatDraftsAsync();
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        });
    }

    public Task FlushChatDraftsAsync()
    {
        lock (draftsLock)
        {
            draftSaveDelay?.Cancel();
            draftSaveDelay?.Dispose();
            draftSaveDelay = null;
            return pendingDraftWrites.Count == 0 ? Task.CompletedTask : Task.Run(SaveChatDraftsAsync);
        }
    }

    async Task SaveChatDraftsAsync()
    {
        await draftStorageLock.WaitAsync();
        Dictionary<(string AccountId, string Address), StoredChatDraft?> snapshot;
        lock (draftsLock) snapshot = new(pendingDraftWrites);
        try
        {
            if (snapshot.Count == 0) return;
            using var scope = scopeFactory.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync();
            foreach (var (key, draft) in snapshot)
            {
                string storageKey = GetDraftKeyPrefix(key.AccountId) + key.Address;
                if (draft is null) await database.Settings.DeleteAsync(storageKey);
                else await database.Settings.PutAsync(storageKey, JsonSerializer.Serialize(draft));
            }
            await transaction.CommitAsync();
            lock (draftsLock)
                foreach (var (key, draft) in snapshot)
                    if (pendingDraftWrites.TryGetValue(key, out var current) && current == draft)
                        pendingDraftWrites.Remove(key);
            foreach (string accountId in snapshot.Keys.Select(p => p.AccountId).Distinct())
                SetDraftStorageError(accountId, loading: false, failed: false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Meshline] Draft saving failed: {ex}");
            // Keep dirty entries for the next edit, navigation or app lifecycle save attempt.
            foreach (string accountId in snapshot.Keys.Select(p => p.AccountId).Distinct())
                SetDraftStorageError(accountId, loading: false, failed: true);
        }
        finally { draftStorageLock.Release(); }
    }

    void SetDraftStorageError(string accountId, bool loading, bool failed)
    {
        lock (draftsLock)
        {
            var errors = loading ? draftLoadErrors : draftSaveErrors;
            if (!(failed ? errors.Add(accountId) : errors.Remove(accountId))) return;
        }
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (isForeground && !disposed) DraftStorageChanged?.Invoke(this, EventArgs.Empty);
        });
    }
}
