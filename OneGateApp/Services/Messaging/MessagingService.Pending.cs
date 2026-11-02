using Meshline.Models.Client;
using Microsoft.EntityFrameworkCore;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Properties;
using System.Text.Json;

namespace NeoOrder.OneGate.Services.Messaging;

public sealed partial class MessagingService
{
    readonly object pendingLock = new();
    readonly Dictionary<Guid, PendingChatMessage> pendingMessages = [];
    readonly Dictionary<(string AccountId, string Address), SemaphoreSlim> submissionLocks = [];

    SemaphoreSlim GetSubmissionLock(string accountId, string address)
    {
        lock (pendingLock)
        {
            var key = (accountId, address.Trim());
            if (!submissionLocks.TryGetValue(key, out var gate)) submissionLocks[key] = gate = new(1, 1);
            return gate;
        }
    }

    public void QueueTextMessage(string localAccountId, string address, string text, MessageRef? replyTo = null)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException(Strings.ChatMessageEmpty, nameof(text));
        ValidatePendingAccount(localAccountId);
        string recipient = GetContactAccountId(address);
        if (replyTo is not null && replyTo.Sender != localAccountId && replyTo.Sender != recipient)
            throw new InvalidOperationException(Strings.ChatQuotedMessageUnavailable);
        var message = new PendingChatMessage(Guid.NewGuid(), localAccountId, address.Trim(), text, DateTimeOffset.Now)
        {
            ReplyTo = replyTo
        };
        lock (pendingLock)
        {
            pendingMessages.Add(message.Id, message);
            SubmitPendingMessage(message);
        }
        NotifyChanged();
    }

    public void RetryPendingMessage(Guid id)
    {
        lock (pendingLock)
        {
            if (!pendingMessages.TryGetValue(id, out var message) || message.ErrorMessage is null || message.SendStatus is not null) return;
            ValidatePendingAccount(message.LocalAccountId);
            message = message with { ErrorMessage = null, HasSubmissionStarted = false };
            pendingMessages[id] = message;
            // Remove the old failure durably before a retry can enter the SDK outbox.
            SubmitPendingMessage(message, QueuePendingMessageStorage(message, remove: true));
        }
        NotifyChanged();
    }

    public PendingChatMessage[] GetPendingChatMessages(string localAccountId, string address)
    {
        lock (pendingLock)
            return pendingMessages.Values.Where(message => message.LocalAccountId == localAccountId && message.Address == address)
                .OrderBy(message => message.CreatedAt).ToArray();
    }

    public void RemoveSavedPendingMessages(string localAccountId, string address, HashSet<string> savedMessageIds)
    {
        lock (pendingLock)
            foreach (var message in pendingMessages.Values.Where(message => message.LocalAccountId == localAccountId
                && message.Address == address && message.SendStatus is { } status && savedMessageIds.Contains(status.MessageId)).ToArray())
                pendingMessages.Remove(message.Id);
    }

    void ValidatePendingAccount(string localAccountId)
    {
        if (!IsEnabled) throw new InvalidOperationException(Strings.ChatEnableFirst);
        if (AccountId != localAccountId) throw new InvalidOperationException(Strings.ChatAccountChanged);
        EnsureCurrentAccount(sessionWallet!, sessionAccount!);
    }

    // Called under pendingLock. Keep click order and bind queued work to this session.
    void SubmitPendingMessage(PendingChatMessage message, Task? storageReady = null)
    {
        var gate = GetSubmissionLock(message.LocalAccountId, message.Address);
        var cancellation = CreateOperation(CancellationToken.None);
        // Join the conversation queue before scheduling work, preserving click order.
        var ready = gate.WaitAsync(cancellation.Token);
        _ = Task.Run(async () =>
        {
            using (cancellation)
            {
                bool entered = false;
                try
                {
                    await ready;
                    entered = true;
                    if (storageReady is not null) await storageReady;
                    await SubmitTextMessageAsync(message, cancellation.Token);
                }
                catch (Exception ex)
                {
                    Task? save = null;
                    lock (pendingLock)
                        if (pendingMessages.TryGetValue(message.Id, out var current) && !current.IsCanceled)
                        {
                            pendingMessages[message.Id] = current = current with
                            {
                                ErrorMessage = cancellation.IsCancellationRequested ? Strings.OperationCancelled : ex.Message
                            };
                            if (current.SendStatus is null) save = QueuePendingMessageStorage(current);
                        }
                    // Keep the failed bubble in memory even when local persistence is unavailable.
                    if (save is not null)
                        try { await save; }
                        catch { /* The storage observer reports the failure. */ }
                }
                finally { if (entered) gate.Release(); }
            }
            NotifyChanged();
        });
    }

    sealed record StoredPendingMessage(Guid Id, string Address, string Text, DateTimeOffset CreatedAt,
        MessageRef? ReplyTo, string? ErrorMessage, bool IsCanceled);

    readonly HashSet<string> loadedPendingAccounts = [];
    Task pendingStorageWrites = Task.CompletedTask;

    string GetPendingStoragePrefix(string accountId) => $"chat/unsent/{registry.Context.Registry}/{accountId}/";

    // Called under lifecycleLock, before this account can queue or retry messages.
    async Task LoadPendingMessagesAsync(string accountId, CancellationToken cancellationToken)
    {
        if (loadedPendingAccounts.Contains(accountId)) return;
        using var scope = scopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        string prefix = GetPendingStoragePrefix(accountId);
        var rows = await database.Settings.AsNoTracking().Where(row => row.Key.StartsWith(prefix)).ToArrayAsync(cancellationToken);
        foreach (var row in rows)
        {
            var stored = JsonSerializer.Deserialize<StoredPendingMessage>(row.Value ?? "null");
            if (stored is null || stored.Id == Guid.Empty || row.Key != prefix + stored.Id.ToString("N")
                || string.IsNullOrWhiteSpace(stored.Address) || string.IsNullOrWhiteSpace(stored.Text)
                || stored.ErrorMessage is null && !stored.IsCanceled) continue;
            // Restore only completed local failures/cancellations. Never enqueue a recovered entry.
            var message = new PendingChatMessage(stored.Id, accountId, stored.Address, stored.Text, stored.CreatedAt)
            {
                ReplyTo = stored.ReplyTo, ErrorMessage = stored.ErrorMessage, IsCanceled = stored.IsCanceled
            };
            lock (pendingLock) pendingMessages.TryAdd(message.Id, message);
        }
        cancellationToken.ThrowIfCancellationRequested();
        loadedPendingAccounts.Add(accountId);
    }

    // Called under pendingLock. Preserve ordering between failure, retry, cancellation and removal.
    Task QueuePendingMessageStorage(PendingChatMessage message, bool remove = false)
    {
        var previous = pendingStorageWrites;
        string key = GetPendingStoragePrefix(message.LocalAccountId) + message.Id.ToString("N");
        var stored = new StoredPendingMessage(message.Id, message.Address, message.Text, message.CreatedAt,
            message.ReplyTo, message.ErrorMessage, message.IsCanceled);
        var write = Task.Run(async () =>
        {
            try { await previous; }
            catch { /* A previous failed write must not prevent later updates. */ }
            using var scope = scopeFactory.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            if (remove) await database.Settings.DeleteAsync(key);
            else await database.Settings.PutAsync(key, JsonSerializer.Serialize(stored));
        });
        pendingStorageWrites = write;
        _ = ObservePendingStorageAsync(write);
        return write;
    }

    static async Task ObservePendingStorageAsync(Task write)
    {
        try { await write; }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Meshline] Unsent message storage failed: {ex.Message}"); }
    }

    async Task FlushPendingMessageStorageAsync()
    {
        Task write;
        lock (pendingLock) write = pendingStorageWrites;
        try { await write; }
        catch { /* Already reported; keep in-memory entries available. */ }
    }
}
