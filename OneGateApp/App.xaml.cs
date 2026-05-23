using Neo;
using Neo.Wallets;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Models.AppLinks;
using NeoOrder.OneGate.Pages;
using NeoOrder.OneGate.Services;
using System.Globalization;

namespace NeoOrder.OneGate;

public partial class App : Application
{
    readonly IServiceProvider serviceProvider;
    readonly IWalletProvider walletProvider;

    AppLinkAction? appLinkAction;

    public App(IServiceProvider serviceProvider, ApplicationDbContext dbContext, IWalletProvider walletProvider, HttpClient httpClient)
    {
        this.serviceProvider = serviceProvider;
        this.walletProvider = walletProvider;
        InitializeComponent();
        dbContext.Database.EnsureCreated();
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
    }

    internal bool ProcessAppLinkUri(Uri uri)
    {
        appLinkAction = uri.Scheme switch
        {
            "neo" => ProcessNeoScheme(uri),
            "neoauth" => ProcessNeoAuthScheme(uri),
            "https" => ProcessHttpsScheme(uri),
            _ => null
        };
        if (appLinkAction is null) return false;
#if IOS
        bool supportMultiWindow = DeviceInfo.Idiom == DeviceIdiom.Desktop || DeviceInfo.Idiom == DeviceIdiom.Tablet;
        if (!supportMultiWindow && Windows.Count > 0 && Windows[0].Page is AppShell shell)
        {
            appLinkAction.GotoRoute(shell);
        }
#endif
        return true;
    }

    AppLinkAction? ProcessNeoScheme(Uri uri)
    {
        ProtocolSettings protocolSettings = serviceProvider.GetRequiredService<ProtocolSettings>();
        try
        {
            return new PaymentAction(uri, protocolSettings);
        }
        catch
        {
            return null;
        }
    }

    static AppLinkAction? ProcessNeoAuthScheme(Uri uri)
    {
        try
        {
            return new AuthenticationAction(uri);
        }
        catch
        {
            return null;
        }
    }

    static AppLinkAction? ProcessHttpsScheme(Uri uri)
    {
        try
        {
            return uri.Segments[1] switch
            {
                "app/" => new LaunchDAppAction(uri),
                "news/" => new ViewNewsAction(uri),
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        Page page;
        if (walletProvider.GetWallet() is null)
        {
            page = serviceProvider.GetServiceOrCreateInstance<WelcomePage>();
            page = new NavigationPage(page);
        }
        else
        {
            page = appLinkAction?.GetPage(serviceProvider)
                ?? serviceProvider.GetServiceOrCreateInstance<AppShell>();
        }
        return new Window(page)
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
    }
}
