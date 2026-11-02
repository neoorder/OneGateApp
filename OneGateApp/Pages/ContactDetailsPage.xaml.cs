using CommunityToolkit.Maui.Alerts;
using Meshline.Models.Client;
using Meshline.Models.Protocol;
using Neo.Wallets;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Services;
using NeoOrder.OneGate.Services.Messaging;
using NeoOrder.OneGate.Properties;
using Contact = NeoOrder.OneGate.Data.Contact;

namespace NeoOrder.OneGate.Pages;

public partial class ContactDetailsPage : ContentPage, IQueryAttributable
{
    readonly AddressBookService addressBookService;
    readonly MessagingService messagingService;
    readonly IWalletProvider walletProvider;
    bool isPageVisible;
    bool reloadPending;
    bool hasLoaded;
    Contact? savedContact;
    bool hasLoadedSavedContact;
    bool refreshSavedContact;
    int loadVersion;
    ChatContactDetails? chatDetails;
    CancellationTokenSource? loadCancellation;
    bool isNavigating;
    bool confirmingChatRemoval;
    bool removingChatContact;
    AccountProfile? ContactProfile => IsChatEnabled && Contact?.CanChat == true
        ? messagingService.GetChatDisplayProfile(chatDetails!.LocalAccountId, Contact.Address) : null;

    public LoadingService LoadingService { get; }
    public string? ErrorText { get; private set { field = value; OnPropertyChanged(); } }
    public bool ShowLoading => LoadingService.IsLoading && !hasLoaded;
    public bool IsChatEnabled => isPageVisible && chatDetails is not null && messagingService.IsEnabled
        && chatDetails.LocalAccountId == messagingService.AccountId && chatDetails.Address == Contact?.Address
        && chatDetails.Address != messagingService.Address;
    public ContactViewModel? Contact { get; private set { field = value; OnPropertyChanged(); } }

    public bool CanNavigate => isPageVisible && Contact is not null && !ShowLoading && !isNavigating && !removingChatContact;
    bool CanEditContact => CanNavigate && (Contact?.IsSaved == true
        || IsChatEnabled && (Contact is { CanChat: true } or { HasOutgoingRequest: true }));
    bool CanRemoveChatContact => CanNavigate && IsChatEnabled && Contact?.CanChat == true;
    public Command EditContactCommand { get; }
    public Command RemoveChatContactCommand { get; }
    public string? ProfileNickname => ContactProfile?.Nickname == Contact?.DisplayName ? null : ContactProfile?.Nickname;
    public string? ProfileBio => ContactProfile?.Bio;

