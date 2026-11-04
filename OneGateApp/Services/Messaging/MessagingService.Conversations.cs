using Meshline.Components;
using Meshline.Identity;
using Meshline.Models.Client;
using Neo.Wallets;
using NeoOrder.OneGate.Properties;
using System.Globalization;

namespace NeoOrder.OneGate.Services.Messaging;

public sealed partial class MessagingService
{
    long unreadRevision;
    long cachedUnreadRevision = -1;
    long cachedUnreadCount;
    Dictionary<string, long> cachedConversationUnreadCounts = new(StringComparer.Ordinal);

    /// <summary>A conversation's local unread count may have changed. Delivered on the UI thread.</summary>
    public event EventHandler? UnreadCountChanged;

    void OnConversationUnreadChanged(object? sender, ConversationChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, client)) return;
        Interlocked.Increment(ref unreadRevision);
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (isForeground && !disposed && ReferenceEquals(sender, client))
                UnreadCountChanged?.Invoke(this, EventArgs.Empty);
        });
    }

    /// <summary>Reads local direct-message summaries without synchronizing relay history or fetching profiles.</summary>
    public async Task<(string LocalAccountId, ChatConversation[] Conversations)> GetChatConversationsAsync(
        CancellationToken cancellationToken = default)
    {
        await lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            await InitializeCoreAsync(cancellationToken);
            if (!IsEnabled) return (AccountId!, []);
            using var operation = CreateOperation(cancellationToken);
            string prefix = $"neo:{registry.Context.Reference.ToString(CultureInfo.InvariantCulture)}:";
            var summaries = new Dictionary<string, ConversationSummary>(StringComparer.Ordinal);
            var unreadCounts = new Dictionary<string, long>(StringComparer.Ordinal);
            await using (var reader = await client!.GetConversationsAsync(
                new ConversationQuery { Kind = ConversationKind.Direct }, operation.Token))
            {
                while (await reader.ReadNextAsync(100, operation.Token) is { Count: > 0 } batch)
                    foreach (var conversation in batch)
                        if (conversation.Latest is { } latest && conversation.ConversationId != AccountId
                            && conversation.ConversationId.StartsWith(prefix, StringComparison.Ordinal)
                            && AccountAdapter.ValidateAccountId(conversation.ConversationId) is null)
                        {
                            summaries[conversation.ConversationId] = latest;
                            unreadCounts[conversation.ConversationId] = conversation.UnreadCount;
                        }
            }

            var failedPreviews = await GetConversationSendErrorsAsync(summaries, operation.Token);

            // Messages not yet committed to SDK history must also appear when returning from chat.
            lock (pendingLock)
                foreach (var pending in pendingMessages.Values.Where(p => p.LocalAccountId == AccountId && p.SendStatus is null))
                {
                    string peer = prefix + pending.Address;
                    if (!summaries.TryGetValue(peer, out var latest) || pending.CreatedAt >= latest.Timestamp)
                    {
                        if (pending.ErrorMessage is not null) failedPreviews[peer] = MessageSendState.Failed;
                        else failedPreviews.Remove(peer);
                        summaries[peer] = new ConversationSummary
                        {
                            Sender = AccountId!, Timestamp = pending.CreatedAt, Text = pending.Text
                        };
                    }
                }

            var drafts = GetChatDrafts(AccountId!);
            var pins = await ReadConversationPinsAsync(AccountId!, operation.Token);
            operation.Token.ThrowIfCancellationRequested();
            EnsureCurrentAccount(sessionWallet!, sessionAccount!);
            var conversations = summaries.ToDictionary(p => p.Key[prefix.Length..],
                p => new ChatConversation(p.Key[prefix.Length..],
                    p.Value.Text, p.Value.Timestamp, unreadCounts.GetValueOrDefault(p.Key), null, true)
                    {
                        SendErrorState = failedPreviews.TryGetValue(p.Key, out var state) ? state : null
                    }, StringComparer.Ordinal);
            // Include unsent first messages without adding them to SDK history or unread counts.
            foreach (var (address, draft) in drafts)
            {
                var conversation = conversations.GetValueOrDefault(address)
                    ?? new ChatConversation(address, null, draft.UpdatedAt, 0, null, false);
                conversations[address] = conversation with
                {
                    Draft = draft.Text,
                    UpdatedAt = draft.UpdatedAt > conversation.UpdatedAt ? draft.UpdatedAt : conversation.UpdatedAt
                };
            }
            // A pin keeps a conversation accessible even after its first draft is cleared.
            foreach (string address in pins)
                conversations.TryAdd(address, new ChatConversation(address,
                    null, DateTimeOffset.MinValue, 0, null, false));
            return (AccountId!, conversations.Values.Select(p => p with { IsPinned = pins.Contains(p.Address) })
                .OrderByDescending(p => p.IsPinned).ThenByDescending(p => p.UpdatedAt)
                .ThenBy(p => p.Address, StringComparer.Ordinal).ToArray());
        }
        finally { lifecycleLock.Release(); }
    }

    /// <summary>Reads the current session's local direct-message unread total, optionally excluding one peer; reuses the same cached snapshot without starting a session or querying contacts.</summary>
    public async Task<(string? LocalAccountId, long Count)> GetChatUnreadCountAsync(CancellationToken cancellationToken = default,
        string? excludingAddress = null)
    {
        await lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (!IsEnabled) return (AccountId, 0);
            using var operation = CreateOperation(cancellationToken);
            operation.Token.ThrowIfCancellationRequested();
            EnsureCurrentAccount(sessionWallet!, sessionAccount!);
            string? excludedPeer = excludingAddress is null ? null : GetContactAccountId(excludingAddress);
            long Count() => cachedUnreadCount - (excludedPeer is null ? 0 : cachedConversationUnreadCounts.GetValueOrDefault(excludedPeer));
            long revision = Volatile.Read(ref unreadRevision);
            if (cachedUnreadRevision == revision) return (AccountId, Count());
            string prefix = $"neo:{registry.Context.Reference.ToString(CultureInfo.InvariantCulture)}:";
            long count = 0;
            var counts = new Dictionary<string, long>(StringComparer.Ordinal);
            await using var reader = await client!.GetConversationsAsync(
                new ConversationQuery { Kind = ConversationKind.Direct, UnreadOnly = true }, operation.Token);
            while (await reader.ReadNextAsync(100, operation.Token) is { Count: > 0 } batch)
                foreach (var conversation in batch)
                    if (conversation.ConversationId != AccountId
                        && conversation.ConversationId.StartsWith(prefix, StringComparison.Ordinal)
                        && AccountAdapter.ValidateAccountId(conversation.ConversationId) is null)
                    {
                        count += conversation.UnreadCount;
                        counts[conversation.ConversationId] = conversation.UnreadCount;
                    }
            operation.Token.ThrowIfCancellationRequested();
            EnsureCurrentAccount(sessionWallet!, sessionAccount!);
            // An event during this snapshot keeps the cache dirty for the queued refresh.
            cachedUnreadCount = count;
            cachedConversationUnreadCounts = counts;
            cachedUnreadRevision = revision;
            return (AccountId, Count());
        }
        finally { lifecycleLock.Release(); }
    }

    /// <summary>Advances this device's read position through a displayed message, or all currently stored messages when omitted; no read receipt is sent.</summary>
    public async Task<bool> MarkChatReadAsync(string localAccountId, string address, long? localSequence = null,
        CancellationToken cancellationToken = default)
    {
        if (localSequence.HasValue) ArgumentOutOfRangeException.ThrowIfNegativeOrZero(localSequence.Value);
        await lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            // A late page callback must not restart a suspended or closed session.
            if (!isForeground) return false;
            if (!IsEnabled) throw new InvalidOperationException(Strings.ChatEnableFirst);
            if (AccountId != localAccountId) throw new InvalidOperationException(Strings.ChatAccountChanged);
            string accountId = GetContactAccountId(address);
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
                operation.Token.ThrowIfCancellationRequested();
                EnsureCurrentAccount(wallet, account);
                if (localSequence is { } sequence)
                    // Reading chat uses the page's displayed history boundary.
                    await client!.MarkReadAsync(accountId, sequence, operation.Token);
                else
                    // The explicit list action marks current local history in one SDK transaction.
                    await client!.MarkReadAsync(accountId, operation.Token);
                operation.Token.ThrowIfCancellationRequested();
                EnsureCurrentAccount(wallet, account);
                NotifyChanged(MessagingChangeKind.ReadState);
                return true;
            }
            finally { walletProvider.WalletChanged -= OnWalletChanged; }
        }
        finally { lifecycleLock.Release(); }
    }

    long sendStatusRevision;
    long failedMessagesRevision = -1;
    Dictionary<string, Dictionary<string, MessageSendState>> failedMessageStates = [];
    readonly Dictionary<(string Peer, DateTimeOffset Timestamp, string? Text, bool HasAttachments), MessageSendState?> failedPreviewCache = [];

    void OnMessageSendStatusChanged(object? sender, MessageSendStatusChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, client?.MessageManager)) return;
        Interlocked.Increment(ref sendStatusRevision);
        NotifyChanged(MessagingChangeKind.SendStatus);
    }

    // Called under lifecycleLock. Only a send-state change invalidates the local outbox snapshot.
    async Task<Dictionary<string, MessageSendState>> GetConversationSendErrorsAsync(
        Dictionary<string, ConversationSummary> summaries, CancellationToken cancellationToken)
    {
        var failed = new Dictionary<string, MessageSendState>(StringComparer.Ordinal);
        await ReadFailedMessageStatesAsync(cancellationToken);
        foreach (var (peer, summary) in summaries)
        {
            if (summary.Sender != AccountId || !failedMessageStates.TryGetValue(peer, out var states)) continue;
            var key = (peer, summary.Timestamp, summary.Text, summary.HasAttachments);
            if (!failedPreviewCache.TryGetValue(key, out var state))
            {
                // Summaries have no message ID. Match the latest local record before attributing a failure.
                await using var reader = await client!.MessageManager.GetMessageHistoryAsync(peer,
                    new HistoryRange { Before = 9_007_199_254_740_991 }, cancellationToken);
                var message = (await reader.ReadNextAsync(1, cancellationToken)).LastOrDefault();
                state = message is not null && message.Key.Sender == AccountId
                    && message.CreatedAt == summary.Timestamp && message.Body?.Text == summary.Text
                    && (message.Attachments is { Count: > 0 }) == summary.HasAttachments
                    && states.TryGetValue(message.Key.MessageId, out var errorState) ? errorState : null;
                failedPreviewCache[key] = state;
            }
            if (state is { } error) failed[peer] = error;
        }
        return failed;
    }

    // Shared by local summary and history queries, both under lifecycleLock.
    async Task ReadFailedMessageStatesAsync(CancellationToken cancellationToken)
    {
        long revision = Volatile.Read(ref sendStatusRevision);
        if (failedMessagesRevision != revision)
        {
            var updated = new Dictionary<string, Dictionary<string, MessageSendState>>(StringComparer.Ordinal);
            await using var reader = await client!.MessageManager.GetOutboxAsync(
                state: MessageSendState.Failed | MessageSendState.Canceled, cancellationToken: cancellationToken);
            while (await reader.ReadNextAsync(100, cancellationToken) is { Count: > 0 } batch)
                foreach (var status in batch)
                {
                    if (!updated.TryGetValue(status.Recipient, out var states))
                        updated[status.Recipient] = states = new(StringComparer.Ordinal);
                    states[status.MessageId] = status.State;
                }
            cancellationToken.ThrowIfCancellationRequested();
            failedMessageStates = updated;
            failedPreviewCache.Clear();
            // If a status changed during this read, the next demand will refresh it again.
            failedMessagesRevision = revision;
        }
    }

    void ClearConversationFailureCache()
    {
        Interlocked.Increment(ref sendStatusRevision);
        failedMessagesRevision = -1;
        failedMessageStates.Clear();
        failedPreviewCache.Clear();
    }
}
