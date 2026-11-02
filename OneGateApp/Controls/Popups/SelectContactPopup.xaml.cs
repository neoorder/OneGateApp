using Neo.Wallets;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Services;
using NeoOrder.OneGate.Services.Messaging;
using Contact = NeoOrder.OneGate.Data.Contact;

namespace NeoOrder.OneGate.Controls.Popups;

public partial class SelectContactPopup : MyPopup<Contact?>
{
    readonly AddressBookService addressBookService;
    readonly MessagingService messagingService;
    readonly IWalletProvider walletProvider;
    readonly Wallet? wallet;
    readonly WalletAccount? account;
    Contact[] savedContacts = [];
    HashSet<string> chatAddresses = new(StringComparer.Ordinal);
    ContactViewModel[] allContacts = [];
    string? chatAccountId;
    string searchText = "";
    CancellationTokenSource? loadCancellation;
    bool closed;

    public ContactViewModel[] Contacts { get; private set { field = value; OnPropertyChanged(); } } = [];

    public SelectContactPopup(AddressBookService addressBookService, MessagingService messagingService, IWalletProvider walletProvider)
    {
        this.addressBookService = addressBookService;
        this.messagingService = messagingService;
        this.walletProvider = walletProvider;
        wallet = walletProvider.GetWallet();
        account = wallet?.GetDefaultAccount();
        InitializeComponent();
        Opened += (_, _) =>
        {
            messagingService.ChatProfilesChanged += OnProfilesChanged;
            walletProvider.WalletChanged += OnWalletChanged;
            LoadData();
        };
        Closed += (_, _) =>
        {
            closed = true;
            loadCancellation?.Cancel();
            messagingService.ChatProfilesChanged -= OnProfilesChanged;
            walletProvider.WalletChanged -= OnWalletChanged;
        };
    }

    bool HasCurrentWallet() => !closed && ReferenceEquals(wallet, walletProvider.GetWallet())
        && ReferenceEquals(account, wallet?.GetDefaultAccount());

    void OnWalletChanged(object? sender, Wallet? current) => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (!closed && !HasCurrentWallet()) OnCancel(this, EventArgs.Empty);
    });

    void OnProfilesChanged(object? sender, EventArgs e) => UpdateContacts();

    void OnSearch(object sender, TextChangedEventArgs e)
    {
        searchText = e.NewTextValue?.Trim() ?? "";
        FilterContacts();
    }

    async void OnSelectContact(object sender, TappedEventArgs e)
    {
        if (closed) return;
        var selected = e.Parameter as ContactViewModel;
        var contact = Contacts.FirstOrDefault(contact => contact.Address == selected?.Address);
        bool canSelect = HasCurrentWallet() && contact is not null
            && (contact.IsSaved || messagingService.IsEnabled && chatAccountId == messagingService.AccountId);
        closed = true;
        await CloseAsync(canSelect ? new Contact { Address = contact!.Address, Label = contact.Label, Note = contact.Note } : null);
    }

    async void OnCancel(object sender, EventArgs e)
    {
        if (closed) return;
        closed = true;
        await CloseAsync(null);
    }

    async void LoadData()
    {
        using var cancellation = new CancellationTokenSource();
        loadCancellation = cancellation;
        try
        {
            try
            {
                savedContacts = await addressBookService.GetEntriesAsync(cancellation.Token);
                UpdateContacts();
            }
            catch (Exception ex) when (!cancellation.IsCancellationRequested) { System.Diagnostics.Debug.WriteLine(ex); }
            if (!HasCurrentWallet() || cancellation.IsCancellationRequested) return;
            // This reads local chat relationships and does not enable chat or require a network request.
            var snapshot = await messagingService.GetChatRecipientsAsync(cancellation.Token);
            if (!HasCurrentWallet() || cancellation.IsCancellationRequested
                || snapshot.LocalAccountId != messagingService.AccountId || !messagingService.IsEnabled) return;
            chatAccountId = snapshot.LocalAccountId;
            chatAddresses = new(snapshot.Contacts, StringComparer.Ordinal);
            UpdateContacts();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        finally { loadCancellation = null; }
    }

    void UpdateContacts()
    {
        if (!HasCurrentWallet()) return;
        bool hasChat = messagingService.IsEnabled && chatAccountId is not null && chatAccountId == messagingService.AccountId;
        var merged = savedContacts.ToDictionary(contact => contact.Address, contact => new ContactViewModel(contact, "",
            displayName: hasChat && chatAddresses.Contains(contact.Address)
                ? messagingService.GetChatDisplayName(chatAccountId!, contact.Address, contact.Label) : null), StringComparer.Ordinal);
        if (hasChat)
            foreach (string address in chatAddresses)
                if (!merged.ContainsKey(address))
                    merged.Add(address, ContactViewModel.FromChatContact(address,
                        messagingService.GetChatDisplayName(chatAccountId!, address)));
        allContacts = merged.Values.OrderBy(contact => contact.DisplayName ?? contact.Address, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(contact => contact.Address, StringComparer.Ordinal).ToArray();
        FilterContacts();
    }

    void FilterContacts()
    {
        if (closed) return;
        string filter = searchText;
        Contacts = filter.Length == 0 ? allContacts : allContacts.Where(contact =>
                contact.DisplayName?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true
                || contact.Address.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || contact.Note?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true
                || chatAccountId is not null && chatAddresses.Contains(contact.Address)
                    && messagingService.GetChatDisplayProfile(chatAccountId, contact.Address)?.Nickname?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true)
            .OrderByDescending(contact => string.Equals(contact.DisplayName, filter, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(contact => string.Equals(contact.Address, filter, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(contact => contact.Address.StartsWith(filter, StringComparison.OrdinalIgnoreCase))
            .Take(30).ToArray();
    }
}
