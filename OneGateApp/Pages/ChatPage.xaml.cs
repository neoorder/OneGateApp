using Meshline.Models.Client;
using Neo.Wallets;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using NeoOrder.OneGate.Services.Messaging;
using System.Collections.ObjectModel;

namespace NeoOrder.OneGate.Pages;

public partial class ChatPage : ContentPage, IQueryAttributable
{
    readonly MessagingService messagingService;
    readonly AddressBookService addressBookService;
    readonly IWalletProvider walletProvider;
    CancellationTokenSource? operation;
    string? localAccountId;
    (string Account, string Device, string Network)? pushNavigationIdentity;
    bool isVisible;
    bool hasNavigatedTo;
    bool openingDetails;
    MessagingChangeKind reloadPending;
    string? receiveError;
    bool isLoadingEarlier;
    bool hasLoadedContact;
    bool followLatest = true;
    int firstVisibleIndex;

    public string? ChatConnectionNotice => IsChatEnabled && localAccountId == messagingService.AccountId
        && messagingService.ConnectionNotice != receiveError ? messagingService.ConnectionNotice : null;

    void OnChatConnectionChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(ChatConnectionNotice));

    public string Address { get; private set; } = "";
    public string DisplayName { get; private set; } = Strings.ChatConversation;
    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    void SetMessages(IReadOnlyList<ChatMessageViewModel> value)
    {
        DateOnly? previousDate = null;
        foreach (var message in value)
        {
            DateOnly date = message.LocalDate;
            message.ShowDateSeparator = previousDate != date;
            previousDate = date;
        }
        // Keep the native ItemsSource and unchanged rows; replacing it resets WinUI's scroll extent.
        int start = 0;
        while (start < Messages.Count && start < value.Count && ReferenceEquals(Messages[start], value[start])) start++;
        int oldEnd = Messages.Count, newEnd = value.Count;
        while (oldEnd > start && newEnd > start && ReferenceEquals(Messages[oldEnd - 1], value[newEnd - 1]))
        {
            oldEnd--;
            newEnd--;
        }
        for (int i = start; i < Math.Min(oldEnd, newEnd); i++) Messages[i] = value[i];
        while (Messages.Count > value.Count) Messages.RemoveAt(newEnd);
        for (int i = oldEnd; i < newEnd; i++) Messages.Insert(i, value[i]);
        UpdateReplyPreviews();
        NotifyScrollState();
    }
    public bool IsLoading => operation is not null;
    public bool ShowLoading => IsLoading && (!HasLoaded || requestedQuote is not null
        || quoteNavigation is { IsCancellationRequested: false });
    public bool IsChatEnabled { get; private set; }
    public bool CanLoadEarlier => IsChatEnabled && !IsLoading;
    public bool HasLoaded { get; private set; }
    public bool HasMore { get; private set; }
    public string EarlierMessagesButtonText => IsLoading && isLoadingEarlier ? Strings.Loading
        : EarlierMessagesError is not null ? Strings.Retry : Strings.ChatLoadEarlier;
    public string? EarlierMessagesError
    {
        get;
        private set
        {
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(EarlierMessagesButtonText));
        }
    }
    bool HasChatContact { get; set; }
    public bool ShowContactRequired => IsChatEnabled && HasLoaded && !HasChatContact;
    bool HasSavedContact => ChatContact?.IsSaved == true;
    public bool CanOpenContactDetails => ChatContact is not null && IsChatEnabled && (HasChatContact || HasSavedContact || ChatContact.HasOutgoingRequest) && !ShowForwardRecipientPicker;
    string? ErrorText
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged(nameof(ErrorNotice));
            OnPropertyChanged(nameof(ShowRetry));
        }
    }
    public string? ErrorNotice => ErrorText ?? receiveError;
    public bool ShowRetry => !IsLoading && ErrorText is not null;
    public string BusyText { get; private set { field = value; OnPropertyChanged(); } } = Strings.Loading;

    public ChatPage(MessagingService messagingService, AddressBookService addressBookService, IWalletProvider walletProvider)
    {
        this.messagingService = messagingService;
        this.addressBookService = addressBookService;
        this.walletProvider = walletProvider;
        BackCommand = new(async () =>
        {
            if (!OnBackButtonPressed()) await Shell.Current.GoToAsync("..");
        });
        MessageActionsCommand = new(async message => await ShowMessageActionsAsync(message));
        OpenSearchResultCommand = new(OpenSearchResult);
        OpenConversationCommand = new(async conversation => await OpenConversationAsync(conversation));
        ConversationActionsCommand = new(async conversation => await ShowConversationActionsAsync(conversation));
        InitializeComponent();
    }

    static View EnsureChatView(ContentView host)
    {
        // Keep optional surfaces out of the navigation path; reuse them once opened.
        host.Content ??= (View)((DataTemplate)host.Resources["DeferredContent"]).CreateContent();
        return host.Content;
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        pushNavigationIdentity = query.TryGetValue("pushIdentity", out var expected) && expected is ValueTuple<string, string, string> identity ? identity : null;
        if (query.TryGetValue("address", out var address))
        {
            string value = ((string)address).Trim();
            if (Address == value) return;
            // Shell can reuse this page when replacing the current chat route.
            operation?.Cancel();
            operation = null;
            reloadPending = 0;
            ClearHistory(keepConversationList: true);
            Address = value;
            ErrorText = null;
            SendErrorText = null;
            NotifyState();
            OnPropertyChanged(nameof(CurrentConversation));
            if (isVisible) _ = LoadAsync();
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        isVisible = true;
        walletProvider.WalletChanged += OnWalletChanged;
        messagingService.Changed += OnMessagingChanged;
        messagingService.ConnectionChanged += OnChatConnectionChanged;
        messagingService.SynchronizationChanged += OnSynchronizationChanged;
        messagingService.DraftStorageChanged += OnDraftStorageChanged;
        messagingService.UnreadCountChanged += OnOtherUnreadChanged;
        await LoadAsync();
    }

    protected override async void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        ConfigureChatSoftInput();
        hasNavigatedTo = true;
        ObserveUnreadNavigation();
#if WINDOWS
        AttachTitleBarUnread();
#endif
        // Shell has completed its transition; binding message rows can now use the UI thread.
        await LoadAsync();
    }

    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        RestoreChatSoftInput();
        hasNavigatedTo = false;
        base.OnNavigatedFrom(args);
    }

    protected override void OnDisappearing()
    {
        var history = contactDetailsHistory;
        isVisible = false;
        StopObservingUnreadNavigation();
#if WINDOWS
        DetachTitleBarUnread();
#endif
        walletProvider.WalletChanged -= OnWalletChanged;
        messagingService.Changed -= OnMessagingChanged;
        messagingService.ConnectionChanged -= OnChatConnectionChanged;
        messagingService.SynchronizationChanged -= OnSynchronizationChanged;
        messagingService.DraftStorageChanged -= OnDraftStorageChanged;
        messagingService.UnreadCountChanged -= OnOtherUnreadChanged;
        reloadPending = 0;
        operation?.Cancel();
        operation = null;
        SendErrorText = null;
        ClearHistory();
        contactDetailsHistory = history;
        NotifyState();
        base.OnDisappearing();
    }

    void OnWalletChanged(object? sender, Wallet? wallet) => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (!isVisible || localAccountId is null) return;
        operation?.Cancel();
        SendErrorText = null;
        ClearHistory();
        ErrorText = Strings.ChatAccountChanged;
        NotifyState();
    });

    async void OnRetryHistoryClicked(object sender, EventArgs e)
    {
        if (ShowRetry) await LoadAsync();
    }

    void OnMessagingChanged(object? sender, MessagingChangedEventArgs e)
    {
        if (!isVisible || !hasNavigatedTo) return;
        if (e.Kind == MessagingChangeKind.ReadState && !UseConversationPane)
        {
            RefreshOtherUnread();
            return;
        }
        if (searchAccountId is not null && !HasCurrentMessageSearchContext()) ClearMessageSearch();
        RefreshPendingMessages();
        RefreshForwardRecipientNames();
        RefreshOtherUnread();
        reloadPending |= e.Kind;
        ReloadIfIdle();
    }

    async void ReloadIfIdle()
    {
        if (!isVisible || !hasNavigatedTo || IsLoading) return;
        if (requestedQuote is { } reference)
        {
            var origin = requestedQuoteOrigin;
            requestedQuote = null;
            requestedQuoteOrigin = null;
            await NavigateToQuoteAsync(reference, origin);
            NotifyQuoteNavigation();
            return;
        }
        if (reloadPending == 0) return;
        var changes = reloadPending;
        reloadPending = 0;
        await LoadAsync(changes: changes);
    }

    async void OnLoadEarlierClicked(object sender, EventArgs e)
    {
        if (CanLoadEarlier && HasMore) await LoadAsync(earlier: true);
    }

    async Task LoadAsync(bool earlier = false, MessagingChangeKind changes = MessagingChangeKind.Records)
    {
        if (IsLoading || !isVisible || !hasNavigatedTo) return;
        using var cancellation = new CancellationTokenSource();
        operation = cancellation;
        isLoadingEarlier = earlier;
        bool refreshRecords = earlier || !HasLoaded || ErrorText is not null || (changes & MessagingChangeKind.Records) != 0;
        if (earlier) EarlierMessagesError = null;
        ErrorText = null;
        BusyText = Strings.Loading;
        NotifyState();
        long? before = earlier ? Messages.FirstOrDefault(message => !message.IsPending)?.LocalSequence : null;
        bool verifiedAccount = false;
        bool historyReadCompleted = false;
        try
        {
            if (!refreshRecords && IsChatEnabled && messagingService.IsEnabled
                && IsCurrentMessageContext(walletProvider.GetWallet(), localAccountId, Address))
            {
                verifiedAccount = true;
                // Send acknowledgements and read positions do not change message text or contacts.
                if ((changes & MessagingChangeKind.SendStatus) != 0) await ReadSendStatusesAsync(cancellation.Token);
                await ReadConversationListAsync(cancellation.Token);
                return;
            }
            if (!await ValidatePushNavigationAsync(cancellation.Token)) return;
            var details = await messagingService.GetChatContactAsync(Address, resolveProfile: false, cancellation.Token);
            if (!await ValidatePushNavigationAsync(cancellation.Token, details.LocalAccountId)) return;
            pushNavigationIdentity = null;
            cancellation.Token.ThrowIfCancellationRequested();
            if (localAccountId is not null && localAccountId != details.LocalAccountId)
                throw new InvalidOperationException(Strings.ChatAccountChanged);
            localAccountId = details.LocalAccountId;
            verifiedAccount = true;
            IsChatEnabled = true;
            HasChatContact = details.IsContact;
            RestoreContactDetailsHistory();
            RestoreDraft();
            RefreshPendingMessages();
            // Reuse local names and notes until leaving the page or switching conversations.
            if (!hasLoadedContact)
            {
                var savedContact = await addressBookService.FindByAddressAsync(Address, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                ChatContact = savedContact is null ? null : new ContactViewModel(savedContact, details.Status, details.IsContact);
                hasLoadedContact = true;
            }
            var metadata = new Data.Contact
            {
                Address = Address,
                Label = HasSavedContact ? ChatContact!.Label : "",
                Note = HasSavedContact ? ChatContact!.Note : null
            };
            var contact = new ContactViewModel(metadata, details.Status, details.IsContact,
                details is { IsContact: false, Request.Direction: ContactRequestDirection.Incoming }, isSaved: HasSavedContact,
                displayName: messagingService.GetChatDisplayName(localAccountId, Address,
                    HasSavedContact ? ChatContact!.Label : null),
                hasOutgoingRequest: details is { IsContact: false, Request.Direction: ContactRequestDirection.Outgoing });
            if (ChatContact is null || ChatContact.Label != contact.Label || ChatContact.DisplayName != contact.DisplayName || ChatContact.ChatStatus != contact.ChatStatus
                || ChatContact.CanChat != contact.CanChat || ChatContact.HasOutgoingRequest != contact.HasOutgoingRequest
                || ChatContact.ChatRequestText != contact.ChatRequestText || ChatContact.ChatRequestRoute != contact.ChatRequestRoute)
                ChatContact = contact;
            DisplayName = contact.DisplayName ?? Address;
            NotifyState();
            RefreshOtherUnread();
            if (earlier || !HasLoaded) await ReadHistoryAsync(before, cancellation.Token);
            else await ReadNewHistoryAsync(cancellation.Token);
            historyReadCompleted = true;
            await ReadSendStatusesAsync(cancellation.Token);
            await ReadReplyPreviewsAsync(cancellation.Token);
            // Keep the address-book queries sequential with the current conversation's load.
            await ReadConversationListAsync(cancellation.Token);
        }
        catch (Exception) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!verifiedAccount || !messagingService.IsEnabled || localAccountId != messagingService.AccountId
                || walletProvider.GetWallet()?.GetDefaultAccount()?.Address != messagingService.Address)
                ClearHistory();
            if (earlier && !historyReadCompleted && HasLoaded) EarlierMessagesError = ex.Message;
            else ErrorText ??= ex.Message;
        }
        finally
        {
            if (ReferenceEquals(operation, cancellation))
            {
                operation = null;
                NotifyState();
                EnsureInitialMessagePresentation();
                MarkDisplayedMessagesRead();
                ReloadIfIdle();
            }
        }
    }

    async Task ReadNewHistoryAsync(CancellationToken cancellationToken)
    {
        var lastSaved = Messages.LastOrDefault(message => !message.IsPending);
        if (lastSaved is null)
        {
            await ReadHistoryAsync(null, cancellationToken);
            return;
        }
        long after = lastSaved.LocalSequence;
        var additions = new List<ChatMessageViewModel>();
        bool more;
        do
        {
            var snapshot = await messagingService.GetChatHistoryAsync(localAccountId!, Address,
                cancellationToken: cancellationToken, after: after);
            cancellationToken.ThrowIfCancellationRequested();
            UpdateReceiveNotice(snapshot.SyncStatus);
            additions.AddRange(snapshot.Messages.Select(message => new ChatMessageViewModel(message, localAccountId!)));
            if (snapshot.Messages.Length > 0) after = snapshot.Messages[^1].LocalSequence;
            more = snapshot.HasMore && snapshot.Messages.Length > 0;
        } while (more);

        if (additions.Count > 0)
        {
            bool scrollToEnd = followLatest;
            RecordNewMessages(additions);
            SetMessages([.. Messages, .. additions]);
            RefreshPendingMessages();
            // Appending preserves the viewport; do not realign a partially visible reading anchor.
            if (scrollToEnd) DispatchMessageScroll(Messages[^1], scrollToEnd: true);
        }
        NotifyState();
    }

    async Task ReadHistoryAsync(long? before, CancellationToken cancellationToken)
    {
        // Entries committed before a latest-history read are already in the SDK, even outside its page limit.
        var submitted = before.HasValue ? [] : messagingService.GetPendingChatMessages(localAccountId!, Address)
            .Where(message => message.SendStatus is not null).Select(message => message.SendStatus!.MessageId).ToHashSet(StringComparer.Ordinal);
        var snapshot = await messagingService.GetChatHistoryAsync(localAccountId!, Address, before, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        messagingService.RemoveSavedPendingMessages(localAccountId!, Address, submitted);
        bool scrollToEnd = !before.HasValue && (!HasLoaded || followLatest);
        // The reader may have moved while the page was loading; preserve the currently visible message.
        var anchor = Messages.ElementAtOrDefault(firstVisibleIndex);
        var batch = snapshot.Messages.Select(message => new ChatMessageViewModel(message, localAccountId!)).ToArray();
        if (HasLoaded && !before.HasValue) RecordNewMessages(batch);
        SetMessages(before.HasValue ? [.. batch, .. Messages] : batch);
        RefreshPendingMessages();
        HasMore = snapshot.HasMore;
        HasLoaded = true;
        UpdateReceiveNotice(snapshot.SyncStatus);
        NotifyState();
        var target = scrollToEnd ? Messages.LastOrDefault() : anchor;
        if (IsMessageListPresented && target is not null)
            DispatchMessageScroll(target, scrollToEnd);
    }

    void OnSynchronizationChanged(object? sender, ResourceSyncStatus status)
    {
        if (isVisible && IsChatEnabled && localAccountId == messagingService.AccountId)
            UpdateReceiveNotice(status);
    }

    void UpdateReceiveNotice(ResourceSyncStatus? status)
    {
        // The SDK owns receive recovery. Keep its notice separate from local failures with a Retry action.
        string? notice = status switch
        {
            { State: ResourceSyncState.Blocked, BlockReason: ResourceSyncBlockReason.Connection } => Strings.ChatConnectionUnavailable,
            { State: ResourceSyncState.Blocked, BlockReason: ResourceSyncBlockReason.Authentication } => Strings.ChatDeviceAuthenticationFailed,
            { State: ResourceSyncState.Blocked, BlockReason: ResourceSyncBlockReason.Storage } => Strings.ChatHistoryStorageFailed,
            { State: ResourceSyncState.Blocked, BlockReason: ResourceSyncBlockReason.Verification } => Strings.ChatMessageVerificationFailed,
            { State: ResourceSyncState.Blocked } => Strings.ChatHistorySyncIncomplete,
            _ => null
        };
        if (receiveError == notice) return;
        receiveError = notice;
        OnPropertyChanged(nameof(ChatConnectionNotice));
        OnPropertyChanged(nameof(ErrorNotice));
    }

    async Task<bool> ValidatePushNavigationAsync(CancellationToken cancellationToken, string? loadedAccount = null)
    {
        if (pushNavigationIdentity is not { } expected) return true;
        var actual = await messagingService.GetPushNavigationIdentityAsync();
        cancellationToken.ThrowIfCancellationRequested();
        if (actual == expected && (loadedAccount is null || loadedAccount == expected.Account)) return true;
        var shell = Shell.Current;
        await shell.GoToAsync("//home");
        if (actual is { } current && (current.Account != expected.Account || current.Network != expected.Network))
            await shell.DisplayAlertAsync(Strings.ChatEnableNotifications, Strings.ChatPushOtherAccount, Strings.OK);
        return false;
    }

    void ClearHistory(bool keepConversationList = false)
    {
        contactDetailsHistory = null;
        CloseForwardRecipientPicker(resumeChat: false);
        CancelQuoteNavigation();
        ClearQuoteHighlight();
        ClearQuoteReturnPosition();
        ClearMessageReader();
        ClearMessageSearch();
        DetachDraft();
        ClearReadState();
        ClearOtherUnread();
        if (!keepConversationList) ClearConversationList();
        ResetMessagePresentation();
        ClearNewMessageHint();
        IsChatEnabled = false;
        HasChatContact = false;
        HasLoaded = false;
        HasMore = false;
        EarlierMessagesError = null;
        UpdateReceiveNotice(null);
        ChatContact = null;
        hasLoadedContact = false;
        DisplayName = Strings.ChatConversation;
        replyMessages.Clear();
        SetMessages([]);
        followLatest = true;
        firstVisibleIndex = 0;
    }

    void NotifyState()
    {
        if (ShowForwardRecipientPicker && forwardRecipientContext?.Invoke() != true)
            CloseForwardRecipientPicker(resumeChat: false);
        OnPropertyChanged(nameof(ChatConnectionNotice));
        OnPropertyChanged(nameof(ShowOtherUnread));
        OnPropertyChanged(nameof(DraftStorageNotice));
        OnPropertyChanged(nameof(CanRetryDraftStorage));
        NotifyScrollState();
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(IsLoading));
        NotifyQuoteNavigation();
        OnPropertyChanged(nameof(ShowConversationLoading));
        OnPropertyChanged(nameof(IsChatEnabled));
        OnPropertyChanged(nameof(CanLoadEarlier));
        OnPropertyChanged(nameof(HasLoaded));
        OnPropertyChanged(nameof(HasMore));
        OnPropertyChanged(nameof(EarlierMessagesButtonText));
        OnPropertyChanged(nameof(ShowContactRequired));
        OnPropertyChanged(nameof(ChatContact));
        OnPropertyChanged(nameof(CanOpenContactDetails));
        OnPropertyChanged(nameof(ShowRetry));
        OnPropertyChanged(nameof(CanCompose));
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(CanOpenMessageSearch));
    }
}
