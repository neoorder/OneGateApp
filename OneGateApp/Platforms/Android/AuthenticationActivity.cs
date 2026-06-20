using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using NeoOrder.OneGate.Services;

namespace NeoOrder.OneGate.Platforms.Android;

[Activity(Theme = "@style/OneGate.SplashTheme", LaunchMode = LaunchMode.Multiple, NoHistory = true, ExcludeFromRecents = true, TaskAffinity = "org.neoorder.onegate.authentication", ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density, Exported = true)]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable], DataScheme = "neoauth", DataHost = "wallet", DataPath = "/authenticate")]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable], DataScheme = "neoauth", DataHost = SharedOptions.OneGateDomain, DataPath = "/authenticate")]
public class AuthenticationActivity : OneGateActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        if (!HandleAppLinkIntent(Intent))
            Finish();
        base.OnCreate(savedInstanceState);
    }
}
