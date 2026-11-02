using Neo.Wallets;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Properties;
using System.Collections.ObjectModel;

namespace NeoOrder.OneGate.Pages;

public partial class HomePage
{
    readonly IWalletProvider walletProvider;
    Wallet? conversationsWallet;
    ChatConversationViewModel[] allConversations = [];
    Dictionary<string, string>? conversationLabels;
    bool openingConversation;

    public ObservableCollection<ChatConversationViewModel> Conversations { get; } = [];
    public bool HasLoadedConversations { get; private set { field = value; OnPropertyChanged(); } }
    public string? ConversationsError { get; private set { field = value; OnPropertyChanged(); } }
    public bool IsConversationSearchVisible { get; private set { field = value; OnPropertyChanged(); } }
    public bool ShowConversationLoading => IsChatBusy && !HasLoadedConversations;
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
        bool IsCurrent() => revision == conversationFilterRevision && isPageVisible && SelectedHomeSection == 0 && IsConversationSearchVisible && !openingConversation
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
            field = value ?? "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(ConversationsEmptyTitle));
            OnPropertyChanged(nameof(HasConversationFilter));
            FilterConversations();
        }
    } = "";

    async Task ReadConversationsAsync(CancellationToken cancellationToken)
    {
        var wallet = walletProvider.GetWallet();
        var (accountId, conversations) = await messagingService.GetChatConversationsAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        // Reuse address-book labels while this page is visible; returning from editing reloads them.
        var labels = conversationLabels ?? (await addressBookService.GetEntriesAsync(cancellationToken))
            .ToDictionary(p => p.Address, p => p.Label, StringComparer.Ordinal);
        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(walletProvider.GetWallet(), wallet) || accountId != messagingService.AccountId)
            return;

        conversationLabels = labels;
        conversationsWallet = wallet;
        allConversations = conversations.Select(p => new ChatConversationViewModel(accountId, p.Address,
            messagingService.GetChatDisplayName(accountId, p.Address, labels.GetValueOrDefault(p.Address)), p.Text, p.UpdatedAt, p.UnreadCount, p.Draft, p.HasMessages) { LastMessageAt = p.LastMessageAt, SendErrorState = p.SendErrorState, IsPinned = p.IsPinned }).ToArray();
        FilterConversations();
        HasLoadedConversations = true;
        ConversationsError = null;
    }

    void FilterConversations()
    {
        string filter = ConversationSearchText.Trim();
        var conversations = allConversations.Where(p => p.MatchesFilter(ConversationFilter) && p.MatchesSearch(filter))
            .OrderByDescending(p => p.IsPinned).ThenByDescending(p => p.UpdatedAt)
            .ThenBy(p => p.Address, StringComparer.Ordinal).ToArray();
        // Keep unaffected rows when a preview, unread count, or conversation order changes.
        for (int i = 0; i < conversations.Length; i++)
        {
            var conversation = conversations[i];
            if (i < Conversations.Count && Conversations[i] == conversation) continue;
            int existingIndex = -1;
            for (int j = i; j < Conversations.Count; j++)
                if (Conversations[j].LocalAccountId == conversation.LocalAccountId && Conversations[j].Address == conversation.Address)
                {
                    existingIndex = j;
                    break;
                }
            if (existingIndex < 0) Conversations.Insert(i, conversation);
            else
            {
                if (existingIndex != i) Conversations.Move(existingIndex, i);
                if (Conversations[i] != conversation) Conversations[i] = conversation;
            }
        }
        while (Conversations.Count > conversations.Length) Conversations.RemoveAt(Conversations.Count - 1);
    }

    void OnConversationListHandlerChanged(object sender, EventArgs e)
    {
#if ANDROID
        if (sender is CollectionView { Handler.PlatformView: AndroidX.RecyclerView.Widget.RecyclerView list }
            && list.GetItemAnimator() is AndroidX.RecyclerView.Widget.SimpleItemAnimator animator)
            animator.SupportsChangeAnimations = false;
#endif
    }

    void ClearConversations()
    {
        allConversations = [];
        Conversations.Clear();
        HasLoadedConversations = false;
        ConversationsError = null;
        conversationsWallet = null;
        conversationFilterRevision++;
        ConversationFilter = ChatConversationFilter.All;
        ConversationSearchText = "";
        IsConversationSearchVisible = false;
    }

    void OnHomeWalletChanged(object? sender, Wallet? wallet) => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (!isPageVisible) return;
        chatLoadCancellation?.Cancel();
        contactsLoadCancellation?.Cancel();
        chatActivationCancellation?.Cancel();
        markConversationReadCancellation?.Cancel();
        ClearConversations();
        HasChatState = false;
        HasPendingChatActivation = false;
        IsChatEnabled = false;
        ChatAddress = null;
        ChatError = null;
        NotifyChatState();
        RefreshPendingRequestCount();
        UpdateSectionActivity();
    });

    void OnConversationsRetryClicked(object sender, EventArgs e) => LoadChatState();

    async Task OpenConversationAsync(ChatConversationViewModel conversation)
    {
        var wallet = walletProvider.GetWallet();
        bool IsCurrent() => ReferenceEquals(Shell.Current?.CurrentPage, this) && CanUseConversation(conversation)
            && messagingService.IsEnabled && ReferenceEquals(wallet, walletProvider.GetWallet())
            && wallet?.GetDefaultAccount()?.Address == messagingService.Address;
        if (openingConversation || showingConversationActions || !IsCurrent()) return;
        openingConversation = true;
        ConversationsError = null;
        conversationSearch.Unfocus();
        try
        {
            await Shell.Current.GoToAsync("chat", new ShellNavigationQueryParameters { ["address"] = conversation.Address });
        }
        catch (Exception ex) { if (IsCurrent()) ConversationsError = ex.Message; }
        finally { openingConversation = false; }
    }
}
