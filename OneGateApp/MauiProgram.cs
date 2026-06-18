using CommunityToolkit.Maui;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Neo;
using Neo.Wallets;
using NeoOrder.OneGate.Controls.Handlers;
using NeoOrder.OneGate.Controls.Views;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Resources;
using NeoOrder.OneGate.Services;
using NeoOrder.OneGate.Services.RPC;
using Plugin.Maui.ScreenSecurity;
using ZXing.Net.Maui.Controls;

[assembly: XmlnsDefinition("http://schemas.neoorder.org/onegate/controls", "NeoOrder.OneGate.Controls.Behaviors")]
[assembly: XmlnsDefinition("http://schemas.neoorder.org/onegate/controls", "NeoOrder.OneGate.Controls.Converters")]
[assembly: XmlnsDefinition("http://schemas.neoorder.org/onegate/controls", "NeoOrder.OneGate.Controls.Handlers")]
[assembly: XmlnsDefinition("http://schemas.neoorder.org/onegate/controls", "NeoOrder.OneGate.Controls.Popups")]
[assembly: XmlnsDefinition("http://schemas.neoorder.org/onegate/controls", "NeoOrder.OneGate.Controls.Views")]
[assembly: XmlnsDefinition("http://schemas.neoorder.org/onegate/controls", "NeoOrder.OneGate.Controls.Views.Validation")]
[assembly: XmlnsDefinition("http://schemas.neoorder.org/onegate/controls", "NeoOrder.OneGate.Data")]
[assembly: XmlnsDefinition("http://schemas.neoorder.org/onegate/controls", "NeoOrder.OneGate.Models")]
[assembly: XmlnsDefinition("http://schemas.neoorder.org/onegate/controls", "NeoOrder.OneGate.Models.Intents")]
[assembly: XmlnsDefinition("http://schemas.neoorder.org/onegate/controls", "NeoOrder.OneGate.Pages")]
[assembly: XmlnsDefinition("http://schemas.neoorder.org/onegate/controls", "NeoOrder.OneGate.Properties")]

namespace NeoOrder.OneGate;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder()
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit(ConfigureMauiCommunityToolkit)
            .UseScreenSecurity()
            .UseBarcodeReader()
            .RegisterServices()
            .ConfigureMauiHandlers(handlers =>
            {
                Controls.Handlers.Border.ConfigureHandlers();
                handlers.AddHandler<BridgeWebView, BridgeWebViewHandler>();
            })
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                fonts.AddFont("iconfont.ttf", "Icons");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    static void ConfigureMauiCommunityToolkit(Options options)
    {
        if (DeviceInfo.Idiom != DeviceIdiom.Desktop && DeviceInfo.Idiom != DeviceIdiom.Tablet && DeviceInfo.Idiom != DeviceIdiom.TV)
        {
            options.SetPopupDefaults(new DefaultPopupSettings
            {
                Margin = new(0),
                HorizontalOptions = LayoutOptions.Fill,
#if !IOS
                VerticalOptions = LayoutOptions.End
#endif
            });
            options.SetPopupOptionsDefaults(new DefaultPopupOptionsSettings
            {
                Shape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
                {
#if IOS
                    CornerRadius = new(10)
#else
                    CornerRadius = new(10, 10, 0, 0)
#endif
                }
            });
        }
        options.SetShouldEnableSnackbarOnWindows(true);
    }

    static MauiAppBuilder RegisterServices(this MauiAppBuilder builder)
    {
        builder.Services.AddSingleton(new HttpClient
        {
            BaseAddress = new($"https://{SharedOptions.OneGateDomain}/")
        });
        builder.Services.AddDbContext<ApplicationDbContext>(options =>
        {
            options.UseSqlite($"Filename={SharedOptions.DbPath}");
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        });
        builder.Services.AddSingleton(sp =>
        {
            using var stream = EmbeddedResource.Open("protocol.json");
            return ProtocolSettings.Load(stream);
        });
        builder.Services.AddSingleton<IWalletProvider, WalletProvider>();
        builder.Services.AddTransient<WalletAuthorizationService>();
        builder.Services.AddSingleton<TokenManager>();
        builder.Services.AddSingleton<RpcClient>();
        builder.Services.AddSingleton<UpdateService>();
        builder.Services.AddSingleton<IHomeShortcutService, HomeShortcutService>();
        return builder;
    }
}
