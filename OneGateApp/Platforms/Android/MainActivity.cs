using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Content.Res;
using Android.OS;
using AndroidX.Core.View;
using Neo.Wallets;
using NeoOrder.OneGate.Services;
using System.Diagnostics;

namespace NeoOrder.OneGate.Platforms.Android;

[Activity(Theme = "@style/OneGate.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.Multiple, DocumentLaunchMode = DocumentLaunchMode.IntoExisting, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable], DataScheme = "neo")]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable], DataScheme = "neoauth", DataHost = "wallet", DataPath = "/authenticate")]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable], DataScheme = "neoauth", DataHost = "onegate.space", DataPath = "/authenticate")]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable], DataScheme = "https", DataHost = SharedOptions.OneGateDomain, DataPathPrefix = "/app/", AutoVerify = true)]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable], DataScheme = "https", DataHost = SharedOptions.OneGateDomain, DataPathPrefix = "/news/", AutoVerify = true)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        IServiceProvider serviceProvider = IPlatformApplication.Current!.Services;
        if (Intent?.Action == Intent.ActionMain)
        {
            UpdateService updateService = serviceProvider.GetRequiredService<UpdateService>();
            updateService.OnMainActivityCreated(this);
        }
        else if (Intent?.Action == Intent.ActionView)
        {
            IWalletProvider walletProvider = serviceProvider.GetRequiredService<IWalletProvider>();
            Wallet? wallet = walletProvider.GetWallet();
            if (wallet is null)
            {
                var intent = new Intent(this, typeof(MainActivity));
                intent.SetAction(Intent.ActionMain);
                intent.AddCategory(Intent.CategoryLauncher);
                intent.AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop | ActivityFlags.ClearTop);
                StartActivity(intent);
                Finish();
            }
            else
            {
                if (!HandleUri(Intent)) Finish();
            }
        }
        base.OnCreate(savedInstanceState);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        if (intent?.Action == Intent.ActionView)
            HandleUri(intent);
        base.OnNewIntent(intent);
    }

    protected override void OnResume()
    {
        base.OnResume();
        ApplySystemBarStyle();
    }

    public override void OnConfigurationChanged(Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        ApplySystemBarStyle();
    }

    static bool HandleUri(Intent intent)
    {
        if (Microsoft.Maui.Controls.Application.Current is not App app) return false;
        string url = intent.GetStringExtra("org.neoorder.onegate.ORIGINAL_URI")
            ?? intent.Data!.ToString()!;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        return app.ProcessAppLinkUri(uri);
    }

    void ApplySystemBarStyle()
    {
        bool isDarkTheme = (Resources?.Configuration?.UiMode & UiMode.NightMask) == UiMode.NightYes;
        var controller = WindowCompat.GetInsetsController(Window, Window!.DecorView);
        controller?.AppearanceLightStatusBars = !isDarkTheme;
        controller?.AppearanceLightNavigationBars = !isDarkTheme;
    }
}
