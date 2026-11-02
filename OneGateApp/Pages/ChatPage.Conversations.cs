using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services.Messaging;

namespace NeoOrder.OneGate.Pages;

public partial class ChatPage
{
    const double ConversationPaneBreakpoint = 880;
    Dictionary<string, string>? conversationLabels;
    ChatConversationViewModel[] allConversations = [];
    bool switchingConversation;
    bool changingConversationPin;
    bool showingConversationActions;
    CancellationTokenSource? markConversationReadCancellation;

    public Command<ChatConversationViewModel> OpenConversationCommand { get; }
    public Command<ChatConversationViewModel> ConversationActionsCommand { get; }
    public bool UseConversationPane { get; private set; }
    public ChatConversationViewModel[] Conversations { get; private set; } = [];
    public ChatConversationViewModel? CurrentConversation => Conversations.FirstOrDefault(p => p.Address == Address);
    public string? ConversationsError { get; private set; }
    public bool HasLoadedConversations { get; private set; }
    public bool ShowConversationLoading => IsLoading && !HasLoadedConversations;
    public string ConversationsEmptyTitle => !string.IsNullOrWhiteSpace(ConversationSearchText) ? Strings.ChatNoMatchingConversations
        : ConversationFilter switch
        {
            ChatConversationFilter.Unread => Strings.ChatNoUnreadConversations,
            _ => Strings.ChatHistoryEmpty
        };

