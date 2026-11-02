using CommunityToolkit.Maui.Alerts;
using Meshline.Models.Client;
using Neo.Wallets;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services.Messaging;

namespace NeoOrder.OneGate.Pages;

public partial class ChatPage
{
    MessageRef? requestedQuote;
    CancellationTokenSource? quoteNavigation;
    CancellationTokenSource? quoteHighlight;
    ChatMessageViewModel? highlightedQuote;
    QuoteReturnPosition? requestedQuoteOrigin;
    QuoteReturnPosition? quoteReturnPosition;

    public bool CanCancelQuoteNavigation => HasLoaded && !switchingConversation && !IsChatContentCovered
        && (requestedQuote is not null || quoteNavigation is { IsCancellationRequested: false })
        && IsCurrentMessageContext(walletProvider.GetWallet(), localAccountId, Address);

    void OnCancelQuoteNavigationClicked(object sender, EventArgs e)
    {
        if (CanCancelQuoteNavigation) CancelQuoteNavigation();
    }

    void OnQuotedMessageTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is ChatMessageViewModel message && Messages.Contains(message))
            RequestQuotedMessage(message.ReplyTo);
    }

    void OnDraftQuoteTapped(object sender, TappedEventArgs e) => RequestQuotedMessage(replyTo);

    void RequestQuotedMessage(MessageRef? reference)
    {
        if (reference is null || !HasLoaded || switchingConversation
            || !IsCurrentMessageContext(walletProvider.GetWallet(), localAccountId, Address)) return;
        CancelQuoteNavigation();
        requestedQuote = reference;
        requestedQuoteOrigin = CaptureQuoteReturnPosition();
        scrollRevision++;
        NotifyQuoteNavigation();
        // A refresh already reading history completes first. The most recent tap then takes priority.
        ReloadIfIdle();
    }

    void CancelQuoteNavigation()
    {
        requestedQuote = null;
        requestedQuoteOrigin = null;
        quoteNavigation?.Cancel();
        NotifyQuoteNavigation();
    }

    void NotifyQuoteNavigation()
    {
        OnPropertyChanged(nameof(ShowLoading));
        OnPropertyChanged(nameof(CanCancelQuoteNavigation));
    }

    async Task NavigateToQuoteAsync(MessageRef reference, QuoteReturnPosition? origin = null)
    {
        var wallet = walletProvider.GetWallet();
        string? accountId = localAccountId;
        string address = Address;
        bool IsCurrent() => !switchingConversation && IsCurrentMessageContext(wallet, accountId, address);
        if (!HasLoaded || IsLoading || !IsCurrent()) return;
        origin ??= CaptureQuoteReturnPosition();
        if (origin is not null && !IsCurrentMessageContext(origin.Wallet, origin.AccountId, origin.Address)) return;
        using var cancellation = new CancellationTokenSource();
        operation = quoteNavigation = cancellation;
        isLoadingEarlier = false;
        BusyText = Strings.ChatFindingQuotedMessage;
        NotifyState();
        try
        {
            ChatMessageViewModel? FindTarget() => Messages.FirstOrDefault(value => value.SavedMessage?.Key == reference);
            var target = FindTarget();
            if (target is null)
            {
                var original = replyMessages.GetValueOrDefault(reference).Message;
                if (original is null)
                {
                    var originals = await messagingService.GetChatReplyMessagesAsync(accountId!, address, [reference], cancellation.Token);
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (!IsCurrent()) return;
                    original = originals.GetValueOrDefault(reference);
                    if (original is not null) replyMessages[reference] = (original, DateTimeOffset.MaxValue);
                }
                if (original is not null)
                {
                    // A referenced message can be received later than its reply and get a newer local sequence.
                    var last = Messages.LastOrDefault(value => !value.IsPending);
                    if (last is null || original.LocalSequence > last.LocalSequence)
                    {
                        await ReadNewHistoryAsync(cancellation.Token);
                        cancellation.Token.ThrowIfCancellationRequested();
                        if (!IsCurrent()) return;
                    }
                    target = FindTarget();
                    if (target is null && Messages.FirstOrDefault(value => !value.IsPending) is { } first)
                    {
                        long before = first.LocalSequence;
                        var batches = new List<ChatMessageViewModel[]>();
                        bool hasMore = HasMore;
                        while (before > original.LocalSequence)
                        {
                            var snapshot = await messagingService.GetChatHistoryAsync(accountId!, address,
                                before: before, cancellationToken: cancellation.Token);
                            cancellation.Token.ThrowIfCancellationRequested();
                            if (!IsCurrent()) return;
                            if (snapshot.Messages.Length == 0 || snapshot.Messages[0].LocalSequence >= before) break;
                            var batch = snapshot.Messages.Select(value => new ChatMessageViewModel(value, accountId!)).ToArray();
                            batches.Add(batch);
                            hasMore = snapshot.HasMore;
                            target = batch.FirstOrDefault(value => value.SavedMessage?.Key == reference);
                            if (target is not null || !hasMore) break;
                            before = snapshot.Messages[0].LocalSequence;
                        }
                        if (target is not null)
                        {
                            // Commit once, only after finding the target. No gaps or partial results on failure.
                            batches.Reverse();
                            SetMessages([.. batches.SelectMany(value => value), .. Messages]);
                            HasMore = hasMore;
                            EarlierMessagesError = null;
                            RefreshPendingMessages();
                            reloadPending |= MessagingChangeKind.Records;
                        }
                    }
                }
            }
            cancellation.Token.ThrowIfCancellationRequested();
            if (!IsCurrent()) return;
            if (target is null) await Toast.Show(Strings.ChatQuotedMessageUnavailable);
            else
            {
                // Nested quote jumps keep the initial reading position until explicitly returning.
                quoteReturnPosition ??= origin;
                FocusQuotedMessage(target);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Meshline] Quote navigation failed: {ex}");
            if (!cancellation.IsCancellationRequested && IsCurrent())
                await Toast.Show(Strings.OperationFailed);
        }
        finally
        {
            if (ReferenceEquals(quoteNavigation, cancellation)) quoteNavigation = null;
            if (ReferenceEquals(operation, cancellation))
            {
                operation = null;
                NotifyState();
                ReloadIfIdle();
            }
        }
    }

    void FocusQuotedMessage(ChatMessageViewModel target)
    {
        // An explicit jump supersedes initial positioning and queued refresh/resize scrolling.
        scrollRevision++;
        latestScrollRevision = null;
        if (!IsMessageListPresented) initialScrollAnchor = target;
        followLatest = false;
        firstVisibleIndex = Messages.IndexOf(target);
        ClearQuoteHighlight();
        highlightedQuote = target;
        target.IsQuoteHighlighted = true;
        var cancellation = new CancellationTokenSource();
        quoteHighlight = cancellation;
        _ = ClearQuoteHighlightLaterAsync(cancellation);
        NotifyScrollState();
        DispatchMessageScroll(target, scrollToEnd: false);
    }

    async Task ClearQuoteHighlightLaterAsync(CancellationTokenSource cancellation)
    {
        using (cancellation)
        {
            try
            {
                await Task.Delay(2500, cancellation.Token);
                if (ReferenceEquals(quoteHighlight, cancellation)) ClearQuoteHighlight();
            }
            catch (OperationCanceledException) { }
        }
    }

    void ClearQuoteHighlight()
    {
        quoteHighlight?.Cancel();
        quoteHighlight = null;
        if (highlightedQuote is { } message) message.IsQuoteHighlighted = false;
        highlightedQuote = null;
    }

    sealed record QuoteReturnPosition(Wallet Wallet, string AccountId, string Address,
        ChatMessageViewModel Anchor, MessageRef? Fallback);

    public bool ShowQuoteReturn => isVisible && IsChatEnabled && HasLoaded
        && (CanReturnToMessageSearch || quoteReturnPosition is not null);
    public string QuoteReturnText => CanReturnToMessageSearch ? Strings.ChatReturnToSearchResults : Strings.ChatReturnToPosition;

    QuoteReturnPosition? CaptureQuoteReturnPosition()
    {
        var wallet = walletProvider.GetWallet();
        if (Messages.Count == 0 || localAccountId is null
            || !IsCurrentMessageContext(wallet, localAccountId, Address)) return null;
        int index = Math.Clamp(firstVisibleIndex, 0, Messages.Count - 1);
        // Keep a saved neighbour in case an optimistic bubble disappears while viewing the quote.
        var fallback = Messages.Take(index + 1).LastOrDefault(value => !value.IsPending)?.SavedMessage?.Key;
        return new(wallet!, localAccountId, Address, Messages[index], fallback);
    }

    void OnQuoteReturnClicked(object sender, EventArgs e)
    {
        if (IsChatContentCovered) return;
        if (CanReturnToMessageSearch)
        {
            // A search-result jump returns to its results, keeping the ordinary reading anchor intact.
            OnOpenMessageSearchClicked(sender, e);
            return;
        }
        if (!HasLoaded || switchingConversation || quoteReturnPosition is not { } position
            || !IsCurrentMessageContext(position.Wallet, position.AccountId, position.Address)) return;
        var anchor = position.Anchor;
        ChatMessageViewModel? target;
        if (anchor.SavedMessage is { } saved)
            target = Messages.FirstOrDefault(value => value.SavedMessage?.Key == saved.Key);
        else
            target = Messages.FirstOrDefault(value => value.PendingId == anchor.PendingId)
                ?? (anchor.MessageId is { } id
                    ? Messages.FirstOrDefault(value => value.IsOutgoing && value.MessageId == id) : null);
        target ??= position.Fallback is { } key
            ? Messages.FirstOrDefault(value => value.SavedMessage?.Key == key) : null;
        CancelQuoteNavigation();
        ClearQuoteReturnPosition();
        // Restore the old reading anchor, even if newer messages have arrived in the meantime.
        // The native viewport still decides when the latest messages have actually been read.
        if (target is not null) FocusQuotedMessage(target);
    }

    void ClearQuoteReturnPosition()
    {
        quoteReturnPosition = null;
        OnPropertyChanged(nameof(ShowQuoteReturn));
    }
}
