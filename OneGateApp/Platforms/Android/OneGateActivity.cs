using Android.Content;
using Android.Content.Res;
using AndroidX.Core.View;
using Neo.Wallets;

namespace NeoOrder.OneGate.Platforms.Android;

public abstract class OneGateActivity : MauiAppCompatActivity
{
    protected static bool TryGetUri(Intent intent, out Uri uri)
    {
        string url = intent.GetStringExtra("org.neoorder.onegate.ORIGINAL_URI")
            ?? intent.Data?.ToString()
            ?? string.Empty;
        return Uri.TryCreate(url, UriKind.Absolute, out uri!);
    }

    protected bool HandleAppLinkIntent(Intent? intent)
    {
        if (intent?.Action != Intent.ActionView) return false;
        if (!TryGetUri(intent, out Uri? uri)) return false;
        if (!EnsureWalletOrOpenMain()) return false;
        return Microsoft.Maui.Controls.Application.Current is App app && app.ProcessAppLinkUri(uri);
    }

    bool EnsureWalletOrOpenMain()
    {
        IServiceProvider serviceProvider = IPlatformApplication.Current!.Services;
        IWalletProvider walletProvider = serviceProvider.GetRequiredService<IWalletProvider>();
        Wallet? wallet = walletProvider.GetWallet();
        if (wallet is not null) return true;

        var mainIntent = new Intent(this, typeof(MainActivity));
        mainIntent.SetAction(Intent.ActionMain);
        mainIntent.AddCategory(Intent.CategoryLauncher);
        mainIntent.AddFlags(ActivityFlags.NewTask | ActivityFlags.SingleTop | ActivityFlags.ClearTop);
        StartActivity(mainIntent);
        return false;
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

    void ApplySystemBarStyle()
    {
        bool isDarkTheme = (Resources?.Configuration?.UiMode & UiMode.NightMask) == UiMode.NightYes;
        var controller = WindowCompat.GetInsetsController(Window, Window!.DecorView);
        controller?.AppearanceLightStatusBars = !isDarkTheme;
        controller?.AppearanceLightNavigationBars = !isDarkTheme;
    }
}
