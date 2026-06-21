using Microsoft.Extensions.DependencyInjection;
using Neo;
using Neo.VM;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services.RPC;
using Plugin.LocalNotification;
using Plugin.LocalNotification.Core.Models;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NeoOrder.OneGate.Services;

/// <summary>
/// Tracks broadcast transactions until they reach a terminal state and raises a local
/// notification on confirm/fail. Unlike the in-page poll on SendingPage, tracking lives
/// for the app process and is persisted, so it survives navigation, backgrounding and relaunch.
/// </summary>
public sealed class PendingTransactionService(IServiceScopeFactory scopeFactory, RpcClient rpcClient)
{
    const string StorageKey = "transactions/pending";
    static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(15);
    static readonly TimeSpan MaxPendingAge = TimeSpan.FromMinutes(10);

    readonly SemaphoreSlim mutex = new(1, 1);
    readonly object loopLock = new();
    readonly List<PendingTransaction> pending = [];
    Task? loop;

    /// <summary>Request notification permission and resume tracking persisted pending transactions.</summary>
    public async Task StartAsync()
    {
        try
        {
            try { await LocalNotificationCenter.Current.RequestNotificationPermission(); }
            catch { /* permission is best-effort; tracking still works without it */ }

            List<PendingTransaction>? saved = await LoadAsync();
            if (saved is not { Count: > 0 }) return;
            await mutex.WaitAsync();
            try
            {
                foreach (PendingTransaction p in saved)
                    if (!pending.Any(x => x.Hash == p.Hash))
                        pending.Add(p);
            }
            finally { mutex.Release(); }
            EnsureLoop();
        }
        catch
        {
            // Pending transaction tracking must never make app startup fail.
        }
    }

    public void Enqueue(UInt256 hash) => _ = EnqueueAsync(hash.ToString());

    async Task EnqueueAsync(string hash)
    {
        await mutex.WaitAsync();
        try
        {
            if (pending.Any(x => x.Hash == hash)) return;
            pending.Add(new PendingTransaction { Hash = hash, CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
            await SaveAsync();
        }
        finally { mutex.Release(); }
        EnsureLoop();
    }

    void EnsureLoop()
    {
        lock (loopLock)
        {
            if (loop is { IsCompleted: false }) return;
            loop = Task.Run(PollLoopAsync);
        }
    }

    async Task PollLoopAsync()
    {
        try
        {
            while (true)
            {
                await Task.Delay(PollInterval);

                PendingTransaction[] snapshot;
                await mutex.WaitAsync();
                try { snapshot = [.. pending]; }
                finally { mutex.Release(); }
                if (snapshot.Length == 0) return;

                foreach (PendingTransaction p in snapshot)
                {
                    bool? succeeded = await TryResolveAsync(p.Hash);
                    if (succeeded is null && !IsExpired(p)) continue;

                    await mutex.WaitAsync();
                    try { pending.RemoveAll(x => x.Hash == p.Hash); await SaveAsync(); }
                    finally { mutex.Release(); }

                    if (succeeded is not null) Notify(p.Hash, succeeded.Value);
                }
            }
        }
        catch
        {
            // Polling is background best-effort work; do not surface unobserved task exceptions.
        }
    }

    // null = not yet in a block; true = HALT; false = FAULT (reverted).
    async Task<bool?> TryResolveAsync(string hash)
    {
        JsonObject tx;
        try
        {
            tx = await rpcClient.RpcSendAsync<JsonObject>("getrawtransaction", hash, true);
            if (tx["blocktime"]?.GetValue<ulong>() is null) return null;
        }
        catch (Exception ex) when (IsExpectedRpcOrJsonException(ex))
        {
            return null;
        }

        try
        {
            JsonObject log = await rpcClient.RpcSendAsync<JsonObject>("getapplicationlog", hash);
            JsonNode? execution = log["executions"] is JsonArray executions && executions.Count > 0 ? executions[0] : null;
            JsonNode? vmState = execution?["vmstate"];
            return vmState is null || vmState.GetValue<string>() == nameof(VMState.HALT);
        }
        catch (Exception ex) when (IsExpectedRpcOrJsonException(ex))
        {
            // The transaction is in a block but the application log is unavailable or malformed.
            // Treat it as confirmed to avoid a false failure notification.
            return true;
        }
    }

    static void Notify(string hash, bool succeeded)
    {
        string shortHash = hash.Length <= 16 ? hash : $"{hash[..10]}…{hash[^6..]}";
        LocalNotificationCenter.Current.Show(new NotificationRequest
        {
            NotificationId = CreateNotificationId(hash),
            Title = succeeded ? Strings.TransactionSucceeded : Strings.TransactionFailed,
            Description = shortHash,
        });
    }

    async Task<List<PendingTransaction>?> LoadAsync()
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await db.Settings.GetAsync<List<PendingTransaction>>(StorageKey);
    }

    async Task SaveAsync()
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        ApplicationDbContext db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Settings.PutAsync(StorageKey, pending);
    }

    static bool IsExpired(PendingTransaction transaction)
    {
        try
        {
            DateTimeOffset createdAt = DateTimeOffset.FromUnixTimeMilliseconds(transaction.CreatedAt);
            return DateTimeOffset.UtcNow - createdAt >= MaxPendingAge;
        }
        catch (ArgumentOutOfRangeException)
        {
            return true;
        }
    }

    static bool IsExpectedRpcOrJsonException(Exception ex) =>
        ex is RpcException or HttpRequestException or JsonException or InvalidOperationException or FormatException;

    static int CreateNotificationId(string hash)
    {
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(hash));
        int id = BinaryPrimitives.ReadInt32LittleEndian(digest) & int.MaxValue;
        return id == 0 ? 1 : id;
    }
}

public sealed class PendingTransaction
{
    public required string Hash { get; set; }
    public long CreatedAt { get; set; }
}
