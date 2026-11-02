#if WINDOWS || MACCATALYST
using System.Text;
#if MACCATALYST
using Foundation;
using UserNotifications;
#endif

namespace NeoOrder.OneGate.Services.Notifications;

internal static class DesktopNotificationService
{
    internal static async Task ShowAsync(string title, string body, IReadOnlyDictionary<string, string> data, Func<bool> isCurrent)
    {
        // Native callbacks, session changes and UI activation share this final presentation check.
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (PushRegistrationService.IsAppForeground || !isCurrent() || !NotificationPreferences.Allows(data)) return;
#if WINDOWS
            WindowsPushNotifications.Show(Truncate(title, 200), Truncate(body, 1000), data);
            await Task.CompletedTask;
#else
            var center = UNUserNotificationCenter.Current;
            var settings = await center.GetNotificationSettingsAsync();
            if (settings.AuthorizationStatus is not (UNAuthorizationStatus.Authorized or UNAuthorizationStatus.Provisional)
                || PushRegistrationService.IsAppForeground || !isCurrent() || !NotificationPreferences.Allows(data)) return;
            string? identifier = PushNotificationHistory.GetIdentifier(data);
            // APNs may have been displayed by the OS while this process was in the background.
            foreach (var delivered in await center.GetDeliveredNotificationsAsync())
                if (identifier is not null && identifier == PushNotificationHistory.GetIdentifier(PushNotificationDelegate.ReadData(delivered.Request.Content.UserInfo)))
                {
                    PushNotificationHistory.Shared.TryClaim(identifier);
                    return;
                }
            if (PushRegistrationService.IsAppForeground || !isCurrent() || !NotificationPreferences.Allows(data)
                || !PushNotificationHistory.Shared.TryClaim(identifier)) return;
            try
            {
                using var values = new NSMutableDictionary();
                foreach (var pair in data) values[new NSString(pair.Key)] = new NSString(pair.Value);
                using var userInfo = new NSMutableDictionary();
                userInfo[new NSString("onegate")] = values;
                using var content = new UNMutableNotificationContent
                {
                    Title = Truncate(title, 200), Body = Truncate(body, 1000),
                    Sound = UNNotificationSound.Default, UserInfo = userInfo
                };
                // Prefix distinguishes our already-claimed local request in WillPresentNotification.
                using var request = UNNotificationRequest.FromIdentifier("onegate.local." + identifier, content, null);
                await center.AddNotificationRequestAsync(request);
            }
            catch
            {
                PushNotificationHistory.Shared.Forget(identifier);
                throw;
            }
#endif
        });
    }

    static string Truncate(string value, int maxBytes)
    {
        if (Encoding.UTF8.GetByteCount(value) <= maxBytes) return value;
        var result = new StringBuilder();
        int bytes = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (bytes + rune.Utf8SequenceLength > maxBytes - 3) break;
            result.Append(rune); bytes += rune.Utf8SequenceLength;
        }
        return result.Append('…').ToString();
    }
}
#endif
