using Meshline.Models.Client;
using Neo.Wallets;
using NeoOrder.OneGate.Properties;

namespace NeoOrder.OneGate.Services.Messaging;

public sealed partial class MessagingService
{
    public async Task<(MessageInfo[] Messages, bool HasMore, ResourceSyncStatus? SyncStatus, HashSet<string>? SubmittedMessageIds)> GetChatHistoryAsync(
        string localAccountId, string address, long? before = null,
        CancellationToken cancellationToken = default, long? after = null)
    {
        if (before.HasValue && after.HasValue) throw new ArgumentException("Specify one history direction.");
        // Earlier pages end at an existing sequence. Only reads that can include a new send wait for its ID.
        var submission = before.HasValue ? null : GetSubmissionLock(localAccountId, address);
        if (submission is not null) await submission.WaitAsync(cancellationToken);
        try
        {
            await lifecycleLock.WaitAsync(cancellationToken);
            try
            {
                await InitializeCoreAsync(cancellationToken);
                if (!IsEnabled) throw new InvalidOperationException(Strings.ChatEnableFirst);
                if (AccountId != localAccountId) throw new InvalidOperationException(Strings.ChatAccountChanged);
                string accountId = GetContactAccountId(address);
                var current = client!;
                Wallet wallet = sessionWallet!;
                WalletAccount account = sessionAccount!;
                using var operation = CreateOperation(cancellationToken);
                void OnWalletChanged(object? sender, Wallet? changedWallet)
                {
                    if (!ReferenceEquals(changedWallet, wallet)) operation.Cancel();
                }
                walletProvider.WalletChanged += OnWalletChanged;
                try
                {
                    operation.Token.ThrowIfCancellationRequested();
                    EnsureCurrentAccount(wallet, account);
                    // Read local state only. The SDK synchronizes and recovers in the background.
                    var status = await current.MessageManager.GetSyncStatusAsync(current.Route!.RelayId, operation.Token);
                    // The SDK commits outgoing history with the outbox before returning a message ID.
                    var submittedMessageIds = before is null && after is null
                        ? GetPendingChatMessages(localAccountId, address).Select(message => message.SendStatus?.MessageId)
                            .OfType<string>().ToHashSet(StringComparer.Ordinal)
                        : null;
                    // Removing a friend does not remove this account's verified local history.
                    // Before selects the closest older batch. The SDK accepts safe-integer bounds.
                    await using var reader = await current.MessageManager.GetMessageHistoryAsync(accountId,
                        after.HasValue ? new HistoryRange { After = after }
                            : new HistoryRange { Before = before ?? 9_007_199_254_740_991 }, operation.Token);
                    var messages = await reader.ReadNextAsync(51, operation.Token);
                    operation.Token.ThrowIfCancellationRequested();
                    EnsureCurrentAccount(wallet, account);
                    bool hasMore = messages.Count > 50;
                    return ((after.HasValue ? messages.Take(50) : messages.TakeLast(50)).ToArray(), hasMore, status,
                        hasMore ? submittedMessageIds : null);
                }
                finally { walletProvider.WalletChanged -= OnWalletChanged; }
            }
            finally { lifecycleLock.Release(); }
        }
        finally { submission?.Release(); }
    }

    // Reads verified local history only; a reply reference must never expose a different conversation.
    public async Task<Dictionary<MessageRef, MessageInfo>> GetChatReplyMessagesAsync(string localAccountId,
        string address, IReadOnlyCollection<MessageRef> references, CancellationToken cancellationToken = default)
    {
        await lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            await InitializeCoreAsync(cancellationToken);
            if (!IsEnabled) throw new InvalidOperationException(Strings.ChatEnableFirst);
            if (AccountId != localAccountId) throw new InvalidOperationException(Strings.ChatAccountChanged);
            string peer = GetContactAccountId(address);
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
                EnsureCurrentAccount(wallet, account);
                var result = new Dictionary<MessageRef, MessageInfo>();
                foreach (var reference in references.Distinct())
                {
                    if (reference.Sender != localAccountId && reference.Sender != peer) continue;
                    var original = await client!.MessageManager.GetMessageAsync(reference, operation.Token);
                    operation.Token.ThrowIfCancellationRequested();
                    EnsureCurrentAccount(wallet, account);
                    if (original is not null && original.Key == reference
                        && (original.Key.Sender == localAccountId && original.Recipient == peer
                            || original.Key.Sender == peer && original.Recipient == localAccountId))
                        result[reference] = original;
                }
                operation.Token.ThrowIfCancellationRequested();
                EnsureCurrentAccount(wallet, account);
                return result;
            }
            finally { walletProvider.WalletChanged -= OnWalletChanged; }
        }
        finally { lifecycleLock.Release(); }
    }
}
