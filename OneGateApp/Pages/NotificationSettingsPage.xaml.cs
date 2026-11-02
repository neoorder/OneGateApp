using CommunityToolkit.Maui.Extensions;
using Neo.Wallets;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services.Messaging;
using NeoOrder.OneGate.Services.Notifications;

namespace NeoOrder.OneGate.Pages;

public partial class NotificationSettingsPage : ContentPage
{
    readonly MessagingService messaging;
    readonly PushRegistrationService registration;
    readonly PushGatewayService gateway;
    readonly IWalletProvider walletProvider;
    Wallet? settingsWallet;
    WalletAccount? settingsAccount;
    Window? settingsWindow;
    CancellationTokenSource? operation;
    NotificationPermissionStatus permission;
    bool active;
    bool rendering;
    bool initialized;
    bool accountChanged;
    int permissionRequest;

    bool IsCurrentAccount => !accountChanged && ReferenceEquals(settingsWallet, walletProvider.GetWallet())
        && ReferenceEquals(settingsAccount, settingsWallet?.GetDefaultAccount());
    public bool IsWorking => operation is not null;
    public bool CanEdit => active && IsCurrentAccount && !IsWorking;
    bool ReceiveNotifications => NotificationPreferences.Enabled;
    bool ReceiveChat => ReceiveNotifications && messaging.ChatNotificationsEnabled;
    public bool CanToggle => active && !IsWorking;
    public bool CanToggleChat => CanEdit && initialized && ReceiveNotifications && messaging.IsEnabled;
    public bool NeedsAuthorization => IsCurrentAccount && initialized && ReceiveChat && messaging.NeedsPushGatewayDeclaration;
    public bool ShowPermissionWarning => ReceiveChat && permission != NotificationPermissionStatus.Allowed;
    public bool IsOffline => Connectivity.Current.NetworkAccess != NetworkAccess.Internet;
    public string PreferenceText => ReceiveNotifications ? Strings.NotificationOnDevice : Strings.NotificationPaused;
    public bool CanRequestPermission => permission == NotificationPermissionStatus.NotDetermined;
    public bool CanRetry => IsCurrentAccount && (!initialized || messaging.IsEnabled && ReceiveChat
        && (messaging.HasPendingPushGatewayUpdate || messaging.SupportsPushGateway
            && registration.Status is PushRegistrationStatus.NotStarted or PushRegistrationStatus.Failed
                or PushRegistrationStatus.ServiceUnavailable));
    public string? ErrorText { get; private set; }
    public string PermissionText => permission switch
    {
        NotificationPermissionStatus.Allowed => Strings.Enabled,
        NotificationPermissionStatus.Denied => Strings.Disabled,
        NotificationPermissionStatus.NotDetermined => Strings.NotificationPermissionPending,
        _ => Strings.Unavailable
    };
    public string ChatStatusText
    {
        get
        {
            if (!IsCurrentAccount) return Strings.ChatAccountChanged;
            if (!initialized) return IsWorking ? Strings.Loading : Strings.NotificationSettingsFailed;
            if (!messaging.IsEnabled) return Strings.ChatEnableFirst;
            if (!ReceiveChat) return Strings.Disabled;
            if (messaging.HasPendingPushGatewayUpdate) return Strings.NotificationUpdatePending;
            if (!messaging.SupportsPushGateway) return Strings.NotificationLocalOnly;
            if (!messaging.HasPushGateway) return Strings.NotificationSetupNeeded;
            return registration.Status switch
            {
                PushRegistrationStatus.Registered => Strings.NotificationChatReady,
                PushRegistrationStatus.Registering or PushRegistrationStatus.NotStarted => Strings.NotificationConnecting,
                PushRegistrationStatus.PermissionDenied or PushRegistrationStatus.PermissionRequired => Strings.NotificationAllowHint,
                PushRegistrationStatus.NotConfigured or PushRegistrationStatus.NotSupported => Strings.NotificationUnavailable,
                _ => Strings.NotificationRetryHint
            };
        }
    }

