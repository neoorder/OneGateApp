#if WINDOWS
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.PushNotifications;

namespace NeoOrder.OneGate.Services.Notifications;

public sealed partial class PushRegistrationService
{
    private partial Task<NotificationPermissionStatus> CheckPlatformPermissionAsync(bool requestPermission) =>
        Task.FromResult(AppNotificationManager.Default.Setting == AppNotificationSetting.Enabled
            ? NotificationPermissionStatus.Allowed : NotificationPermissionStatus.Denied);

    private partial async Task OpenPlatformSettingsAsync()
    {
        if (!await Launcher.Default.TryOpenAsync(new Uri("ms-settings:notifications")))
            throw new InvalidOperationException("The system notification settings could not be opened.");
    }

    private partial async Task RefreshPlatformAsync(bool requestPermission)
    {
        if (AppNotificationManager.Default.Setting != AppNotificationSetting.Enabled)
        {
            SetState(PushRegistrationStatus.PermissionDenied);
            return;
        }
        if (WindowsPushNotifications.RemoteId is not { } remoteId)
        {
            SetState(PushRegistrationStatus.NotConfigured);
            return;
        }
        if (!PushNotificationManager.IsSupported())
        {
            SetState(PushRegistrationStatus.NotSupported);
            return;
        }
        if (WindowsPushNotifications.RegistrationError is { } error) throw new InvalidOperationException("Windows notification registration failed.", error);
        SetState(PushRegistrationStatus.Registering);
        // Request on each foreground session: channels expire and their addresses can change.
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var result = await PushNotificationManager.Default.CreateChannelAsync(remoteId).AsTask(cancellation.Token);
        if (result.Status != PushNotificationChannelStatus.CompletedSuccess)
            throw result.ExtendedError ?? new IOException("WNS channel creation failed.");
        if (AppNotificationManager.Default.Setting != AppNotificationSetting.Enabled)
        {
            SetState(PushRegistrationStatus.PermissionDenied);
            return;
        }
        if (result.Channel.ExpirationTime <= DateTimeOffset.UtcNow)
            throw new IOException("WNS returned an expired channel.");
        SetRegistration(new("wns", result.Channel.Uri.ToString(), global::Windows.ApplicationModel.Package.Current.Id.Name, null));
    }
}
#endif
