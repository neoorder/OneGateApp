using Foundation;
using UIKit;

namespace NeoOrder.OneGate.Platforms.iOS;

[Register(nameof(AppDelegate))]
public class AppDelegate : MauiUIApplicationDelegate
{
    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
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

    static bool HandleAppLink(NSUrl? url)
    {
        if (url?.AbsoluteString is null) return false;
        if (Microsoft.Maui.Controls.Application.Current is not App app) return false;
        if (!Uri.TryCreate(url.AbsoluteString, UriKind.Absolute, out var uri)) return false;
        return app.ProcessAppLinkUri(uri);
    }
}
