using Foundation;
using UIKit;
using UserNotifications;
using NeoOrder.OneGate.Services.Notifications;

namespace NeoOrder.OneGate.Platforms.MacCatalyst;

[Register(nameof(AppDelegate))]
public class AppDelegate : MauiUIApplicationDelegate
{
    readonly PushNotificationDelegate pushDelegate = new();

    [Export("application:didRegisterForRemoteNotificationsWithDeviceToken:")]
    public void RegisteredForRemoteNotifications(UIApplication application, NSData deviceToken) =>
        IPlatformApplication.Current!.Services.GetRequiredService<PushRegistrationService>().OnApnsRegistered(deviceToken);

    [Export("application:didFailToRegisterForRemoteNotificationsWithError:")]
    public void FailedToRegisterForRemoteNotifications(UIApplication application, NSError error) =>
        IPlatformApplication.Current!.Services.GetRequiredService<PushRegistrationService>().OnApnsRegistrationFailed(error);

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        UNUserNotificationCenter.Current.Delegate = pushDelegate;
        if (application.UserActivity is not null)
            HandleAppLink(application.UserActivity);
        return base.FinishedLaunching(application, launchOptions);
    }

    public override bool ContinueUserActivity(UIApplication application, NSUserActivity userActivity, UIApplicationRestorationHandler completionHandler)
    {
        HandleAppLink(userActivity);
        return base.ContinueUserActivity(application, userActivity, completionHandler);
    }

    public override bool OpenUrl(UIApplication application, NSUrl url, NSDictionary options)
    {
        if (HandleAppLink(url)) return true;
        return base.OpenUrl(application, url, options);
    }

    static bool HandleAppLink(NSUserActivity activity)
    {
        if (activity.ActivityType != NSUserActivityType.BrowsingWeb || activity.WebPageUrl is null) return false;
        return HandleAppLink(activity.WebPageUrl);
    }

    static bool HandleAppLink(NSUrl url)
    {
        if (Microsoft.Maui.Controls.Application.Current is not App app) return false;
        if (!Uri.TryCreate(url.AbsoluteString, UriKind.Absolute, out var uri)) return false;
        return app.ProcessAppLinkUri(uri);
    }
}
