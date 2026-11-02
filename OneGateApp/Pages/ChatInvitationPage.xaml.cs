using Neo.Wallets;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using NeoOrder.OneGate.Services.Messaging;
using System.Globalization;

namespace NeoOrder.OneGate.Pages;

public partial class ChatInvitationPage : ContentPage
{
    readonly MessagingService messagingService;
    readonly IWalletProvider walletProvider;
    readonly IDispatcherTimer expiryTimer;
    CancellationTokenSource? operation;
    ChatInvitation? invitation;
    Wallet? invitationWallet;
    string? localAccountId;
    bool isVisible;
    bool initialized;
    bool accountChanged;
    bool sharingInvitation;

    public bool IsGenerating => operation is not null;
    public bool HasInvitation => invitation is not null && !accountChanged
        && ReferenceEquals(invitationWallet, walletProvider.GetWallet())
        && invitation.Address == invitationWallet?.GetDefaultAccount()?.Address;
    public bool IsExpired => HasInvitation && invitation!.ExpiresAt <= DateTimeOffset.UtcNow;
    public bool CanCopyInvitation => isVisible && HasInvitation && messagingService.IsCurrentChatInvitation(invitation!);
    public bool CanShareInvitation => CanCopyInvitation && !IsGenerating && !sharingInvitation;
    public bool IsUnavailable => isVisible && HasInvitation && !IsExpired && !CanCopyInvitation;
    public bool CanGenerate => isVisible && !IsGenerating && !sharingInvitation && !accountChanged && (!initialized || messagingService.IsEnabled);
    public string? Address => HasInvitation ? invitation!.Address : null;
    public string? Nickname => HasInvitation && invitation!.LocalAccountId == messagingService.AccountId
        && messagingService.IsEnabled ? messagingService.ChatNickname : null;
    public string? InvitationValue => CanCopyInvitation ? invitation!.Value : null;
    public string? ExpiryText => HasInvitation ? string.Format(Strings.ChatInvitationExpiry,
        invitation!.ExpiresAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)) : null;
    public string? ErrorText { get; private set { field = value; OnPropertyChanged(); } }

    public ChatInvitationPage(MessagingService messagingService, IWalletProvider walletProvider)
    {
        this.messagingService = messagingService;
        this.walletProvider = walletProvider;
        expiryTimer = Dispatcher.CreateTimer();
        expiryTimer.IsRepeating = false;
        expiryTimer.Tick += (_, _) => UpdateExpiry();
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        isVisible = true;
        invitationWallet = walletProvider.GetWallet();
        initialized = false;
        accountChanged = false;
        walletProvider.WalletChanged += OnWalletChanged;
        messagingService.Changed += OnMessagingChanged;
        await GenerateAsync();
    }

    protected override void OnDisappearing()
    {
        isVisible = false;
        walletProvider.WalletChanged -= OnWalletChanged;
        messagingService.Changed -= OnMessagingChanged;
        operation?.Cancel();
        operation = null;
        expiryTimer.Stop();
        invitation = null;
        localAccountId = null;
        NotifyState();
        base.OnDisappearing();
    }

    void OnWalletChanged(object? sender, Wallet? wallet) => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (isVisible && !ReferenceEquals(invitationWallet, wallet)) ClearChangedAccount();
    });

    void OnMessagingChanged(object? sender, EventArgs e)
    {
        if (!isVisible) return;
        if (localAccountId is not null && messagingService.AccountId is { } current && current != localAccountId)
            ClearChangedAccount();
        else
            UpdateExpiry();
    }

    void ClearChangedAccount()
    {
        accountChanged = true;
        operation?.Cancel();
        invitation = null;
        expiryTimer.Stop();
        ErrorText = Strings.ChatInvitationAccountChanged;
        NotifyState();
    }

    async void OnRefreshInvitationClicked(object sender, EventArgs e) => await GenerateAsync();

    async Task GenerateAsync()
    {
        if (!CanGenerate) return;
        using var cancellation = new CancellationTokenSource();
        operation = cancellation;
        ErrorText = null;
        NotifyState();
        try
        {
            await messagingService.InitializeAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(invitationWallet, walletProvider.GetWallet()))
            {
                ClearChangedAccount();
                return;
            }
            initialized = true;
            if (!messagingService.IsEnabled) throw new InvalidOperationException(Strings.ChatEnableFirst);
            if (localAccountId is not null && localAccountId != messagingService.AccountId)
            {
                ClearChangedAccount();
                return;
            }
            localAccountId = messagingService.AccountId!;
            var result = await messagingService.CreateChatInvitationAsync(localAccountId, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            // Keep an existing, usable invitation if a manual refresh fails.
            invitation = result;
            UpdateExpiry();
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

    async void OnCopyInvitationClicked(object sender, EventArgs e)
    {
        UpdateExpiry();
        if (!CanCopyInvitation || invitation is not { } selected || !IsCurrentInvitation(selected)) return;
        try { await Commands.Copy.ExecuteAsync(selected.Value); }
        catch (Exception ex)
        {
            if (IsCurrentInvitation(selected) && messagingService.IsCurrentChatInvitation(selected))
                ErrorText = ex.Message;
        }
    }

    async void OnShareInvitationClicked(object sender, EventArgs e) => await ExportInvitationImageAsync(save: false);

    async void OnSaveInvitationClicked(object sender, EventArgs e) => await ExportInvitationImageAsync(save: true);

    async Task ExportInvitationImageAsync(bool save)
    {
        UpdateExpiry();
        if (!CanShareInvitation || invitation is not { } selected || !IsCurrentInvitation(selected)) return;
        sharingInvitation = true;
        ErrorText = null;
        NotifyState();
        try
        {
            var image = await invitationCard.CaptureAsync();
            if (!IsCurrentInvitation(selected) || !messagingService.IsCurrentChatInvitation(selected)) return;
            if (image is null) throw new InvalidOperationException(Strings.ScreenshotFailed);
            string fileName = $"onegate-invitation-{Guid.NewGuid():N}.png";
            if (save)
            {
                await PhotoLibraryService.SaveAsync(image, fileName);
            }
            else
            {
                string path = Path.Combine(FileSystem.CacheDirectory, fileName);
                // Close the PNG before the system share target opens it; retain it for deferred reads.
                await using (var stream = File.Open(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    await image.CopyToAsync(stream, ScreenshotFormat.Png);
                if (!IsCurrentInvitation(selected) || !messagingService.IsCurrentChatInvitation(selected)) return;
                await Share.RequestAsync(new ShareFileRequest
                {
                    Title = Strings.ChatMyInvitation,
                    File = new ShareFile(path, "image/png")
                });
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (IsCurrentInvitation(selected) && messagingService.IsCurrentChatInvitation(selected))
                ErrorText = ex.Message;
        }
        finally
        {
            sharingInvitation = false;
            NotifyState();
        }
    }

    async void OnCopyAddressClicked(object sender, EventArgs e)
    {
        if (invitation is not { } selected || !IsCurrentInvitation(selected)) return;
        try { await Commands.Copy.ExecuteAsync(selected.Address); }
        catch (Exception ex) { if (IsCurrentInvitation(selected)) ErrorText = ex.Message; }
    }

    bool IsCurrentInvitation(ChatInvitation selected) => isVisible
        && ReferenceEquals(Shell.Current?.CurrentPage, this)
        && ReferenceEquals(invitation, selected) && HasInvitation;

    void UpdateExpiry()
    {
        expiryTimer.Stop();
        if (isVisible && invitation is not null && invitation.ExpiresAt - DateTimeOffset.UtcNow is { Ticks: > 0 } remaining)
        {
            expiryTimer.Interval = remaining;
            expiryTimer.Start();
        }
        NotifyState();
    }

    void NotifyState()
    {
        OnPropertyChanged(nameof(IsGenerating));
        OnPropertyChanged(nameof(HasInvitation));
        OnPropertyChanged(nameof(IsExpired));
        OnPropertyChanged(nameof(IsUnavailable));
        OnPropertyChanged(nameof(CanCopyInvitation));
        OnPropertyChanged(nameof(CanShareInvitation));
        OnPropertyChanged(nameof(CanGenerate));
        OnPropertyChanged(nameof(Address));
        OnPropertyChanged(nameof(Nickname));
        OnPropertyChanged(nameof(InvitationValue));
        OnPropertyChanged(nameof(ExpiryText));
    }
}
