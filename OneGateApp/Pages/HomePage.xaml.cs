using CommunityToolkit.Maui.Alerts;
using Microsoft.EntityFrameworkCore;
using Neo.Wallets;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Models.AppLinks;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using NeoOrder.OneGate.Services.Messaging;
using System.Net;
using Contact = NeoOrder.OneGate.Data.Contact;

namespace NeoOrder.OneGate.Pages;

public partial class HomePage : ContentPage
{
    readonly ApplicationDbContext dbContext;
    readonly AddressBookService addressBookService;
    readonly IDispatcherTimer timer;
    ContactViewModel[] allContacts = [];
    Contact[]? contactEntries;
    bool refreshContactEntries;
    Dictionary<string, (string Status, bool CanChat, bool HasIncomingRequest)>? contactStatuses;
    Wallet? contactsWallet;
    WalletAccount? contactsAccount;
    string? contactsAccountId;
    bool isPageVisible;
    bool contactsReloadPending;
    CancellationTokenSource? contactsLoadCancellation;
    bool openingContact;

    public LoadingService LoadingService { get; }
    public LoadingService ContactsLoadingService { get; }
    public CachedCollection<Banner> Banners { get; }
    public CachedCollection<News> News { get; }
    public ContactViewModel[] Contacts { get; private set { field = value; OnPropertyChanged(); } } = [];
    public bool HasLoadedContacts
    {
        get;
        private set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowContactsLoading)); }
    }
    public bool ShowContactsLoading => ContactsLoadingService.IsLoading && !HasLoadedContacts;
    public string? ContactsError { get; private set { field = value; OnPropertyChanged(); } }
    public bool IsContactChatEnabled => contactStatuses is not null && messagingService.IsEnabled
        && contactsAccountId == messagingService.AccountId
        && ReferenceEquals(contactsWallet, walletProvider.GetWallet())
        && ReferenceEquals(contactsAccount, contactsWallet?.GetDefaultAccount());
    public bool IsContactSearchVisible { get; private set { field = value; OnPropertyChanged(); } }
    public bool HasContactSearch => !string.IsNullOrWhiteSpace(ContactSearchText);
    public string ContactsEmptyTitle => HasContactSearch ? Strings.HomeNoMatchingContacts : Strings.NoAddress;
    public string ContactsEmptyDescription => HasContactSearch ? Strings.HomeNoMatchingContactsHint : Strings.NoAddressText;

    public string ContactSearchText
    {
        get;
        set
        {
            field = value ?? "";
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasContactSearch));
            OnPropertyChanged(nameof(ContactsEmptyTitle));
            OnPropertyChanged(nameof(ContactsEmptyDescription));
            FilterContacts();
        }
    } = "";

    public int SelectedHomeSection
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            conversationFilterRevision++;
            OnPropertyChanged();
            UpdateSectionActivity();
        }
    }

    public HomePage(IServiceProvider serviceProvider, ApplicationDbContext dbContext, AddressBookService addressBookService, MessagingService messagingService, IWalletProvider walletProvider)
    {
        this.LoadingService = new(LoadBannersAsync, LoadNewsAsync);
        this.ContactsLoadingService = new(LoadContactsAsync);
        this.ContactsLoadingService.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LoadingService.IsLoading)) OnPropertyChanged(nameof(ShowContactsLoading));
        };
        this.dbContext = dbContext;
        this.addressBookService = addressBookService;
        this.messagingService = messagingService;
        this.walletProvider = walletProvider;
        this.Banners = serviceProvider.GetServiceOrCreateInstance<CachedCollection<Banner>>();
        this.News = serviceProvider.GetServiceOrCreateInstance<CachedCollection<News>>();
        this.timer = Dispatcher.CreateTimer();
        this.timer.Interval = TimeSpan.FromSeconds(5);
        this.timer.Tick += Timer_Tick;
        OpenConversationCommand = new(async conversation => await OpenConversationAsync(conversation));
        ConversationActionsCommand = new(async conversation => await ShowConversationActionsAsync(conversation));
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        isPageVisible = true;
        refreshContactEntries = true;
        messagingService.Changed += OnMessagingChanged;
        messagingService.ConnectionChanged += OnChatConnectionChanged;
        walletProvider.WalletChanged += OnHomeWalletChanged;
        conversationLabels = null;
        if (!ReferenceEquals(conversationsWallet, walletProvider.GetWallet()))
        {
            ClearConversations();
            HasChatState = false;
            HasPendingChatActivation = false;
            ChatAddress = null;
            ChatError = null;
            IsChatEnabled = false;
            NotifyChatState();
        }
        CheckForUpdates();
        UpdateSectionActivity();
        RefreshPendingRequestCount();
    }

    protected override void OnDisappearing()
    {
        isPageVisible = false;
        conversationFilterRevision++;
        messagingService.Changed -= OnMessagingChanged;
        messagingService.ConnectionChanged -= OnChatConnectionChanged;
        walletProvider.WalletChanged -= OnHomeWalletChanged;
        chatLoadCancellation?.Cancel();
        contactsLoadCancellation?.Cancel();
        markConversationReadCancellation?.Cancel();
        chatReloadPending = false;
        contactsReloadPending = false;
        requestCountCancellation?.Cancel();
        requestCountReloadPending = false;
        timer.Stop();
        base.OnDisappearing();
    }

    void OnMessagingChanged(object? sender, EventArgs e)
    {
        if (!isPageVisible) return;
        if (contactStatuses is not null && !IsContactChatEnabled && contactEntries is not null) UpdateContacts(contactEntries);
        OnPropertyChanged(nameof(IsContactChatEnabled));
        RefreshPendingRequestCount();
        if (SelectedHomeSection == 0) LoadChatState();
        else if (SelectedHomeSection == 1)
        {
            contactsReloadPending = true;
            ContactsLoadingService.BeginLoad();
        }
    }

    void UpdateSectionActivity()
    {
        timer.Stop();
        if (contactStatuses is not null && !IsContactChatEnabled && contactEntries is not null) UpdateContacts(contactEntries);
        OnPropertyChanged(nameof(IsContactChatEnabled));
        if (SelectedHomeSection != 0)
        {
            chatLoadCancellation?.Cancel();
            chatReloadPending = false;
        }
        if (SelectedHomeSection != 1)
        {
            refreshContactEntries = true;
            contactsLoadCancellation?.Cancel();
            contactsReloadPending = false;
        }
        if (!isPageVisible) return;

        if (SelectedHomeSection == 0)
        {
            LoadChatState();
        }
        else if (SelectedHomeSection == 1)
        {
            contactsReloadPending = true;
            ContactsLoadingService.BeginLoad();
        }
        else if (SelectedHomeSection == 2)
        {
            LoadingService.BeginLoad();
            timer.Start();
        }
    }

    async void OnSearchClicked(object sender, EventArgs e)
    {
        if (SelectedHomeSection == 0)
        {
            if (!IsChatEnabled) return;
            IsConversationSearchVisible = !IsConversationSearchVisible;
            if (IsConversationSearchVisible)
                Dispatcher.Dispatch(() => conversationSearch.Focus());
            else
            {
                conversationSearch.Unfocus();
                OnClearConversationFilterClicked(sender, e);
            }
            return;
        }
        if (SelectedHomeSection != 1)
        {
            await Commands.GotoPage.ExecuteAsync("//home/search");
            return;
        }

        IsContactSearchVisible = !IsContactSearchVisible;
        if (IsContactSearchVisible)
            Dispatcher.Dispatch(() => contactSearch.Focus());
        else
        {
            contactSearch.Unfocus();
            ContactSearchText = "";
        }
    }

    async void OnContactTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is ContactViewModel contact) await OpenContactAsync(contact);
    }

    async Task OpenContactAsync(ContactViewModel contact)
    {
        if (openingContact || !isPageVisible || SelectedHomeSection != 1
            || !ReferenceEquals(Shell.Current.CurrentPage, this) || !Contacts.Contains(contact)) return;
        contactSearch.Unfocus();

        openingContact = true;
        try
        {
            await Shell.Current.GoToAsync("contact-details", new ShellNavigationQueryParameters
            {
                ["contact"] = contact
            });
        }
        catch (Exception ex)
        {
            if (isPageVisible && SelectedHomeSection == 1 && ReferenceEquals(Shell.Current.CurrentPage, this)
                && Contacts.Any(current => current.Address == contact.Address))
                await Toast.Show(ex.Message);
        }
        finally { openingContact = false; }
    }

    void OnContactsRetryClicked(object sender, EventArgs e)
    {
        if (!isPageVisible || SelectedHomeSection != 1 || ContactsLoadingService.IsLoading) return;
        refreshContactEntries = true;
        ContactsLoadingService.BeginLoad();
    }

    async Task LoadContactsAsync()
    {
        if (ContactsLoadingService.IsReloading) { refreshContactEntries = true; messagingService.RefreshVisibleChatProfiles(); }
        do
        {
            contactsReloadPending = false;
            using var cancellation = new CancellationTokenSource();
            contactsLoadCancellation = cancellation;
            ContactsError = null;
            try { await ReadContactsAsync(cancellation.Token); }
            catch (Exception) when (cancellation.IsCancellationRequested) { }
            catch (Exception ex)
            {
                ContactsError = ex.Message;
                return;
            }
            finally { contactsLoadCancellation = null; }
        } while (isPageVisible && SelectedHomeSection == 1 && contactsReloadPending);
    }

    async Task ReadContactsAsync(CancellationToken cancellationToken)
    {
        // A failed explicit refresh keeps the last successful data available to background updates.
        Contact[]? contacts = refreshContactEntries ? null : contactEntries;
        refreshContactEntries = false;
        contacts ??= await addressBookService.GetEntriesAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        contactEntries = contacts;
        // Address-book data is usable before chat initialization or relationship reads finish.
        UpdateContacts(contacts);
        try
        {
            await ReadChatStateAsync(cancellationToken);
            if (IsChatEnabled)
            {
                Wallet? wallet = walletProvider.GetWallet();
                WalletAccount? account = wallet?.GetDefaultAccount();
                string? accountId = messagingService.AccountId;
                var statuses = await messagingService.GetContactStatusesAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (!ReferenceEquals(wallet, walletProvider.GetWallet())
                    || !ReferenceEquals(account, wallet?.GetDefaultAccount())
                    || accountId != messagingService.AccountId || !messagingService.IsEnabled)
                {
                    contactsReloadPending = true;
                    return;
                }
                contactStatuses = statuses;
                contactsWallet = wallet;
                contactsAccount = account;
                contactsAccountId = accountId;
            }
            else contactStatuses = null;
            UpdateContacts(contacts);
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                NotifyChatState();
                OnPropertyChanged(nameof(IsContactChatEnabled));
            }
        }
    }

    void UpdateContacts(Contact[] contacts)
    {
        if (!IsContactChatEnabled) contactStatuses = null;
        var statuses = contactStatuses;
        var previous = allContacts.ToDictionary(p => p.Address, StringComparer.Ordinal);
        var merged = contacts.ToDictionary(contact => contact.Address, contact =>
        {
            var (status, canChat, hasIncomingRequest) = statuses?.GetValueOrDefault(contact.Address, (Strings.HomeAddressOnly, false, false))
                ?? (Strings.ChatStatusUnavailable, false, false);
            bool hasChatState = statuses?.ContainsKey(contact.Address) == true;
            return new ContactViewModel(contact, status, canChat, hasIncomingRequest,
                displayName: hasChatState ? messagingService.GetChatDisplayName(contactsAccountId!, contact.Address, contact.Label) : null,
                hasOutgoingRequest: !canChat && !hasIncomingRequest && hasChatState);
        }, StringComparer.Ordinal);
        // Saved names and notes take priority; unsaved friends and outgoing requests stay display-only.
        if (statuses is not null)
            foreach (var (address, status) in statuses)
            {
                if (merged.ContainsKey(address)) continue;
                if (status.CanChat)
                    merged.Add(address, ContactViewModel.FromChatContact(address,
                        messagingService.GetChatDisplayName(contactsAccountId!, address)));
                else if (!status.HasIncomingRequest)
                    merged.Add(address, ContactViewModel.FromChatRequest(address, status.Status,
                        messagingService.GetChatDisplayName(contactsAccountId!, address)));
            }
        allContacts = merged.Values
            .OrderBy(contact => contact.DisplayName ?? contact.Address, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(contact => contact.Address, StringComparer.Ordinal)
            .Select(current =>
            {
                if (previous.TryGetValue(current.Address, out var existing)
                    && existing.IsSaved == current.IsSaved && existing.Label == current.Label && existing.Note == current.Note
                    && existing.DisplayName == current.DisplayName
                    && existing.ChatStatus == current.ChatStatus && existing.CanChat == current.CanChat
                    && existing.ChatRequestRoute == current.ChatRequestRoute && existing.ChatRequestText == current.ChatRequestText)
                    return existing;
                return current;
            }).ToArray();
        FilterContacts();
        HasLoadedContacts = true;
    }

    void FilterContacts()
    {
        string filter = ContactSearchText.Trim();
        var contacts = filter.Length == 0 ? allContacts : allContacts
            .Where(p => p.DisplayName?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true
                || p.Address.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || (p.Note?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToArray();
        // Avoid resetting the native list for background notifications with no visible changes.
        if (!Contacts.SequenceEqual(contacts)) Contacts = contacts;
    }

    async void CheckForUpdates()
    {
        var last_check = await dbContext.Settings.GetAsync<DateTimeOffset>("system/last_update_check");
        if (last_check > DateTimeOffset.UtcNow - TimeSpan.FromDays(7)) return;
        await dbContext.Settings.PutAsync("system/last_update_check", DateTimeOffset.UtcNow);
        await Commands.CheckForUpdates.ExecuteAsync(true);
    }

    void Timer_Tick(object? sender, EventArgs e)
    {
        // There are some issues with CarouselView on Windows, so we disable auto-scrolling on that platform for now
#if !WINDOWS
        if (!isPageVisible || SelectedHomeSection != 2) return;
        if (Banners.Count > 1)
        {
            carouselView.Position = (carouselView.Position + 1) % Banners.Count;
        }
#endif
    }

    async void OnBannerTapped(object sender, TappedEventArgs e)
    {
        string url = (string)e.Parameter!;
        if (LaunchDAppAction.TryCreate(url) is LaunchDAppAction action)
            await Commands.LaunchDApp.ExecuteAsync(action.Uri);
        else
            await Commands.OpenUrl.ExecuteAsync(url);
    }

    async void OnNewsTapped(object sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("//home/news/details", new Dictionary<string, object>
        {
            ["news"] = e.Parameter!
        });
    }

    void OnNewsSizeChanged(object sender, EventArgs e)
    {
        double width = ((CollectionView)sender).Width;
        if (!double.IsFinite(width) || width <= 0) return;
        int span = Math.Max(1, (int)(width / 400));
        if (newsItemsLayout.Span != span) newsItemsLayout.Span = span;
    }

    async Task LoadBannersAsync()
    {
        await Banners.LoadAsync("/api/banners", TimeSpan.FromDays(1));
    }

    async Task LoadNewsAsync()
    {
        string url = "/api/news";
        string[] excluded = await dbContext.ExcludedNewsCategories.Select(p => p.Category).ToArrayAsync();
        if (excluded.Length > 0)
            url += "?" + string.Join('&', excluded.Select(p => "x=" + WebUtility.UrlEncode(p)));
        await News.LoadAsync(url, TimeSpan.FromMinutes(15));
    }
}
