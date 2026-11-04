using Microsoft.EntityFrameworkCore;
using Neo.Wallets;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Properties;

namespace NeoOrder.OneGate.Services.Messaging;

public sealed partial class MessagingService
{
    // Accessed under lifecycleLock. These local records only change through SetChatPinnedAsync.
    readonly Dictionary<string, (HashSet<string> Addresses, DateTimeOffset? RetryAfter)> conversationPins = [];

    async Task<HashSet<string>> ReadConversationPinsAsync(string accountId, CancellationToken cancellationToken)
    {
        if (conversationPins.TryGetValue(accountId, out var cached)
            && (cached.RetryAfter is null || DateTimeOffset.UtcNow < cached.RetryAfter)) return cached.Addresses;
        try
        {
            using var scope = scopeFactory.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var peers = await database.ChatPins.AsNoTracking().Where(p => p.LocalAccountId == accountId)
                .Select(p => p.PeerAddress).ToArrayAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var addresses = peers.ToHashSet(StringComparer.Ordinal);
            conversationPins[accountId] = (addresses, null);
            return addresses;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            // Keep the conversation list usable. Retry a failed local read on demand, without a timer.
            var addresses = cached.Addresses ?? new HashSet<string>(StringComparer.Ordinal);
            conversationPins[accountId] = (addresses, DateTimeOffset.UtcNow.AddMinutes(1));
            return addresses;
        }
    }

    public async Task SetChatPinnedAsync(string localAccountId, string address, bool pinned,
        CancellationToken cancellationToken = default)
    {
        await lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            await InitializeCoreAsync(cancellationToken);
            if (!IsEnabled) throw new InvalidOperationException(Strings.ChatEnableFirst);
            if (AccountId != localAccountId) throw new InvalidOperationException(Strings.ChatAccountChanged);
            GetContactAccountId(address);
            address = address.Trim();
            Wallet wallet = sessionWallet!;
            WalletAccount account = sessionAccount!;
            using var operation = CreateOperation(cancellationToken);
            void OnWalletChanged(object? sender, Wallet? current)
            {
                if (!ReferenceEquals(current, wallet)) operation.Cancel();
            }
            walletProvider.WalletChanged += OnWalletChanged;
            try
            {
                using var scope = scopeFactory.CreateScope();
                var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                operation.Token.ThrowIfCancellationRequested();
                EnsureCurrentAccount(wallet, account);
                if (pinned)
                    await database.Database.ExecuteSqlAsync($"""
                        INSERT INTO [ChatPins]([LocalAccountId], [PeerAddress]) VALUES({localAccountId}, {address})
                        ON CONFLICT([LocalAccountId], [PeerAddress]) DO NOTHING
                        """, operation.Token);
                else
                    await database.ChatPins.Where(p => p.LocalAccountId == localAccountId && p.PeerAddress == address)
                        .ExecuteDeleteAsync(operation.Token);
                // Publish only after persistence succeeds; failed writes leave the displayed state intact.
                if (conversationPins.TryGetValue(localAccountId, out var cached))
                {
                    var addresses = new HashSet<string>(cached.Addresses, StringComparer.Ordinal);
                    if (pinned) addresses.Add(address); else addresses.Remove(address);
                    conversationPins[localAccountId] = (addresses, cached.RetryAfter is null ? null : DateTimeOffset.MinValue);
                }
                EnsureCurrentAccount(wallet, account);
                NotifyChanged();
            }
            finally { walletProvider.WalletChanged -= OnWalletChanged; }
        }
        finally { lifecycleLock.Release(); }
    }
}
