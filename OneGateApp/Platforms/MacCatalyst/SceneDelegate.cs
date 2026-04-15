using Foundation;
using UIKit;

namespace NeoOrder.OneGate.Platforms.MacCatalyst;

[Register(nameof(SceneDelegate))]
public class SceneDelegate : MauiUISceneDelegate
{
    public override void WillConnect(UIScene scene, UISceneSession session, UISceneConnectionOptions connectionOptions)
    {
        var activity = connectionOptions.UserActivities?
            .ToArray()
            .FirstOrDefault(p => p?.ActivityType == NSUserActivityType.BrowsingWeb);
        if (activity is not null)
            HandleAppLink(activity);
        else
            HandleAppLink(connectionOptions.UrlContexts);
        base.WillConnect(scene, session, connectionOptions);
    }

    public override bool ContinueUserActivity(UIScene scene, NSUserActivity userActivity)
    {
        if (HandleAppLink(userActivity)) return true;
        return base.ContinueUserActivity(scene, userActivity);
    }

    public override bool OpenUrl(UIScene scene, NSSet<UIOpenUrlContext> urlContexts)
    {
        if (HandleAppLink(urlContexts)) return true;
        return base.OpenUrl(scene, urlContexts);
    }

    static bool HandleAppLink(NSUserActivity activity)
    {
        if (activity.ActivityType != NSUserActivityType.BrowsingWeb || activity.WebPageUrl is null) return false;
        return HandleAppLink(activity.WebPageUrl);
    }

    static bool HandleAppLink(NSSet<UIOpenUrlContext>? urlContexts)
    {
        if (urlContexts is null || urlContexts.Count == 0) return false;
        return urlContexts.AnyObject?.Url is NSUrl url && HandleAppLink(url);
    }

    static bool HandleAppLink(NSUrl? url)
    {
        if (url?.AbsoluteString is null) return false;
        if (Application.Current is not App app) return false;
        if (!Uri.TryCreate(url.AbsoluteString, UriKind.Absolute, out var uri)) return false;
        return app.ProcessAppLinkUri(uri);
    }
}
