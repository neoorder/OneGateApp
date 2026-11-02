using Neo.Wallets;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services.Messaging;
using System.Collections.ObjectModel;

namespace NeoOrder.OneGate.Pages;

public partial class ChatPage
{
    Entry? messageSearchEntry;
    CollectionView? messageSearchResultsList;
    Wallet? searchWallet;
    string? searchAccountId;
    string searchAddress = "";
    CancellationTokenSource? messageSearchOperation;
    long? messageSearchBefore;
    bool hasSearchedMessages;
    ChatSearchResult? selectedSearchResult;
    bool restoreSearchResultPending;
    int messageSearchViewRevision;
    ChatSearchSender messageSearchSender;
    bool choosingSearchSender;
    bool choosingSearchFilter;
    bool choosingSearchPeriod;
    int messageSearchDays;
    bool messageSearchLinks;
    DateTimeOffset? messageSearchSince;

    public Command<ChatSearchResult> OpenSearchResultCommand { get; }
    public string? SearchResultActionError { get; private set { field = value; OnPropertyChanged(); } }
    public ObservableCollection<ChatSearchResult> MessageSearchResults { get; } = [];
    public bool ShowMessageSearch { get; private set; }
    public bool IsChatContentCovered => ShowMessageReader || ShowMessageSearch || ShowForwardRecipientPicker;
    public bool CanOpenMessageSearch => HasLoaded && !IsChatContentCovered && !switchingConversation
        && IsCurrentMessageContext(walletProvider.GetWallet(), localAccountId, Address);
    public bool IsSearchingMessages => messageSearchOperation is not null;
    bool HasMessageSearchCriteria => HasMessageSearchFilters || !string.IsNullOrWhiteSpace(MessageSearchText);
    public bool CanSearchMessages => IsCurrentMessageSearch() && !IsSearchingMessages && HasMessageSearchCriteria;
    public bool CanUseMessageSearchButton => IsCurrentMessageSearch()
        && (IsSearchingMessages || HasMessageSearchCriteria);
    public string MessageSearchButtonText => IsSearchingMessages ? Strings.Cancel : Strings.Search;
    string MessageSearchSenderText => (messageSearchSender switch
    {
        ChatSearchSender.Self => Strings.ChatYou,
        ChatSearchSender.Peer => Strings.ChatOtherParticipant,
        _ => Strings.All
    });
    string MessageSearchPeriodText => messageSearchDays switch
    {
        7 => Strings.ChatLast7Days,
        30 => Strings.ChatLast30Days,
        _ => Strings.All
    };
    public string MessageSearchFilterText
    {
        get
        {
            int count = (messageSearchSender == ChatSearchSender.All ? 0 : 1) + (messageSearchDays == 0 ? 0 : 1)
                + (messageSearchLinks ? 1 : 0);
            return (count == 0 ? Strings.ChatSearchFilters : $"{Strings.ChatSearchFilters} ({count})") + " ▾";
        }
    }
    bool HasMessageSearchFilters => messageSearchSender != ChatSearchSender.All || messageSearchDays != 0
        || messageSearchLinks;
    public bool ShowClearMessageSearchFilters => IsCurrentMessageSearch() && HasMessageSearchFilters
        && hasSearchedMessages && !IsSearchingMessages && MessageSearchError is null && MessageSearchResults.Count == 0;
    public bool ShowSearchContinuation => hasSearchedMessages && messageSearchBefore.HasValue || MessageSearchError is not null;
    public string SearchContinuationText => MessageSearchError is not null ? Strings.Retry : Strings.ChatSearchEarlier;
    public string? MessageSearchError { get; private set; }
    public string? MessageSearchNotice => IsSearchingMessages || MessageSearchError is not null || MessageSearchResults.Count > 0 ? null
        : !hasSearchedMessages ? Strings.ChatSearchLocalHint
        : Strings.ChatSearchEmpty;
    public string MessageSearchText
    {
        get;
        set
        {
            value ??= "";
            if (field == value) return;
            field = value;
            ResetMessageSearchResults();
            OnPropertyChanged();
        }
    } = "";

    bool HasCurrentMessageSearchContext() => !switchingConversation
        && IsCurrentMessageContext(searchWallet, searchAccountId, searchAddress);

    bool IsCurrentMessageSearch() => ShowMessageSearch && HasCurrentMessageSearchContext();

