using Neo.Wallets;
using NeoOrder.OneGate.Services.Messaging;
using Contact = NeoOrder.OneGate.Data.Contact;

namespace NeoOrder.OneGate.Pages;

public partial class GlobalSearchPage
{
    readonly MessagingService messagingService;
    readonly IWalletProvider walletProvider;
    Contact[] savedSearchContacts = [];
    Dictionary<string, (string Status, bool CanChat, bool HasIncomingRequest)> chatSearchContacts = [];
    Wallet? chatSearchWallet;
    WalletAccount? chatSearchAccount;
    string? chatSearchAccountId;
    CancellationTokenSource? chatContactSearchCancellation;
    bool searchPageVisible;
    long chatSearchNamesRevision = -1;
    long chatSearchContactsRevision = -1;

    public string? ChatContactSearchError
    {
        get;
        private set
        {
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanRetryChatContactSearch));
        }
    }
    public bool CanRetryChatContactSearch => searchPageVisible && HasCurrentChatSearchAccount()
        && chatContactSearchCancellation is null && ChatContactSearchError is not null;

    void OnRetryChatContactSearchClicked(object sender, EventArgs e)
    {
        if (CanRetryChatContactSearch && ReferenceEquals(Shell.Current.CurrentPage, this)) RefreshChatContactSearch();
    }

    bool HasCurrentChatSearchAccount() => messagingService.IsEnabled && chatSearchAccountId is not null
        && chatSearchAccountId == messagingService.AccountId
        && ReferenceEquals(chatSearchWallet, walletProvider.GetWallet())
        && chatSearchAccount is not null && ReferenceEquals(chatSearchAccount, chatSearchWallet?.GetDefaultAccount())
        && chatSearchAccount.Address == messagingService.Address;

    void StartChatContactSearch()
    {
        searchPageVisible = true;
        messagingService.Changed += OnSearchMessagingChanged;
        walletProvider.WalletChanged += OnSearchWalletChanged;
        RefreshChatContactSearch();
    }

    protected override void OnDisappearing()
    {
        searchPageVisible = false;
        messagingService.Changed -= OnSearchMessagingChanged;
        walletProvider.WalletChanged -= OnSearchWalletChanged;
        chatContactSearchCancellation?.Cancel();
        chatContactSearchCancellation = null;
        OnPropertyChanged(nameof(CanRetryChatContactSearch));
        base.OnDisappearing();
    }

    void OnSearchWalletChanged(object? sender, Wallet? wallet) => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (searchPageVisible) RefreshChatContactSearch();
    });

    void OnSearchMessagingChanged(object? sender, EventArgs e)
    {
        if (!searchPageVisible) return;
        // Relationship and request changes reread local contacts; nickname changes reuse the snapshot.
        if (!HasCurrentChatSearchAccount()) RefreshChatContactSearch();
        else if (chatContactSearchCancellation is null && chatSearchContactsRevision != messagingService.ChatContactsRevision)
            RefreshChatContactSearch();
        else if (chatSearchNamesRevision != messagingService.ChatNamesRevision) RebuildContactSearchIndex();
    }

    void RefreshChatContactSearch()
    {
        chatContactSearchCancellation?.Cancel();
        chatContactSearchCancellation = null;
        if (!HasCurrentChatSearchAccount())
        {
            chatSearchContacts = [];
            ChatContactSearchError = null;
        }
        chatSearchWallet = walletProvider.GetWallet();
        chatSearchAccount = chatSearchWallet?.GetDefaultAccount();
        chatSearchAccountId = messagingService.AccountId;
        if (!HasCurrentChatSearchAccount()) chatSearchAccountId = null;
        RebuildContactSearchIndex();
        if (chatSearchAccountId is null) return;
        var cancellation = new CancellationTokenSource();
        chatContactSearchCancellation = cancellation;
        OnPropertyChanged(nameof(CanRetryChatContactSearch));
        _ = ReadChatSearchContactsAsync(cancellation);
    }

    async Task ReadChatSearchContactsAsync(CancellationTokenSource cancellation)
    {
        bool IsCurrent() => searchPageVisible && !cancellation.IsCancellationRequested
            && ReferenceEquals(chatContactSearchCancellation, cancellation) && HasCurrentChatSearchAccount();
        // Capture before reading, so a change during the read triggers one follow-up snapshot.
        chatSearchContactsRevision = messagingService.ChatContactsRevision;
        try
        {
            // This local read is independent of asset/catalog loading and never resolves remote profiles.
            var snapshot = await messagingService.GetContactStatusesAsync(cancellation.Token);
            if (!IsCurrent()) return;
            chatSearchContacts = snapshot;
            ChatContactSearchError = null;
            RebuildContactSearchIndex();
        }
        catch (Exception) when (!IsCurrent()) { }
        catch (Exception ex) { ChatContactSearchError = ex.Message; }
        finally
        {
            if (ReferenceEquals(chatContactSearchCancellation, cancellation))
            {
                chatContactSearchCancellation = null;
                OnPropertyChanged(nameof(CanRetryChatContactSearch));
                if (searchPageVisible && HasCurrentChatSearchAccount()
                    && chatSearchContactsRevision != messagingService.ChatContactsRevision)
                    RefreshChatContactSearch();
            }
            cancellation.Dispose();
        }
    }

    void RebuildContactSearchIndex()
    {
        // Capture before resolving names, so a profile completing during this pass remains pending.
        chatSearchNamesRevision = messagingService.ChatNamesRevision;
        var statuses = HasCurrentChatSearchAccount()
            ? new Dictionary<string, (string Status, bool CanChat, bool HasIncomingRequest)>(chatSearchContacts, StringComparer.Ordinal)
            : [];
        var entries = new List<GlobalSearchIndex<GlobalSearchResult>>();
        foreach (var contact in savedSearchContacts)
        {
            bool hasChatState = statuses.Remove(contact.Address);
            string? displayName = hasChatState
                ? messagingService.GetChatDisplayName(chatSearchAccountId!, contact.Address, contact.Label) : null;
            var result = GlobalSearchResult.FromContact(contact, displayName);
            entries.Add(new(result, result.Title, contact.Address, contact.Note));
        }
        foreach (var (address, status) in statuses)
        {
            // Match the home contacts list: received stranger requests stay in Friend Requests.
            if (!status.CanChat && status.HasIncomingRequest) continue;
            string? displayName = messagingService.GetChatDisplayName(chatSearchAccountId!, address);
            var result = GlobalSearchResult.FromChatContact(chatSearchAccountId!, address, displayName);
            entries.Add(new(result, result.Title, address));
        }
        contactIndex = entries.OrderBy(p => p.Item.Title ?? p.Item.Subtitle, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(p => p.Item.Subtitle, StringComparer.Ordinal).ToArray();
        UpdateResults();
    }

    bool CanOpenChatSearchResult(GlobalSearchResult result) => searchPageVisible && HasCurrentChatSearchAccount()
        && result.ChatAccountId == chatSearchAccountId
        && result.ChatAddress is not null && chatSearchContacts.TryGetValue(result.ChatAddress, out var status)
        && (status.CanChat || !status.HasIncomingRequest);
}
