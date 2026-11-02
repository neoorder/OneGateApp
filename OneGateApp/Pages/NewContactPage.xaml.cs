using CommunityToolkit.Maui.Alerts;
using Microsoft.EntityFrameworkCore;
using Neo.Wallets;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Controls.Views.Validation;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using NeoOrder.OneGate.Services.Messaging;
using Contact = NeoOrder.OneGate.Data.Contact;

namespace NeoOrder.OneGate.Pages;

public partial class NewContactPage : ContentPage, IQueryAttributable
{
    readonly ApplicationDbContext dbContext;
    readonly IWalletProvider walletProvider;
    readonly AddressBookService addressBookService;
    readonly MessagingService messagingService;
    CancellationTokenSource? operation;
    Wallet? lookupWallet;
    WalletAccount? lookupAccount;
    string? lookupAccountId;
    bool isPageVisible;

    public string? Input
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            operation?.Cancel();
            ClearResult();
            Label = null;
            Note = null;
            OnPropertyChanged();
            NotifyState();
        }
    }
    public string? Label { get; set { field = value; OnPropertyChanged(); } }
    public string? Note { get; set { field = value; OnPropertyChanged(); } }
    public string? ResolvedAddress { get; private set; }
    public string? AddressOnlyHint { get; private set; }
    public string? ErrorText { get; private set; }
    public bool CanSaveAddress { get; private set; }
    public bool IsWorking => operation is not null;
    public bool IsSaving { get; private set; }
    public bool CanSearch => isPageVisible && !IsWorking && !string.IsNullOrWhiteSpace(Input);

    public NewContactPage(ApplicationDbContext dbContext, IWalletProvider walletProvider,
        AddressBookService addressBookService, MessagingService messagingService)
    {
        this.dbContext = dbContext;
        this.walletProvider = walletProvider;
        this.addressBookService = addressBookService;
        this.messagingService = messagingService;
        InitializeComponent();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("input", out var input) || query.TryGetValue("address", out input))
            Input = (string)input;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        isPageVisible = true;
        walletProvider.WalletChanged += OnWalletChanged;
        NotifyState();
    }

    protected override void OnDisappearing()
    {
        isPageVisible = false;
        walletProvider.WalletChanged -= OnWalletChanged;
        operation?.Cancel();
        ClearResult();
        NotifyState();
        base.OnDisappearing();
    }

    void OnWalletChanged(object? sender, Wallet? wallet) => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (!isPageVisible) return;
        operation?.Cancel();
        ClearResult();
        ErrorText = Strings.ChatAccountChanged;
        NotifyState();
    });

    async void OnPasteClicked(object sender, EventArgs e) => await ReadInputAsync(scan: false);

    async void OnScanClicked(object sender, EventArgs e) => await ReadInputAsync(scan: true);

    async Task ReadInputAsync(bool scan)
    {
        if (!isPageVisible || IsWorking) return;
        using var cancellation = new CancellationTokenSource();
        operation = cancellation;
        ErrorText = null;
        NotifyState();
        try
        {
            if (scan)
            {
                await Commands.ScanQRCode.ExecuteAsync("?action=AddContact");
            }
            else
            {
                string? text = await Clipboard.Default.GetTextAsync();
                cancellation.Token.ThrowIfCancellationRequested();
                if (!string.IsNullOrWhiteSpace(text)) Input = text;
            }
        }
        catch (Exception) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) { ErrorText = ex.Message; }
        finally
        {
            if (ReferenceEquals(operation, cancellation))
            {
                operation = null;
                NotifyState();
            }
        }
    }

    async void OnSearchClicked(object sender, EventArgs e) => await SearchAsync();

    async Task SearchAsync()
    {
        if (!CanSearch || !isPageVisible) return;
        using var cancellation = new CancellationTokenSource();
        operation = cancellation;
        ClearResult();
        NotifyState();
        string input = Input!.Trim();
        try
        {
            lookupWallet = walletProvider.GetWallet();
            lookupAccount = lookupWallet?.GetDefaultAccount();
            await messagingService.InitializeAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            lookupAccountId = messagingService.AccountId;
            EnsureCurrentAccount();
            if (input.StartsWith('{'))
            {
                if (!messagingService.IsEnabled) throw new InvalidOperationException(Strings.ChatEnableFirst);
                messagingService.ParseChatInvitation(input);
                await Shell.Current.GoToAsync("chat-request", new ShellNavigationQueryParameters { ["invitation"] = input });
                return;
            }
            if (!addressBookService.TryNormalizeAddress(input, out string address))
                throw new ArgumentException(Strings.ChatInvalidAddress);
            // Existing requests remain reachable even after failure/cancellation or discovery is disabled.
            var details = messagingService.IsEnabled
                ? await messagingService.GetChatContactAsync(address, resolveProfile: false, cancellationToken: cancellation.Token) : null;
            cancellation.Token.ThrowIfCancellationRequested();
            EnsureCurrentAccount();
            if (details is { CanCreateRequest: true, Request: null })
            {
                var profile = await messagingService.GetChatProfileAsync(details.LocalAccountId, address, cancellation.Token);
                cancellation.Token.ThrowIfCancellationRequested();
                EnsureCurrentAccount();
                details = details with { Profile = profile };
            }
            string? route = details is null ? null : details.IsContact ? "//home/chat"
                : details.Request is not null || !details.CanCreateRequest || details.Profile is { PublicDiscovery: true } ? "chat-request" : null;
            if (route is not null)
            {
                await Shell.Current.GoToAsync(route, new ShellNavigationQueryParameters { ["address"] = address });
                return;
            }
            // With chat enabled, only a successful non-discoverable lookup reaches address-only saving.
            var savedContact = await addressBookService.FindByAddressAsync(address, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            EnsureCurrentAccount();
            if (savedContact is not null)
            {
                await Shell.Current.GoToAsync("contact-details", new ShellNavigationQueryParameters
                {
                    ["contact"] = new ContactViewModel(savedContact, details?.Status ?? Strings.HomeAddressOnly)
                });
                return;
            }
            ResolvedAddress = address;
            AddressOnlyHint = details is null ? Strings.ContactChatDisabledHint : Strings.ContactAddressOnlyHint;
            CanSaveAddress = true;
        }
        catch (Exception) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) { ErrorText = ex.Message; }
        finally
        {
            operation = null;
            NotifyState();
        }
    }

    void EnsureCurrentAccount()
    {
        if (lookupWallet is null || lookupAccount is null
            || !ReferenceEquals(lookupWallet, walletProvider.GetWallet())
            || !ReferenceEquals(lookupAccount, lookupWallet.GetDefaultAccount())
            || lookupAccountId != messagingService.AccountId)
            throw new InvalidOperationException(Strings.ChatAccountChanged);
    }

    void OnValidateLabelDuplicity(object sender, CustomValidationEventArgs e)
    {
        e.IsValid = addressBookService.IsLabelAvailable(e.Value as string, ResolvedAddress);
        if (!e.IsValid) e.ErrorMessage = Strings.LabelAlreadyExists;
    }

    async void OnSubmitted(object sender, EventArgs e)
    {
        if (!isPageVisible || !CanSaveAddress || IsWorking || ResolvedAddress is not { } address) return;
        using var cancellation = new CancellationTokenSource();
        operation = cancellation;
        IsSaving = true;
        ErrorText = null;
        NotifyState();
        Contact? contact = null;
        try
        {
            EnsureCurrentAccount();
            string label = Label?.Trim() ?? "";
            if (label.Length == 0 || !addressBookService.IsLabelAvailable(label, address))
                throw new InvalidOperationException(Strings.LabelAlreadyExists);
            if (await dbContext.Contacts.AnyAsync(p => p.Address == address, cancellation.Token))
                throw new InvalidOperationException(Strings.AddressAlreadyExists);
            cancellation.Token.ThrowIfCancellationRequested();
            EnsureCurrentAccount();
            contact = new Contact { Address = address, Label = label, Note = Note?.Trim() };
            dbContext.Contacts.Add(contact);
            await dbContext.SaveChangesAsync(cancellation.Token);
            CanSaveAddress = false;
            cancellation.Token.ThrowIfCancellationRequested();
            EnsureCurrentAccount();
            await Toast.Show(Strings.ContactAddedSuccessfully);
            cancellation.Token.ThrowIfCancellationRequested();
            EnsureCurrentAccount();
            await Shell.Current.GoToAsync("..");
        }
        catch (Exception) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) { ErrorText = ex.Message; }
        finally
        {
            if (contact is not null) dbContext.Entry(contact).State = EntityState.Detached;
            IsSaving = false;
            operation = null;
            NotifyState();
        }
    }

    void ClearResult()
    {
        CanSaveAddress = false;
        ResolvedAddress = null;
        AddressOnlyHint = null;
        lookupWallet = null;
        lookupAccount = null;
        lookupAccountId = null;
        ErrorText = null;
    }

    void NotifyState()
    {
        OnPropertyChanged(nameof(ResolvedAddress));
        OnPropertyChanged(nameof(AddressOnlyHint));
        OnPropertyChanged(nameof(CanSaveAddress));
        OnPropertyChanged(nameof(CanSearch));
        OnPropertyChanged(nameof(IsWorking));
        OnPropertyChanged(nameof(IsSaving));
        OnPropertyChanged(nameof(ErrorText));
    }
}