    bool CanReturnToMessageSearch => !ShowMessageSearch && hasSearchedMessages && selectedSearchResult is not null
        && HasCurrentMessageSearchContext();

    void OnOpenMessageSearchClicked(object sender, EventArgs e)
    {
        if (!CanOpenMessageSearch) return;
        bool resume = HasCurrentMessageSearchContext() && hasSearchedMessages;
        if (!resume) ClearMessageSearch();
        CancelQuoteNavigation();
        scrollRevision++;
        searchWallet = walletProvider.GetWallet();
        searchAccountId = localAccountId;
        searchAddress = Address;
        var view = EnsureChatView(messageSearchView);
        messageSearchEntry ??= view.FindByName<Entry>("messageSearchEntry");
        messageSearchResultsList ??= view.FindByName<CollectionView>("messageSearchResultsList");
        ShowMessageSearch = true;
        restoreSearchResultPending = resume && selectedSearchResult is not null;
        int revision = ++messageSearchViewRevision;
        messageEditor.Unfocus();
        NotifyMessageSearch();
        Dispatcher.Dispatch(() =>
        {
            if (revision != messageSearchViewRevision || !IsCurrentMessageSearch()) return;
            if (resume) RestoreSearchResultPosition();
            else messageSearchEntry.Focus();
        });
    }

    void OnMessageSearchResultsLayoutChanged(object sender, EventArgs e) => RestoreSearchResultPosition();

    void RestoreSearchResultPosition()
    {
        if (!restoreSearchResultPending || !IsCurrentMessageSearch() || messageSearchResultsList is not { IsLoaded: true }
            || messageSearchResultsList.Width <= 0 || messageSearchResultsList.Height <= 0) return;
        restoreSearchResultPending = false;
        if (selectedSearchResult is { } target && MessageSearchResults.Contains(target))
            messageSearchResultsList.ScrollTo(target, position: ScrollToPosition.Center, animate: false);
    }

    async void OnSearchMessagesClicked(object sender, EventArgs e) => await SearchMessagesAsync();
    async void OnSearchEarlierMessagesClicked(object sender, EventArgs e) => await SearchMessagesAsync(earlier: true);

    async void OnMessageSearchButtonClicked(object sender, EventArgs e) => await SearchOrCancelMessagesAsync();

    async void OnMessageSearchFilterClicked(object sender, EventArgs e) => await ChooseMessageSearchFilterAsync();

    async Task ChooseMessageSearchFilterAsync()
    {
        if (choosingSearchFilter || choosingSearchSender || choosingSearchPeriod || showingMessageActions || forwardingMessage
            || !IsCurrentMessageSearch()) return;
        choosingSearchFilter = true;
        bool chooseSender = false;
        bool choosePeriod = false;
        bool clearFilters = false;
        bool toggleLinks = false;
        int revision = messageSearchViewRevision;
        bool IsCurrent() => revision == messageSearchViewRevision && IsCurrentMessageSearch();
        try
        {
            string sender = $"{Strings.ChatSearchSender} · {MessageSearchSenderText}";
            string period = $"{Strings.ChatSearchPeriod} · {MessageSearchPeriodText}";
            string links = Strings.ChatLinksOnly + (messageSearchLinks ? " ✓" : "");
            List<string> actions = [sender, period, links];
            if (HasMessageSearchFilters) actions.Add(Strings.ChatClearSearchFilters);
            string choice = await DisplayActionSheetAsync(Strings.ChatSearchFilters, Strings.Cancel, null, [.. actions]);
            if (!IsCurrent()) return;
            chooseSender = choice == sender;
            choosePeriod = choice == period;
            toggleLinks = choice == links;
            clearFilters = choice == Strings.ChatClearSearchFilters;
        }
        catch (Exception ex)
        {
            if (IsCurrent())
            {
                MessageSearchError = ex.Message;
                NotifyMessageSearch();
            }
        }
        finally { choosingSearchFilter = false; }
        if (chooseSender) await ChooseMessageSearchSenderAsync();
        else if (choosePeriod) await ChooseMessageSearchPeriodAsync();
        else if (clearFilters) await ClearMessageSearchFiltersAsync();
        else if (toggleLinks)
        {
            messageSearchLinks = !messageSearchLinks;
            ResetMessageSearchResults();
            if (CanSearchMessages) await SearchMessagesAsync();
        }
    }

