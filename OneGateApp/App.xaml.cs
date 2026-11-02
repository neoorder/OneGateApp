using Neo.Wallets;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Models.AppLinks;
using NeoOrder.OneGate.Pages;
using NeoOrder.OneGate.Services;
using NeoOrder.OneGate.Services.Messaging;
using NeoOrder.OneGate.Services.Notifications;
using NeoOrder.OneGate.Services.RemoteDebug;
using System.ComponentModel;
using System.Globalization;

namespace NeoOrder.OneGate;

public partial class App : Application
{
    readonly IServiceProvider serviceProvider;
    readonly IWalletProvider walletProvider;
    readonly MessagingService messagingService;
    readonly PushRegistrationService pushRegistrationService;
    readonly PushGatewayService pushGatewayService;
    readonly PushNavigationService pushNavigationService;
    readonly HashSet<Window> visibleMainWindows = [];

    Task pushCleanup = Task.CompletedTask;
    AppLinkAction? appLinkAction;

    public App(IServiceProvider serviceProvider, ApplicationDbContext dbContext, IWalletProvider walletProvider, HttpClient httpClient, RemoteDebugService remoteDebugService, MessagingService messagingService, PushRegistrationService pushRegistrationService, PushGatewayService pushGatewayService, PushNavigationService pushNavigationService)
    {
        this.serviceProvider = serviceProvider;
        this.walletProvider = walletProvider;
        this.messagingService = messagingService;
        this.pushRegistrationService = pushRegistrationService;
        this.pushGatewayService = pushGatewayService;
        this.pushNavigationService = pushNavigationService;
        pushRegistrationService.Changed += OnPlatformPushChanged;
        NotificationPreferences.Changed += OnNotificationPreferencesChanged;
        Connectivity.Current.ConnectivityChanged += OnPushConnectivityChanged;
        messagingService.Changed += OnMessagingChangedForPush;
        walletProvider.WalletChanged += OnWalletChanged;
        InitializeComponent();
        dbContext.Database.EnsureCreated();
        dbContext.EnsureMigrations();
        Version? version = dbContext.Settings.Get<Version>("system/version");
        if (version is null || version < AppInfo.Version)
        {
            dbContext.Settings.Put("system/version", AppInfo.Version);
            dbContext.Settings.Delete("system/updates");
        }
        string? lang = dbContext.Settings.Get("preference/language");
        if (!string.IsNullOrEmpty(lang))
        {
            CultureInfo culture = new(lang);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }
        httpClient.DefaultRequestHeaders.AcceptLanguage.Clear();
        httpClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd(CultureInfo.CurrentUICulture.Name);
        _ = InitializeRemoteDebugAsync(remoteDebugService, dbContext.Settings.Get<bool>(DAppCatalogPolicy.DeveloperModeKey));
    }

    static async Task InitializeRemoteDebugAsync(RemoteDebugService service, bool developerModeEnabled)
    {
        try
        {
            await service.SetDeveloperModeAsync(developerModeEnabled);
        }
        catch
        {
            // Developer diagnostics must not prevent the wallet from starting.
        }
    }

    internal bool ProcessAppLinkUri(Uri uri)
    {
        appLinkAction = AppLinkAction.TryCreate(uri);
        if (appLinkAction is null) return false;
#if IOS
        bool supportMultiWindow = DeviceInfo.Idiom == DeviceIdiom.Desktop || DeviceInfo.Idiom == DeviceIdiom.Tablet;
        if (!supportMultiWindow && Windows.Count > 0 && Windows[0].Page is AppShell shell)
        {
            _ = appLinkAction.GotoRoute(shell);
            appLinkAction = null;
        }
#endif
        return true;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        Page page;
        bool isMainWindow;
        if (walletProvider.GetWallet() is null)
        {
            page = serviceProvider.GetServiceOrCreateInstance<WelcomePage>();
            page = new NavigationPage(page);
            isMainWindow = true;
        }
        else
        {
            page = appLinkAction?.GetPage(serviceProvider)
                ?? serviceProvider.GetServiceOrCreateInstance<AppShell>();
            isMainWindow = page is AppShell;
            appLinkAction = null;
        }
        var window = new Window(page)
        {
            Title = "OneGate",
#if WINDOWS
            TitleBar = new TitleBar
            {
                Title = "OneGate",
                BackgroundColor = Color.FromArgb("#e8e8e8")
            }
#endif
        };
        // Independent DApp, news, payment and authentication windows do not own chat.
        if (isMainWindow)
        {
#if WINDOWS || MACCATALYST
            window.Activated += OnMainWindowVisible;
            window.Deactivated += OnMainWindowHidden;
#else
            window.Created += OnMainWindowVisible;
            window.Resumed += OnMainWindowVisible;
#endif
            window.Stopped += OnMainWindowHidden;
            window.Destroying += OnMainWindowHidden;
            window.PropertyChanged += OnMainWindowPropertyChanged;
        }
        return window;
    }

    async void OnMainWindowVisible(object? sender, EventArgs e)
    {
        if (sender is Window window && visibleMainWindows.Add(window))
            await UpdateMessagingVisibilityAsync();
    }

    async void OnMainWindowHidden(object? sender, EventArgs e)
    {
        if (sender is Window window && visibleMainWindows.Remove(window))
            await UpdateMessagingVisibilityAsync();
    }

    async void OnMainWindowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Creating/importing/deleting a wallet replaces the root page in the same window.
        if (e.PropertyName == nameof(Window.Page) && sender is Window window && visibleMainWindows.Contains(window))
            await UpdateMessagingVisibilityAsync();
    }

    async Task UpdateMessagingVisibilityAsync()
    {
        bool visible = visibleMainWindows.Any(window => window.Page is AppShell);
#if WINDOWS || MACCATALYST
        PushRegistrationService.IsAppForeground = visible;
#endif
        pushGatewayService.SetForeground(visible);
        pushNavigationService.SetForeground(visible);
        if (!visible) await pushCleanup;
        try { await messagingService.SetForegroundAsync(visible); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Meshline] Session lifecycle failed: {ex}"); }
        UpdatePushRegistration();
        pushNavigationService.TryOpen();
    }

    async void OnWalletChanged(object? sender, Wallet? wallet)
    {
        // Root-page replacement can race this event; keep the device session until cleanup finishes.
        if (wallet is null) await (pushCleanup = pushGatewayService.UnregisterAsync());
        try { await messagingService.WalletChangedAsync(wallet); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Meshline] Wallet session update failed: {ex}"); }
    }
}
