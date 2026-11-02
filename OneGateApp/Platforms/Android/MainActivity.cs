using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using NeoOrder.OneGate.Models.AppLinks;
using NeoOrder.OneGate.Services;
using Notifications = NeoOrder.OneGate.Services.Notifications;

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
        HandlePushIntent(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        HandlePaymentIntent(intent);
        HandlePushIntent(intent);
    }

    static void HandlePushIntent(Intent? intent)
    {
        if (intent?.GetStringExtra("og_v") is null) return;
        var values = new Dictionary<string, string>();
        foreach (var key in Notifications.PushNavigationService.Keys)
            if (intent.GetStringExtra(key) is { } value) values[key] = value;
        IPlatformApplication.Current!.Services.GetRequiredService<Notifications.PushNavigationService>().Receive(values);
        // Activity recreation must not replay the same click.
        foreach (var key in Notifications.PushNavigationService.Keys) intent.RemoveExtra(key);
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
