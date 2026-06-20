using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using NeoOrder.OneGate.Models.AppLinks;
using NeoOrder.OneGate.Services;

namespace NeoOrder.OneGate.Platforms.Android;

[Activity(Theme = "@style/OneGate.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTask, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[IntentFilter([Intent.ActionView], Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable], DataScheme = "neo")]
public class MainActivity : OneGateActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        NotifyMainActivityCreated();
        HandlePaymentIntent(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        HandlePaymentIntent(intent);
    }

    void NotifyMainActivityCreated()
    {
        IServiceProvider serviceProvider = IPlatformApplication.Current!.Services;
        UpdateService updateService = serviceProvider.GetRequiredService<UpdateService>();
        updateService.OnMainActivityCreated(this);
    }

    static void HandlePaymentIntent(Intent? intent)
    {
        if (intent?.Action != Intent.ActionView) return;
        if (!TryGetUri(intent, out Uri? uri)) return;
        if (PaymentAction.TryCreate(uri) is not PaymentAction action) return;

        _ = MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await action.GotoRoute(Shell.Current);
        });
    }
}
