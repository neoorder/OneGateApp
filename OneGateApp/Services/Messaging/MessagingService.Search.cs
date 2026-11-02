using Meshline.Models.Client;
using Meshline.Models.Protocol;
using Neo.Wallets;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Properties;

namespace NeoOrder.OneGate.Services.Messaging;

public sealed partial class MessagingService
{
    const int SearchHistoryScanLimit = 200;
    const int SearchHistoryResultLimit = 20;
    const long SearchHistoryUpperBound = 9_007_199_254_740_991;

    /// <summary>Searches one bounded batch of local plain-text history, newest local sequence first.</summary>
    /// <remarks>
    /// Uses a literal, case-insensitive substring after trimming the query; a blank query requires a sender, time or link filter.
    /// Requires an enabled session;
    /// does not initialize a session, synchronize relay history, fetch profiles or change read positions.
    /// Pass NextBefore back with the same account, peer, query, sender/link filters and CreatedAt lower bound to continue toward older messages.
    /// A non-null cursor means unsearched history remains, even when this batch has no matches.
    /// Reset the cursor when the query, filters or lower bound change, or to include messages received since the search started.
    /// </remarks>
    public async Task<(MessageInfo[] Messages, long? NextBefore)> SearchChatMessagesAsync(
        string localAccountId, string address, string query, long? before = null,
        ChatSearchSender sender = ChatSearchSender.All, DateTimeOffset? createdSince = null,
        bool linksOnly = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (sender == ChatSearchSender.All && createdSince is null && !linksOnly) ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (before is < 0 or > SearchHistoryUpperBound) throw new ArgumentOutOfRangeException(nameof(before));
        if (!Enum.IsDefined(sender)) throw new ArgumentOutOfRangeException(nameof(sender));
        string term = query.Trim();
        await lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(disposed, this);
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
                operation.Token.ThrowIfCancellationRequested();
                EnsureCurrentAccount(wallet, account);
                // Previously received history stays searchable after a contact is removed.
                IReadOnlyList<MessageInfo> batch;
                await using (var reader = await client!.MessageManager.GetMessageHistoryAsync(peer,
                    new HistoryRange { Before = before ?? SearchHistoryUpperBound }, operation.Token))
                {
                    // One lookahead row distinguishes exhaustion from a full bounded scan.
                    batch = await reader.ReadNextAsync(SearchHistoryScanLimit + 1, operation.Token);
                }
                operation.Token.ThrowIfCancellationRequested();
                EnsureCurrentAccount(wallet, account);
                var matches = new List<MessageInfo>();
                int index = batch.Count;
                int scanned = 0;
                // SDK backward batches are ascending internally; search from their newest end.
                // CreatedAt can differ from arrival order, so an older timestamp must not end the scan.
                while (index > 0 && scanned < SearchHistoryScanLimit && matches.Count < SearchHistoryResultLimit)
                {
                    operation.Token.ThrowIfCancellationRequested();
                    var message = batch[--index];
                    scanned++;
                    if ((message.Key.Sender == localAccountId && message.Recipient == peer
                            || message.Key.Sender == peer && message.Recipient == localAccountId)
                        && (sender == ChatSearchSender.All || message.Key.Sender == (sender == ChatSearchSender.Self ? localAccountId : peer))
                        && (createdSince is null || message.CreatedAt >= createdSince.Value)
                        && message.Body is { ContentType: "text/plain", Text: { } text }
                        && !string.IsNullOrWhiteSpace(text)
                        && (term.Length == 0 || text.Contains(term, StringComparison.OrdinalIgnoreCase))
                        && (!linksOnly || ChatMessageLinks.FindOccurrences(text).Any()))
                        matches.Add(message);
                }
                operation.Token.ThrowIfCancellationRequested();
                EnsureCurrentAccount(wallet, account);
                // Continue before the last examined row, not before the last match or the fetched batch.
                // This preserves unmatched gaps and matches left over when the result limit is reached.
                return ([.. matches], index > 0 ? batch[index].LocalSequence : null);
            }
            finally { walletProvider.WalletChanged -= OnWalletChanged; }
        }
        finally { lifecycleLock.Release(); }
    }
}
