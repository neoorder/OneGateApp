using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using NeoOrder.OneGate.Services;

namespace NeoOrder.OneGate.Platforms.Android;

[Activity(Theme = "@style/OneGate.SplashTheme", LaunchMode = LaunchMode.Multiple, DocumentLaunchMode = DocumentLaunchMode.IntoExisting, TaskAffinity = "org.neoorder.onegate.document", ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density, Exported = true)]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable], DataScheme = "https", DataHost = SharedOptions.OneGateDomain, DataPathPrefix = "/app/", AutoVerify = true)]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable], DataScheme = "https", DataHost = SharedOptions.OneGateDomain, DataPathPrefix = "/news/", AutoVerify = true)]
public class DocumentLinkActivity : OneGateActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        if (!HandleAppLinkIntent(Intent))
            Finish();
        base.OnCreate(savedInstanceState);
    }
}