    async void OnClearMessageSearchFiltersClicked(object sender, EventArgs e)
    {
        if (ShowClearMessageSearchFilters) await ClearMessageSearchFiltersAsync();
    }

    async Task ClearMessageSearchFiltersAsync()
    {
        if (!HasMessageSearchFilters || choosingSearchFilter || choosingSearchSender || choosingSearchPeriod
            || showingMessageActions || forwardingMessage || !IsCurrentMessageSearch()) return;
        messageSearchSender = ChatSearchSender.All;
        messageSearchDays = 0;
        messageSearchLinks = false;
        // Keep the keyword; retire the filtered batch and its cursor before scanning all history.
        ResetMessageSearchResults();
        if (CanSearchMessages) await SearchMessagesAsync();
    }

    async Task ChooseMessageSearchPeriodAsync()
    {
        if (choosingSearchPeriod || choosingSearchSender || choosingSearchFilter || showingMessageActions || forwardingMessage || !IsCurrentMessageSearch()) return;
        choosingSearchPeriod = true;
        int revision = messageSearchViewRevision;
        bool IsCurrent() => revision == messageSearchViewRevision && IsCurrentMessageSearch();
        try
        {
            string choice = await DisplayActionSheetAsync(Strings.ChatSearchPeriod, Strings.Cancel, null,
                Strings.All, Strings.ChatLast7Days, Strings.ChatLast30Days);
            if (!IsCurrent()) return;
            int? days = choice == Strings.All ? 0 : choice == Strings.ChatLast7Days ? 7 : choice == Strings.ChatLast30Days ? 30 : null;
            if (days is null || days == messageSearchDays) return;
            messageSearchDays = days.Value;
            ResetMessageSearchResults();
        }
        catch (Exception ex)
        {
            if (IsCurrent())
            {
                MessageSearchError = ex.Message;
                NotifyMessageSearch();
            }
            return;
        }
        finally { choosingSearchPeriod = false; }
        if (CanSearchMessages) await SearchMessagesAsync();
    }

    async Task ChooseMessageSearchSenderAsync()
    {
        if (choosingSearchSender || choosingSearchPeriod || choosingSearchFilter || showingMessageActions || forwardingMessage || !IsCurrentMessageSearch()) return;
        choosingSearchSender = true;
        int revision = messageSearchViewRevision;
        bool IsCurrent() => revision == messageSearchViewRevision && IsCurrentMessageSearch();
        try
        {
            string choice = await DisplayActionSheetAsync(Strings.ChatSearchSender, Strings.Cancel, null,
                Strings.All, Strings.ChatYou, Strings.ChatOtherParticipant);
            if (!IsCurrent()) return;
            ChatSearchSender? selected = choice == Strings.All ? ChatSearchSender.All
                : choice == Strings.ChatYou ? ChatSearchSender.Self
                : choice == Strings.ChatOtherParticipant ? ChatSearchSender.Peer : null;
            if (selected is null || selected == messageSearchSender) return;
            messageSearchSender = selected.Value;
            // A different sender starts a new scan; old results and continuation belong to the previous filter.
            ResetMessageSearchResults();
        }
        catch (Exception ex)
        {
            if (IsCurrent())
            {
                MessageSearchError = ex.Message;
                NotifyMessageSearch();
            }
            return;
        }
        finally { choosingSearchSender = false; }
        if (CanSearchMessages) await SearchMessagesAsync();
    }

    Task SearchOrCancelMessagesAsync()
    {
        if (!IsCurrentMessageSearch()) return Task.CompletedTask;
        if (messageSearchOperation is not { } cancellation) return SearchMessagesAsync();
        // Retire this batch immediately. Its late completion cannot replace retained results or a new query.
        messageSearchOperation = null;
        cancellation.Cancel();
        NotifyMessageSearch();
        return Task.CompletedTask;
    }