    public ContactDetailsPage(AddressBookService addressBookService, MessagingService messagingService, IWalletProvider walletProvider)
    {
        this.addressBookService = addressBookService;
        this.messagingService = messagingService;
        this.walletProvider = walletProvider;
        EditContactCommand = new(async () => await EditContactAsync(), () => CanEditContact);
        RemoveChatContactCommand = new(async () => await RemoveChatContactAsync(), () => !confirmingChatRemoval && CanRemoveChatContact);
        LoadingService = new(LoadContactAsync);
        LoadingService.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LoadingService.IsLoading))
            {
                OnPropertyChanged(nameof(ShowLoading));
                NotifyNavigationState();
            }
        };
        InitializeComponent();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("contact", out var contact))
        {
            loadVersion++;
            hasLoaded = false;
            savedContact = null;
            hasLoadedSavedContact = false;
            refreshSavedContact = true;
            chatDetails = null;
            ErrorText = null;
            Contact = (ContactViewModel)contact;
            NotifyChatState();
            OnPropertyChanged(nameof(ShowLoading));
            loadCancellation?.Cancel();
            if (isPageVisible) OnMessagingChanged(this, EventArgs.Empty);
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        isPageVisible = true;
        refreshSavedContact = true;
        messagingService.Changed += OnMessagingChanged;
        messagingService.ChatProfilesChanged += OnChatProfilesChanged;
        NotifyChatState();
        reloadPending = true;
        LoadingService.BeginLoad();
    }

    protected override void OnDisappearing()
    {
        isPageVisible = false;
        messagingService.Changed -= OnMessagingChanged;
        messagingService.ChatProfilesChanged -= OnChatProfilesChanged;
        reloadPending = false;
        loadCancellation?.Cancel();
        loadVersion++;
        chatDetails = null;
        NotifyChatState();
        base.OnDisappearing();
    }

    void OnMessagingChanged(object? sender, EventArgs e)
    {
        if (!isPageVisible) return;
        if (chatDetails is not null && !IsChatEnabled)
        {
            chatDetails = null;
            loadVersion++;
        }
        NotifyChatState();
        reloadPending = true;
        LoadingService.BeginLoad();
    }

    void OnRetryClicked(object sender, EventArgs e)
    {
        if (!isPageVisible || Contact is null || LoadingService.IsLoading) return;
        refreshSavedContact = true;
        LoadingService.BeginLoad();
    }

    async Task LoadContactAsync()
    {
        if (LoadingService.IsReloading) { refreshSavedContact = true; messagingService.RefreshVisibleChatProfiles(); }
        do
        {
            reloadPending = false;
            using var cancellation = new CancellationTokenSource();
            loadCancellation = cancellation;
            try { await ReadContactAsync(cancellation.Token); }
            finally { loadCancellation = null; }
        } while (isPageVisible && reloadPending);
    }

    async Task ReadContactAsync(CancellationToken cancellationToken)
    {
        if (!isPageVisible || Contact is null) return;
        string address = Contact.Address;
        int version = loadVersion;
        bool IsCurrent() => !cancellationToken.IsCancellationRequested && isPageVisible
            && version == loadVersion && Contact?.Address == address;
        ErrorText = null;
        try
        {
            // A failed explicit refresh keeps saved metadata available to background updates.
            if (refreshSavedContact || !hasLoadedSavedContact)
            {
                refreshSavedContact = false;
                var saved = await addressBookService.FindByAddressAsync(address, cancellationToken);
                if (!IsCurrent()) return;
                // Cache absence too: a chat-only friend does not need an address-book read on every message.
                savedContact = saved;
                hasLoadedSavedContact = true;
            }
            Contact? contact = savedContact;

            // Local names and notes stay usable independently of chat-state reads.
            if (contact is not null) UpdateContact(contact, IsChatEnabled ? chatDetails : null);

            await messagingService.InitializeAsync(cancellationToken);
            if (!IsCurrent()) return;
            ChatContactDetails? details = null;
            if (messagingService.IsEnabled && address != messagingService.Address)
            {
                details = await messagingService.GetChatContactAsync(address, resolveProfile: false, cancellationToken: cancellationToken);
                if (!IsCurrent()) return;
                if (!messagingService.IsEnabled || details.LocalAccountId != messagingService.AccountId)
                {
                    reloadPending = true;
                    return;
                }
            }

            if (contact is null && details?.IsContact != true && details?.Request?.Direction != ContactRequestDirection.Outgoing)
            {
                await Shell.Current.GoToAsync("..");
                return;
            }
            UpdateContact(contact, details);
        }
        catch (Exception) when (!IsCurrent()) { }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
            reloadPending = false;
        }
        finally
        {
            if (IsCurrent())
            {
                if (!IsChatEnabled) chatDetails = null;
                NotifyChatState();
            }
        }
    }

    void UpdateContact(Contact? contact, ChatContactDetails? details)
    {
        chatDetails = details;
        string? displayName = details is null ? null : messagingService.GetChatDisplayName(
            details.LocalAccountId, details.Address, contact?.Label);
        var current = contact is not null
            ? new ContactViewModel(contact, details?.Status ?? Strings.ChatStatusUnavailable,
                details?.IsContact == true, details is { IsContact: false, Request.Direction: ContactRequestDirection.Incoming }, displayName: displayName,
                hasOutgoingRequest: details is { IsContact: false, Request.Direction: ContactRequestDirection.Outgoing })
            : details!.IsContact ? ContactViewModel.FromChatContact(details.Address, displayName)
            : ContactViewModel.FromChatRequest(details.Address, details.Status, displayName);
        // Background notifications with unchanged content must not rebind the details view.
        if (Contact is not { } existing || existing.Address != current.Address
            || existing.IsSaved != current.IsSaved || existing.Label != current.Label || existing.Note != current.Note
            || existing.DisplayName != current.DisplayName
            || existing.ChatStatus != current.ChatStatus || existing.CanChat != current.CanChat
            || existing.ChatRequestRoute != current.ChatRequestRoute || existing.ChatRequestText != current.ChatRequestText)
            Contact = current;
        hasLoaded = true;
        NotifyChatState();
        OnPropertyChanged(nameof(ShowLoading));
    }

    void NotifyChatState()
    {
        OnPropertyChanged(nameof(IsChatEnabled));
        NotifyNavigationState();
        UpdateProfile();
    }

    async void OnChatClicked(object sender, EventArgs e)
    {
        if (!IsChatEnabled || Contact is not { } contact) return;
        string route = contact.CanChat ? $"//home/chat?address={Uri.EscapeDataString(contact.Address)}" : contact.ChatRequestRoute;
        await NavigateAsync(contact, route);
    }

    async void OnTransferClicked(object sender, EventArgs e)
    {
        if (Contact is not { } contact) return;
        await NavigateAsync(contact, $"//wallet/send?address={Uri.EscapeDataString(contact.Address)}");
    }

    async Task EditContactAsync()
    {
        if (!CanEditContact || Contact is not { } contact) return;
        if (contact.IsSaved)
            await NavigateAsync(contact, $"edit-contact?address={Uri.EscapeDataString(contact.Address)}");
        else
            await NavigateAsync(contact, "edit-contact", new ShellNavigationQueryParameters { ["chat-contact"] = contact });
    }

    void NotifyNavigationState()
    {
        OnPropertyChanged(nameof(CanNavigate));
        EditContactCommand.ChangeCanExecute();
        RemoveChatContactCommand.ChangeCanExecute();
    }

    async Task NavigateAsync(ContactViewModel contact, string route, ShellNavigationQueryParameters? parameters = null)
    {
        var shell = Shell.Current;
        if (!CanNavigate || shell?.CurrentPage != this) return;
        isNavigating = true;
        NotifyNavigationState();
        try
        {
            if (parameters is null) await shell.GoToAsync(route);
            else await shell.GoToAsync(route, parameters);
        }
        catch (Exception ex)
        {
            if (isPageVisible && shell.CurrentPage == this && Contact?.Address == contact.Address)
                await Toast.Show(ex.Message);
        }
        finally
        {
            isNavigating = false;
            NotifyNavigationState();
        }
    }

    async Task RemoveChatContactAsync()
    {
        var shell = Shell.Current;
        if (confirmingChatRemoval || !CanRemoveChatContact || Contact is not { } contact || shell?.CurrentPage != this) return;
        var wallet = walletProvider.GetWallet();
        string? accountId = messagingService.AccountId;
        bool IsCurrent() => isPageVisible && shell.CurrentPage == this && Contact?.Address == contact.Address;
        bool IsCurrentChat() => IsCurrent() && CanRemoveChatContact && messagingService.IsEnabled && accountId is not null
            && accountId == messagingService.AccountId && ReferenceEquals(wallet, walletProvider.GetWallet())
            && wallet?.GetDefaultAccount()?.Address == messagingService.Address;
        confirmingChatRemoval = true;
        NotifyNavigationState();
        try
        {
            if (!IsCurrentChat()) return;
            bool confirmed = await DisplayAlertAsync(Strings.ChatRemoveContact,
                string.Format(Strings.ChatRemoveContactPrompt, contact.DisplayName ?? contact.Address), Strings.Delete, Strings.Cancel);
            if (!confirmed || !IsCurrentChat()) return;
            removingChatContact = true;
            NotifyNavigationState();
            await messagingService.RemoveChatContactAsync(accountId!, contact.Address);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (IsCurrent() && accountId == messagingService.AccountId && ReferenceEquals(wallet, walletProvider.GetWallet()))
                await Toast.Show(ex.Message);
        }
        finally
        {
            removingChatContact = false;
            confirmingChatRemoval = false;
            NotifyNavigationState();
        }
    }

    void OnChatProfilesChanged(object? sender, EventArgs e)
    {
        if (isPageVisible) UpdateProfile();
    }

    void UpdateProfile()
    {
        NotifyProfile();
        if (IsChatEnabled && Contact?.CanChat == true)
            _ = messagingService.RefreshChatDisplayProfileAsync(chatDetails!.LocalAccountId, Contact.Address);
    }

    void NotifyProfile()
    {
        OnPropertyChanged(nameof(ProfileNickname));
        OnPropertyChanged(nameof(ProfileBio));
    }
}