    bool choosingConversationFilter;
    int conversationFilterRevision;
    public bool HasConversationTypeFilter => ConversationFilter != ChatConversationFilter.All;
    public bool HasConversationFilter => HasConversationTypeFilter || !string.IsNullOrWhiteSpace(ConversationSearchText);
    public string ConversationFilterText => ConversationFilter switch
    {
        ChatConversationFilter.Unread => Strings.ChatUnreadOnly,
        _ => Strings.All
    };
    public ChatConversationFilter ConversationFilter
    {
        get;
        set
        {
            if (field == value || !Enum.IsDefined(value)) return;
            field = value;
            conversationFilterRevision++;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ConversationFilterText));
            OnPropertyChanged(nameof(HasConversationTypeFilter));
            OnPropertyChanged(nameof(HasConversationFilter));
            OnPropertyChanged(nameof(ConversationsEmptyTitle));
            FilterConversations();
        }
    }

    async void OnConversationFilterClicked(object sender, EventArgs e)
    {
        var wallet = walletProvider.GetWallet();
        string? accountId = messagingService.AccountId;
        int revision = conversationFilterRevision;
        bool IsCurrent() => revision == conversationFilterRevision && isVisible && UseConversationPane && !switchingConversation && !openingDetails
            && !ShowForwardRecipientPicker && localAccountId == accountId
            && IsChatEnabled && messagingService.IsEnabled && accountId is not null && accountId == messagingService.AccountId
            && ReferenceEquals(Shell.Current?.CurrentPage, this) && ReferenceEquals(wallet, walletProvider.GetWallet())
            && wallet?.GetDefaultAccount() is { } account && account.Address == messagingService.Address;
        if (choosingConversationFilter || !IsCurrent()) return;
        choosingConversationFilter = true;
        try
        {
            string choice = await DisplayActionSheetAsync(Strings.ChatConversationFilter, Strings.Cancel, null,
                Strings.All, Strings.ChatUnreadOnly);
            if (!IsCurrent()) return;
            if (choice == Strings.All) ConversationFilter = ChatConversationFilter.All;
            else if (choice == Strings.ChatUnreadOnly) ConversationFilter = ChatConversationFilter.Unread;
        }
        catch (Exception ex)
        {
            if (IsCurrent())
            {
                ConversationsError = ex.Message;
                OnPropertyChanged(nameof(ConversationsError));
            }
        }
        finally { choosingConversationFilter = false; }
    }

    void OnClearConversationFilterClicked(object sender, EventArgs e)
    {
        conversationFilterRevision++;
        ConversationFilter = ChatConversationFilter.All;
        ConversationSearchText = "";
    }

    public string ConversationSearchText
    {
        get;
        set
        {
            if (field == value) return;
            field = value ?? "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(ConversationsEmptyTitle));
            OnPropertyChanged(nameof(HasConversationFilter));
            FilterConversations();
        }
    } = "";

    void OnChatLayoutSizeChanged(object sender, EventArgs e)
    {
        // Use the space remaining after the application's navigation sidebar.
        bool usePane = (DeviceInfo.Idiom == DeviceIdiom.Desktop || DeviceInfo.Idiom == DeviceIdiom.Tablet)
            && chatLayout.Width >= ConversationPaneBreakpoint;
        if (usePane != UseConversationPane)
        {
            conversationListColumn.Width = new GridLength(usePane ? 320 : 0);
            conversationDividerColumn.Width = new GridLength(usePane ? 1 : 0);
            if (usePane) EnsureChatView(conversationPane);
            UseConversationPane = usePane;
            OnPropertyChanged(nameof(UseConversationPane));
            OnPropertyChanged(nameof(ShowOtherUnread));
            RefreshOtherUnread();
            if (usePane) RefreshConversationList();
        }
    }

    void RefreshConversationList()
    {
        if (!isVisible || !UseConversationPane) return;
        reloadPending |= MessagingChangeKind.Records;
        ReloadIfIdle();
    }

    void OnConversationListRetryClicked(object sender, EventArgs e) => RefreshConversationList();

    async Task ReadConversationListAsync(CancellationToken cancellationToken)
    {
        if (!UseConversationPane || !IsChatEnabled) return;
        try
        {
            var (accountId, conversations) = await messagingService.GetChatConversationsAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var labels = conversationLabels ?? (await addressBookService.GetEntriesAsync(cancellationToken))
                .ToDictionary(p => p.Address, p => p.Label, StringComparer.Ordinal);
            cancellationToken.ThrowIfCancellationRequested();
            if (!isVisible || accountId != localAccountId || accountId != messagingService.AccountId) return;
            conversationLabels = labels;
            allConversations = conversations.Select(p => new ChatConversationViewModel(accountId, p.Address,
                messagingService.GetChatDisplayName(accountId, p.Address, labels.GetValueOrDefault(p.Address)),
                p.Text, p.UpdatedAt, p.UnreadCount, p.Draft, p.HasMessages) { LastMessageAt = p.LastMessageAt, SendErrorState = p.SendErrorState, IsPinned = p.IsPinned }).ToArray();
            FilterConversations();
            HasLoadedConversations = true;
            ConversationsError = null;
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) { ConversationsError = ex.Message; }
        OnPropertyChanged(nameof(HasLoadedConversations));
        OnPropertyChanged(nameof(ConversationsError));
        OnPropertyChanged(nameof(ShowConversationLoading));
    }

    void UpdateCurrentConversationDraft()
    {
        if (!UseConversationPane || draftConversation is not { } conversation) return;
        int index = Array.FindIndex(allConversations, p => p.LocalAccountId == conversation.AccountId
            && p.Address == conversation.Address);
        string? draft = string.IsNullOrWhiteSpace(Draft) ? HasReplyDraft ? Strings.ChatQuotedMessage : null : Draft;
        var current = index >= 0 ? allConversations[index]
            : new ChatConversationViewModel(conversation.AccountId, conversation.Address, ChatContact?.DisplayName,
                null, DateTimeOffset.MinValue, 0, null, false);
        if (draft is null)
        {
            // Sending clears the editor before a summary refresh. Preserve the local pending message.
            var pending = messagingService.GetPendingChatMessages(conversation.AccountId, conversation.Address).LastOrDefault();
            var latest = Messages.MaxBy(p => p.Timestamp);
            if (pending is null && latest is null && !current.HasMessages)
            {
                if (!current.IsPinned)
                {
                    if (index >= 0) allConversations = allConversations.Where((_, i) => i != index).ToArray();
                    return;
                }
                current = current with { Text = null, UpdatedAt = DateTimeOffset.MinValue };
            }
            else if (pending?.CreatedAt >= current.LastMessageAt || latest?.Timestamp >= current.LastMessageAt)
            {
                bool usePending = pending is not null && (latest is null || pending.CreatedAt >= latest.Timestamp);
                current = current with
                {
                    Text = usePending ? pending!.Text : latest!.CopyText,
                    LastMessageAt = usePending ? pending!.CreatedAt : latest!.Timestamp,
                    HasMessages = true,
                    SendErrorState = usePending
                        ? new ChatMessageViewModel(pending!).SendErrorState : latest!.SendErrorState
                };
            }
        }
        var draftUpdatedAt = draft is null ? null
            : messagingService.GetChatDraftUpdatedAt(conversation.AccountId, conversation.Address);
        current = current with
        {
            Draft = draft,
            UpdatedAt = draftUpdatedAt is { } editedAt && editedAt > current.LastMessageAt ? editedAt : current.LastMessageAt
        };
        if (index < 0)
        {
            allConversations = [current, .. allConversations];
            return;
        }
        if (current == allConversations[index]) return;
        // Conversations can share this array; replace it so bindings observe the changed row.
        allConversations = [.. allConversations];
        allConversations[index] = current;
    }

    void FilterConversations()
    {
        // Apply the editor's latest draft even when an older background snapshot has just completed.
        UpdateCurrentConversationDraft();
        string filter = ConversationSearchText.Trim();
        var updated = allConversations.Where(p => p.MatchesFilter(ConversationFilter) && p.MatchesSearch(filter))
            .OrderByDescending(p => p.IsPinned).ThenByDescending(p => p.UpdatedAt)
            .ThenBy(p => p.Address, StringComparer.Ordinal).ToArray();
        if (!Conversations.SequenceEqual(updated))
        {
            Conversations = updated;
            OnPropertyChanged(nameof(Conversations));
        }
        OnPropertyChanged(nameof(CurrentConversation));
    }

    async Task OpenConversationAsync(ChatConversationViewModel conversation)
    {
        if (switchingConversation || openingDetails || showingConversationActions
            || conversation.Address == Address || !Conversations.Contains(conversation)) return;
        var wallet = walletProvider.GetWallet();
        string address = Address;
        bool IsCurrent() => isVisible && UseConversationPane && IsChatEnabled && messagingService.IsEnabled
            && ReferenceEquals(Shell.Current?.CurrentPage, this) && Address == address
            && localAccountId == conversation.LocalAccountId && localAccountId == messagingService.AccountId
            && ReferenceEquals(wallet, walletProvider.GetWallet())
            && wallet?.GetDefaultAccount()?.Address == messagingService.Address;
        if (!IsCurrent()) return;
        switchingConversation = true;
        ConversationsError = null;
        OnPropertyChanged(nameof(ConversationsError));
        try
        {
            // Replace the current route without stacking chat pages; Shell may reuse this page instance.
            await Shell.Current.GoToAsync("../chat", false,
                new ShellNavigationQueryParameters { ["address"] = conversation.Address });
        }
        catch (Exception ex)
        {
            if (IsCurrent())
            {
                ConversationsError = ex.Message;
                OnPropertyChanged(nameof(ConversationsError));
            }
        }
        finally
        {
            switchingConversation = false;
            OnPropertyChanged(nameof(CurrentConversation));
        }
    }

    bool CanUseConversation(ChatConversationViewModel conversation) => isVisible && UseConversationPane
        && !switchingConversation && ReferenceEquals(Shell.Current?.CurrentPage, this) && messagingService.IsEnabled
        && IsChatEnabled && conversation.LocalAccountId == localAccountId && localAccountId == messagingService.AccountId
        && walletProvider.GetWallet()?.GetDefaultAccount()?.Address == messagingService.Address
        && Conversations.Any(p => p.LocalAccountId == conversation.LocalAccountId && p.Address == conversation.Address);

    bool CanMarkConversationRead(ChatConversationViewModel conversation) => CanUseConversation(conversation)
        && conversation.HasUnread && Conversations.Any(p => p.LocalAccountId == conversation.LocalAccountId
            && p.Address == conversation.Address && p.HasUnread);

    async Task ShowConversationActionsAsync(ChatConversationViewModel conversation)
    {
        if (showingConversationActions || openingDetails || changingConversationPin || markConversationReadCancellation is not null
            || !CanUseConversation(conversation)) return;
        showingConversationActions = true;
        var wallet = walletProvider.GetWallet();
        try
        {
            List<string> actions = [conversation.PinActionText];
            if (conversation.HasUnread) actions.Add(Strings.ChatMarkAsRead);
            string action = await DisplayActionSheetAsync(conversation.Title, Strings.Cancel, null, [.. actions]);
            if (!ReferenceEquals(wallet, walletProvider.GetWallet())) return;
            if (action == conversation.PinActionText) await SetConversationPinnedAsync(conversation, !conversation.IsPinned);
            else if (action == Strings.ChatMarkAsRead) await MarkConversationReadAsync(conversation);
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(wallet, walletProvider.GetWallet()) && CanUseConversation(conversation))
            {
                ConversationsError = ex.Message;
                OnPropertyChanged(nameof(ConversationsError));
            }
        }
        finally
        {
            showingConversationActions = false;
            OnPropertyChanged(nameof(CurrentConversation));
        }
    }

    async void OnPinConversationClicked(object sender, EventArgs e)
    {
        if (sender is MenuFlyoutItem { CommandParameter: ChatConversationViewModel conversation })
            await SetConversationPinnedAsync(conversation, !conversation.IsPinned);
    }

    async Task SetConversationPinnedAsync(ChatConversationViewModel conversation, bool pinned)
    {
        if (changingConversationPin || !CanUseConversation(conversation)) return;
        changingConversationPin = true;
        var wallet = walletProvider.GetWallet();
        try
        {
            await messagingService.SetChatPinnedAsync(conversation.LocalAccountId, conversation.Address, pinned);
            if (!ReferenceEquals(wallet, walletProvider.GetWallet()) || !CanUseConversation(conversation)) return;
            allConversations = allConversations.Select(p => p.LocalAccountId == conversation.LocalAccountId
                && p.Address == conversation.Address ? p with { IsPinned = pinned } : p).ToArray();
            FilterConversations();
            ConversationsError = null;
            OnPropertyChanged(nameof(ConversationsError));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (ReferenceEquals(wallet, walletProvider.GetWallet()) && CanUseConversation(conversation))
            {
                ConversationsError = ex.Message;
                OnPropertyChanged(nameof(ConversationsError));
            }
        }
        finally { changingConversationPin = false; }
    }

    async void OnMarkConversationReadClicked(object sender, EventArgs e)
    {
        if (sender is MenuFlyoutItem { CommandParameter: ChatConversationViewModel conversation })
            await MarkConversationReadAsync(conversation);
    }

    async Task MarkConversationReadAsync(ChatConversationViewModel conversation)
    {
        if (markConversationReadCancellation is not null || !CanMarkConversationRead(conversation)) return;
        using var cancellation = new CancellationTokenSource();
        markConversationReadCancellation = cancellation;
        var wallet = walletProvider.GetWallet();
        try
        {
            await messagingService.MarkChatReadAsync(conversation.LocalAccountId, conversation.Address,
                cancellationToken: cancellation.Token);
            // The service refreshes both the sidebar and home badge through its change event.
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!cancellation.IsCancellationRequested && ReferenceEquals(wallet, walletProvider.GetWallet())
                && CanMarkConversationRead(conversation))
            {
                ConversationsError = ex.Message;
                OnPropertyChanged(nameof(ConversationsError));
            }
        }
        finally
        {
            if (ReferenceEquals(markConversationReadCancellation, cancellation)) markConversationReadCancellation = null;
        }
    }

    void ClearConversationList()
    {
        markConversationReadCancellation?.Cancel();
        markConversationReadCancellation = null;
        conversationLabels = null;
        allConversations = [];
        conversationFilterRevision++;
        ConversationFilter = ChatConversationFilter.All;
        ConversationSearchText = "";
        FilterConversations();
        HasLoadedConversations = false;
        ConversationsError = null;
        OnPropertyChanged(nameof(HasLoadedConversations));
        OnPropertyChanged(nameof(ConversationsError));
        OnPropertyChanged(nameof(ShowConversationLoading));
    }
}
