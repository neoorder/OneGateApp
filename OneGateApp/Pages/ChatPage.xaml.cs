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
    bool earlierRequested;
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
    long? FirstSavedSequence => Messages.Where(message => !message.IsPending).Min(message => (long?)message.LocalSequence);
    long? LastSavedSequence => Messages.Where(message => !message.IsPending).Max(message => (long?)message.LocalSequence);

    bool SetMessages(IReadOnlyList<ChatMessageViewModel> value, bool refreshExisting = true, bool appendNewRows = false)
    {
        var additions = value.ToHashSet();
        int previousCount = Messages.Count;
        for (int i = previousCount - 1; i >= 0; i--)
            if (!additions.Remove(Messages[i])) Messages.RemoveAt(i);

        bool changed = previousCount != Messages.Count || additions.Count > 0;
        if (!changed && !refreshExisting)
        {
            return false;
        }

        // Live updates append new rows after the retained bubbles in their visible order.
        if (appendNewRows) value = [.. Messages, .. value.Where(additions.Contains)];
        DateOnly? previousDate = null;
        for (int i = 0; i < value.Count; i++)
        {
            bool insert = additions.Contains(value[i]);
            var message = insert ? value[i] : Messages[i];
            DateOnly date = message.LocalDate;
            message.ShowDateSeparator = previousDate != date;
            previousDate = date;
            if (insert) Messages.Insert(i, message);
        }
        UpdateReplyPreviews();
        NotifyScrollState();
        return changed;
    }
    public bool IsLoading => operation is not null;
    public bool ShowLoading => IsLoading && (!HasLoaded || requestedQuote is not null
        || quoteNavigation is { IsCancellationRequested: false });
    public bool IsChatEnabled { get; private set; }
    public bool CanLoadEarlier => IsChatEnabled && HasLoaded && !earlierRequested
        && (!IsLoading || (!isLoadingEarlier && quoteNavigation is null));
    public bool HasLoaded { get; private set; }
    public bool HasMore { get; private set; }
    public string EarlierMessagesButtonText => earlierRequested || (IsLoading && isLoadingEarlier) ? Strings.Loading
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
            OnPropertyChanged(nameof(CanRetryInitialHistory));
        }
    }
    public string? ErrorNotice => ErrorText ?? receiveError;
    public bool CanRetryInitialHistory => !HasLoaded && !IsLoading && ErrorText is not null;
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

    async void OnRetryInitialHistoryClicked(object sender, EventArgs e)
    {
        if (CanRetryInitialHistory) await LoadAsync();
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
        if (earlierRequested)
        {
            earlierRequested = false;
            if (CanLoadEarlier && HasMore)
            {
                await LoadAsync(earlier: true);
                return;
            }
        }
        if (reloadPending == 0) return;
        var changes = reloadPending;
        reloadPending = 0;
        await LoadAsync(changes: changes);
    }

    async void OnLoadEarlierClicked(object sender, EventArgs e) => await LoadEarlierAsync();

    Task LoadEarlierAsync()
    {
        if (!CanLoadEarlier || !HasMore) return Task.CompletedTask;
        if (operation is not { } refresh) return LoadAsync(earlier: true);

        // An incremental refresh may be waiting for a send ID. Let the older page read first.
        earlierRequested = true;
        NotifyState();
        refresh.Cancel();
        return Task.CompletedTask;
    }

    async Task LoadAsync(bool earlier = false, MessagingChangeKind changes = MessagingChangeKind.Records)
    {
        if (IsLoading || !isVisible || !hasNavigatedTo) return;
        using var cancellation = new CancellationTokenSource();
        operation = cancellation;
        isLoadingEarlier = earlier;
        bool refreshRecords = earlier || !HasLoaded || (changes & MessagingChangeKind.Records) != 0;
        if (earlier) EarlierMessagesError = null;
        BusyText = Strings.Loading;
        NotifyState();
        long? before = earlier ? FirstSavedSequence : null;
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
            if (!earlier) ErrorText = null;
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
                if (earlierRequested && cancellation.IsCancellationRequested) reloadPending |= changes;
                operation = null;
                NotifyState();
                EnsureInitialMessagePresentation();
                MarkDisplayedMessagesRead();
                ReloadIfIdle();
            }
        }
    }

    async Task<bool> ReadNewHistoryAsync(CancellationToken cancellationToken)
    {
        var wallet = walletProvider.GetWallet();
        string accountId = localAccountId!;
        string address = Address;
        long after = LastSavedSequence ?? 0;
        var additions = new List<ChatMessageViewModel>();
        string? readError = null;
        try
        {
            bool more;
            do
            {
                var snapshot = await messagingService.GetChatHistoryAsync(accountId, address,
                    cancellationToken: cancellationToken, after: after);
                cancellationToken.ThrowIfCancellationRequested();
                UpdateReceiveNotice(snapshot.SyncStatus);
                additions.AddRange(snapshot.Messages.Select(message => new ChatMessageViewModel(message, accountId)));
                if (snapshot.Messages.Length > 0) after = snapshot.Messages[^1].LocalSequence;
                more = snapshot.HasMore && snapshot.Messages.Length > 0;
            } while (more);
        }
        catch (Exception ex) when (ex is not OperationCanceledException && additions.Count > 0
            && !cancellationToken.IsCancellationRequested && IsCurrentMessageContext(wallet, accountId, address))
        {
            // Keep the contiguous batches already read. The next refresh resumes after the last appended sequence.
            readError = ex.Message;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (additions.Count > 0)
        {
            bool scrollToEnd = followLatest || explicitBottomScrollRevision == scrollRevision;
            RecordNewMessages(additions);
            SetMessagesWithPending([.. Messages.Where(message => !message.IsPending), .. additions], appendNewRows: true);
            // Appending preserves the viewport; do not realign a partially visible reading anchor.
            if (scrollToEnd && explicitScrollRevision != scrollRevision) DispatchMessageScroll(Messages[^1], scrollToEnd: true);
        }
        if (readError is not null) ErrorText ??= readError;
        NotifyState();
        return readError is null;
    }

    async Task ReadHistoryAsync(long? before, CancellationToken cancellationToken)
    {
        var snapshot = await messagingService.GetChatHistoryAsync(localAccountId!, Address, before, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        // An explicit scroll can precede the native viewport confirmation.
        bool scrollToEnd = followLatest || explicitBottomScrollRevision == scrollRevision || (!before.HasValue && !HasLoaded);
        // The reader may have moved while the page was loading; preserve the currently visible message.
        var anchor = Messages.ElementAtOrDefault(firstVisibleIndex);
        var batch = snapshot.Messages.Select(message => new ChatMessageViewModel(message, localAccountId!)).ToArray();
        SetMessagesWithPending(before.HasValue ? [.. batch, .. Messages.Where(message => !message.IsPending)] : batch,
            snapshot.SubmittedMessageIds);
        HasMore = snapshot.HasMore;
        HasLoaded = true;
        UpdateReceiveNotice(snapshot.SyncStatus);
        NotifyState();
        if (before.HasValue && explicitScrollRevision == scrollRevision) return;
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
        // The SDK reports receive progress separately from local history reads.
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
        earlierRequested = false;
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
        OnPropertyChanged(nameof(CanRetryInitialHistory));
        OnPropertyChanged(nameof(CanCompose));
        OnPropertyChanged(nameof(CanSend));
        OnPropertyChanged(nameof(CanOpenMessageSearch));
    }
}