    async Task SearchMessagesAsync(bool earlier = false)
    {
        if (!CanSearchMessages || earlier && !ShowSearchContinuation) return;
        if (!earlier) ResetMessageSearchResults();
        // Freeze the lower bound for continuation and retries, even if the search spans midnight.
        if (messageSearchSince is null && messageSearchDays > 0)
            messageSearchSince = DateTimeOffset.UtcNow.AddDays(-messageSearchDays);
        using var cancellation = new CancellationTokenSource();
        messageSearchOperation = cancellation;
        string term = MessageSearchText.Trim();
        string accountId = searchAccountId!;
        string address = searchAddress;
        MessageSearchError = null;
        messageSearchEntry?.Unfocus();
        NotifyMessageSearch();
        bool IsCurrent() => ReferenceEquals(messageSearchOperation, cancellation) && IsCurrentMessageSearch();
        try
        {
            if (!hasSearchedMessages) AddPendingSearchResults(accountId, address, term);
            long? before = messageSearchBefore;
            while (true)
            {
                cancellation.Token.ThrowIfCancellationRequested();
                if (!IsCurrent()) return;
                var batch = await messagingService.SearchChatMessagesAsync(accountId, address, term,
                    before, messageSearchSender, createdSince: messageSearchSince,
                    linksOnly: messageSearchLinks, cancellationToken: cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                if (!IsCurrent()) return;
                if (batch.Messages.Length == 0 && batch.NextBefore is { } next)
                {
                    // Skip nonmatching local batches while leaving the UI responsive to cancellation.
                    // Commit the cursor only with a result or exhaustion, so cancellation can resume safely.
                    before = next;
                    await Task.Yield();
                    continue;
                }
                foreach (var message in batch.Messages)
                {
                    if (MessageSearchResults.Any(result => result.Key == message.Key)) continue;
                    string text = message.Body!.Text!;
                    var result = new ChatSearchResult(message, message.Key.Sender == accountId ? Strings.ChatYou : DisplayName,
                        message.CreatedAt.ToLocalTime().ToString("g"), SearchPreview(text, term));
                    // Merge local failures by creation time without changing SDK history order or its cursor.
                    int index = MessageSearchResults.Count;
                    while (index > 0 && MessageSearchResults[index - 1] is { Pending: not null } pending
                        && pending.Timestamp < result.Timestamp) index--;
                    MessageSearchResults.Insert(index, result);
                }
                messageSearchBefore = batch.NextBefore;
                hasSearchedMessages = true;
                break;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (IsCurrent()) MessageSearchError = ex.Message; }
        finally
        {
            // An obsolete completion cannot clear the next query's results or loading indicator.
            if (ReferenceEquals(messageSearchOperation, cancellation))
            {
                messageSearchOperation = null;
                if (!IsCurrentMessageSearch()) ClearMessageSearch();
                else NotifyMessageSearch();
            }
        }
    }

    void AddPendingSearchResults(string accountId, string address, string term)
    {
        if (messageSearchSender == ChatSearchSender.Peer) return;
        foreach (var pending in messagingService.GetPendingChatMessages(accountId, address).Reverse())
        {
            // Only pre-SDK failures/cancellations have no searchable saved original.
            if (pending.SendStatus is not null || pending.ErrorMessage is null && !pending.IsCanceled
                || messageSearchSince is { } since && pending.CreatedAt < since
                || string.IsNullOrWhiteSpace(pending.Text) || !pending.Text.Contains(term, StringComparison.OrdinalIgnoreCase)
                || messageSearchLinks && !ChatMessageLinks.FindOccurrences(pending.Text).Any()
                || MessageSearchResults.Any(result => result.PendingId == pending.Id)) continue;
            MessageSearchResults.Add(new(pending, Strings.ChatYou,
                pending.CreatedAt.ToLocalTime().ToString("g"), SearchPreview(pending.Text, term)));
        }
    }

    static FormattedString SearchPreview(string text, string term)
    {
        int match = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        int start = Math.Max(0, match - 40);
        if (start > 0 && char.IsLowSurrogate(text[start])) start--;
        int end = Math.Min(text.Length, start + 200);
        if (end < text.Length && char.IsLowSurrogate(text[end])) end--;
        var preview = new FormattedString();
        void Append(string value, bool matched = false)
        {
            if (value.Length == 0) return;
            string normalized = value.Replace('\r', ' ').Replace('\n', ' ');
            var attributes = matched ? FontAttributes.Bold : FontAttributes.None;
            if (preview.Spans.LastOrDefault() is { } previous && previous.FontAttributes == attributes)
                previous.Text += normalized;
            else preview.Spans.Add(new Span { Text = normalized, FontAttributes = attributes });
        }
        if (start > 0) Append("…");
        int offset = start;
        while (offset < end)
        {
            // Match against the original text, before flattening line breaks for the excerpt.
            int next = term.Length == 0 ? -1 : text.IndexOf(term, offset, StringComparison.OrdinalIgnoreCase);
            if (next < 0 || next >= end)
            {
                Append(text[offset..end]);
                break;
            }
            Append(text[offset..next]);
            int matchEnd = Math.Min(end, next + term.Length);
            Append(text[next..matchEnd], matched: true);
            offset = matchEnd;
        }
        if (end < text.Length) Append("…");
        return preview;
    }

    void OpenSearchResult(ChatSearchResult result)
    {
        if (!HasLoaded || !IsCurrentMessageSearch() || result is null || !MessageSearchResults.Contains(result)) return;
        if (result.Message is { } original)
        {
            // Search already supplied the verified original; navigation can reuse it when loading history.
            replyMessages[original.Key] = (original, DateTimeOffset.MaxValue);
            SuspendMessageSearch(result);
            RequestQuotedMessage(original.Key);
        }
        else if (Messages.FirstOrDefault(message => message.PendingId == result.PendingId) is { } pending)
        {
            SuspendMessageSearch(result);
            CancelQuoteNavigation();
            FocusQuotedMessage(pending);
        }
        else SearchResultActionError = Strings.ChatQuotedMessageUnavailable;
    }

    void SuspendMessageSearch(ChatSearchResult result)
    {
        // Keep the query, results and continuation cursor while viewing an original message.
        // Cancel only the in-flight batch; its cursor has not been committed and can be retried.
        selectedSearchResult = result;
        hasSearchedMessages = true;
        messageSearchOperation?.Cancel();
        messageSearchOperation = null;
        ShowMessageSearch = false;
        restoreSearchResultPending = false;
        messageSearchViewRevision++;
        messageSearchEntry?.Unfocus();
        NotifyMessageSearch();
    }

    void OnCloseMessageSearchClicked(object sender, EventArgs e) => CloseMessageSearch();

    void CloseMessageSearch()
    {
        if (!ShowMessageSearch) return;
        ClearMessageSearch();
        if (HasLoaded && followLatest && !switchingConversation
            && IsCurrentMessageContext(walletProvider.GetWallet(), localAccountId, Address))
        {
            QueueLatestMessageScroll();
        }
    }

    void ResetMessageSearchResults()
    {
        messageSearchViewRevision++;
        selectedSearchResult = null;
        restoreSearchResultPending = false;
        messageSearchOperation?.Cancel();
        messageSearchOperation = null;
        messageSearchBefore = null;
        messageSearchSince = null;
        hasSearchedMessages = false;
        MessageSearchError = null;
        SearchResultActionError = null;
        MessageSearchResults.Clear();
        NotifyMessageSearch();
    }

    void ClearMessageSearch()
    {
        ShowMessageSearch = false;
        searchWallet = null;
        searchAccountId = null;
        searchAddress = "";
        messageSearchEntry?.Unfocus();
        MessageSearchText = "";
        messageSearchSender = ChatSearchSender.All;
        messageSearchDays = 0;
        messageSearchLinks = false;
        ResetMessageSearchResults();
    }

    void NotifyMessageSearch()
    {
        OnPropertyChanged(nameof(ShowMessageSearch));
        OnPropertyChanged(nameof(IsChatContentCovered));
        OnPropertyChanged(nameof(CanOpenMessageSearch));
        OnPropertyChanged(nameof(ShowQuoteReturn));
        OnPropertyChanged(nameof(QuoteReturnText));
        OnPropertyChanged(nameof(IsSearchingMessages));
        OnPropertyChanged(nameof(CanSearchMessages));
        OnPropertyChanged(nameof(CanUseMessageSearchButton));
        OnPropertyChanged(nameof(MessageSearchButtonText));
        OnPropertyChanged(nameof(MessageSearchFilterText));
        OnPropertyChanged(nameof(ShowClearMessageSearchFilters));
        OnPropertyChanged(nameof(ShowSearchContinuation));
        OnPropertyChanged(nameof(SearchContinuationText));
        OnPropertyChanged(nameof(MessageSearchError));
        OnPropertyChanged(nameof(MessageSearchNotice));
    }
}