    public NotificationSettingsPage(MessagingService messaging, PushRegistrationService registration,
        PushGatewayService gateway, IWalletProvider walletProvider)
    {
        this.messaging = messaging;
        this.registration = registration;
        this.gateway = gateway;
        this.walletProvider = walletProvider;
        InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        if (active) return;
        active = true;
        initialized = false;
        accountChanged = false;
        settingsWallet = walletProvider.GetWallet();
        settingsAccount = settingsWallet?.GetDefaultAccount();
        settingsWindow = Window;
        if (settingsWindow is not null)
        {
            settingsWindow.Activated += OnWindowActivated;
            settingsWindow.Resumed += OnWindowActivated;
        }
        walletProvider.WalletChanged += OnWalletChanged;
        messaging.Changed += OnStateChanged;
        registration.Changed += OnStateChanged;
        NotificationPreferences.Changed += OnStateChanged;
        Connectivity.Current.ConnectivityChanged += OnConnectivityChanged;
        Render();
        await RefreshPermissionAsync();
        if (CanEdit) await RunAsync(InitializeAsync);
    }

    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        // Wallet authorization uses a toolkit popup; it must not cancel its own operation.
        if (args.IsDestinationPageACommunityToolkitPopupPage())
        {
            base.OnNavigatedFrom(args);
            return;
        }
        active = false;
        permissionRequest++;
        operation?.Cancel();
        operation = null;
        if (settingsWindow is not null)
        {
            settingsWindow.Activated -= OnWindowActivated;
            settingsWindow.Resumed -= OnWindowActivated;
            settingsWindow = null;
        }
        walletProvider.WalletChanged -= OnWalletChanged;
        messaging.Changed -= OnStateChanged;
        registration.Changed -= OnStateChanged;
        NotificationPreferences.Changed -= OnStateChanged;
        Connectivity.Current.ConnectivityChanged -= OnConnectivityChanged;
        base.OnNavigatedFrom(args);
    }

    async void OnWindowActivated(object? sender, EventArgs e)
    {
        await RefreshPermissionAsync();
        if (CanEdit && initialized && messaging.IsEnabled) await RunAsync(RefreshRegistrationAsync);
    }

    void OnWalletChanged(object? sender, Wallet? wallet) => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (!active || IsCurrentAccount) return;
        accountChanged = true;
        operation?.Cancel();
        ErrorText = Strings.ChatAccountChanged;
        Render();
    });

    void OnStateChanged(object? sender, EventArgs e) => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (!active) return;
        Render();
        _ = RefreshPermissionAsync();
    });

    void OnConnectivityChanged(object? sender, ConnectivityChangedEventArgs e) =>
        MainThread.BeginInvokeOnMainThread(() => { if (active) Render(); });

    void OnNotificationsToggled(object sender, ToggledEventArgs e)
    {
        if (rendering) return;
        if (!CanToggle) { Render(); return; }
        if (ReceiveNotifications == e.Value) return;
        try
        {
            ErrorText = null;
            NotificationPreferences.SetEnabled(e.Value);
        }
        catch (Exception) { ErrorText = Strings.NotificationSettingsFailed; }
        Render();
    }

    async void OnChatToggled(object sender, ToggledEventArgs e)
    {
        if (rendering) return;
        if (!CanToggleChat || e.Value == messaging.ChatNotificationsEnabled) { Render(); return; }
        bool enabled = e.Value;
        try
        {
            ErrorText = null;
            messaging.SetChatNotificationsEnabled(enabled);
        }
        catch (Exception)
        {
            ErrorText = Strings.NotificationSettingsFailed;
            Render();
            return;
        }
        Render();
        if (enabled && messaging.SupportsPushGateway
            && (!messaging.HasPushGateway || messaging.HasPendingPushGatewayUpdate))
            await RunAsync(EnableChatNotificationsAsync);
    }

    async Task EnableChatNotificationsAsync(CancellationToken token)
    {
        try
        {
            await messaging.ConfigurePushGatewayAsync(this, token);
        }
        catch (OperationCanceledException)
        {
            // Cancelling initial authorization leaves reminders off. A signed pending update is retained.
            if (IsCurrentAccount && !messaging.HasPendingPushGatewayUpdate)
                messaging.SetChatNotificationsEnabled(false);
            throw;
        }
        token.ThrowIfCancellationRequested();
        if (IsCurrentAccount) await RefreshRegistrationAsync(token);
    }

    async Task CompleteSetupAsync(CancellationToken token)
    {
        await messaging.ConfigurePushGatewayAsync(this, token);
        token.ThrowIfCancellationRequested();
        if (IsCurrentAccount) await RefreshRegistrationAsync(token);
    }

    async void OnCompleteSetupClicked(object sender, EventArgs e)
    {
        if (CanEdit && NeedsAuthorization) await RunAsync(CompleteSetupAsync);
    }

    async Task RefreshPermissionAsync()
    {
        int request = ++permissionRequest;
        try
        {
            var current = await registration.CheckPermissionAsync();
            if (!active || request != permissionRequest) return;
            permission = current;
        }
        catch (Exception)
        {
            if (!active || request != permissionRequest) return;
            permission = NotificationPermissionStatus.Unknown;
        }
        Render();
    }

    async Task InitializeAsync(CancellationToken token)
    {
        await messaging.InitializeAsync(token);
        token.ThrowIfCancellationRequested();
        if (!IsCurrentAccount) return;
        initialized = true;
        if (messaging.IsEnabled) await RefreshRegistrationAsync(token);
    }

    Task RefreshRegistrationAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (IsCurrentAccount)
        {
            gateway.RequestSync(force: true);
            // Network registration runs independently so offline settings remain editable.
            if (ReceiveChat) _ = registration.RefreshAsync();
        }
        return Task.CompletedTask;
    }

    async void OnRetryClicked(object sender, EventArgs e)
    {
        if (!CanEdit || !CanRetry) return;
        if (initialized && messaging.HasPendingPushGatewayUpdate)
            await RunAsync(CompleteSetupAsync);
        else
            await RunAsync(InitializeAsync);
    }

    async void OnAllowClicked(object sender, EventArgs e)
    {
        if (!CanEdit || !CanRequestPermission) return;
        await RunAsync(async token =>
        {
            await registration.CheckPermissionAsync(requestPermission: true);
            token.ThrowIfCancellationRequested();
            if (IsCurrentAccount && messaging.IsEnabled) await RefreshRegistrationAsync(token);
        });
    }

    async void OnOpenSettingsClicked(object sender, EventArgs e)
    {
        if (CanEdit) await RunAsync(_ => registration.OpenNotificationSettingsAsync());
    }

    async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (!CanEdit) return;
        using var cancellation = new CancellationTokenSource();
        operation = cancellation;
        ErrorText = null;
        Render();
        try { await action(cancellation.Token); }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (active && IsCurrentAccount && ReferenceEquals(operation, cancellation))
                ErrorText = Strings.NotificationSettingsFailed;
        }
        finally
        {
            if (ReferenceEquals(operation, cancellation))
            {
                operation = null;
                await RefreshPermissionAsync();
            }
        }
    }

    void Render()
    {
        rendering = true;
        try
        {
            notificationsEnabled.IsToggled = ReceiveNotifications;
            chatEnabled.IsToggled = IsCurrentAccount && messaging.ChatNotificationsEnabled;
        }
        finally { rendering = false; }
        OnPropertyChanged(nameof(CanToggle));
        OnPropertyChanged(nameof(CanToggleChat));
        OnPropertyChanged(nameof(ShowPermissionWarning));
        OnPropertyChanged(nameof(IsOffline));
        OnPropertyChanged(nameof(PreferenceText));
        OnPropertyChanged(nameof(IsWorking));
        OnPropertyChanged(nameof(CanEdit));
        OnPropertyChanged(nameof(CanRequestPermission));
        OnPropertyChanged(nameof(CanRetry));
        OnPropertyChanged(nameof(NeedsAuthorization));
        OnPropertyChanged(nameof(PermissionText));
        OnPropertyChanged(nameof(ChatStatusText));
        OnPropertyChanged(nameof(ErrorText));
    }
}
