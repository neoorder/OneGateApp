#if IOS || MACCATALYST
using Foundation;
using UserNotifications;

namespace NeoOrder.OneGate.Services.Notifications;

internal sealed class PushNotificationDelegate : UNUserNotificationCenterDelegate
{
    public override void WillPresentNotification(UNUserNotificationCenter center, UNNotification notification,
        Action<UNNotificationPresentationOptions> completionHandler)
    {
#if MACCATALYST
        var data = ReadData(notification.Request.Content.UserInfo);
        bool show = !PushRegistrationService.IsAppForeground && NotificationPreferences.Allows(data);
        if (show && !notification.Request.Identifier.StartsWith("onegate.local.", StringComparison.Ordinal))
            show = PushNotificationHistory.Shared.TryClaim(PushNotificationHistory.GetIdentifier(data));
        completionHandler(show ? UNNotificationPresentationOptions.Banner | UNNotificationPresentationOptions.List
            | UNNotificationPresentationOptions.Sound : UNNotificationPresentationOptions.None);
#else
        completionHandler(UNNotificationPresentationOptions.None);
#endif
    }

    public override void DidReceiveNotificationResponse(UNUserNotificationCenter center, UNNotificationResponse response,
        Action completionHandler)
    {
        try
        {
            if (!response.IsDefaultAction) return;
            var values = ReadData(response.Notification.Request.Content.UserInfo);
            IPlatformApplication.Current!.Services.GetRequiredService<PushNavigationService>().Receive(values);
        }
        finally { completionHandler(); }
    }

    internal static Dictionary<string, string> ReadData(NSDictionary userInfo)
    {
        var values = new Dictionary<string, string>();
        if (userInfo["onegate"] is NSDictionary data)
            foreach (var key in PushNavigationService.Keys)
                if (data[key] is NSString value) values[key] = value.ToString();
        return values;
    }
}

#endif
