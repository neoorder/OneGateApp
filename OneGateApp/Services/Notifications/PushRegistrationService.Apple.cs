#if IOS || MACCATALYST
using Foundation;
using UIKit;
using UserNotifications;

namespace NeoOrder.OneGate.Services.Notifications;

public sealed partial class PushRegistrationService
{
    TaskCompletionSource<(long Revision, string Token)>? apnsRegistration;
    long apnsCallbackRevision;
    static readonly Lazy<string?> apnsEnvironment = new(() => ApnsEnvironmentReader.Read(
#if MACCATALYST
        Path.Combine(NSBundle.MainBundle.BundlePath, "Contents", "embedded.provisionprofile"), "com.apple.developer.aps-environment"));
#else
        Path.Combine(NSBundle.MainBundle.BundlePath, "embedded.mobileprovision"), "aps-environment"));
#endif
    static string? ApnsEnvironment => apnsEnvironment.Value;

    private partial async Task RefreshPlatformAsync(bool requestPermission)
    {
        var permission = await CheckPlatformPermissionAsync(requestPermission);
        if (permission != NotificationPermissionStatus.Allowed)
        {
            SetState(permission == NotificationPermissionStatus.NotDetermined
                ? PushRegistrationStatus.PermissionRequired : PushRegistrationStatus.PermissionDenied);
            return;
        }
        if (ApnsEnvironment is not ("development" or "production"))
        {
            SetState(PushRegistrationStatus.NotConfigured);
            return;
        }

        SetState(PushRegistrationStatus.Registering);
        var completion = new TaskCompletionSource<(long Revision, string Token)>(TaskCreationOptions.RunContinuationsAsynchronously);
        apnsRegistration = completion;
        try
        {
            // Always obtain a fresh token from APNs; never restore a cached token from app storage.
            UIApplication.SharedApplication.RegisterForRemoteNotifications();
            var result = await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
            await AcceptApnsTokenAsync(result.Token, result.Revision);
        }
        finally
        {
            if (ReferenceEquals(apnsRegistration, completion)) apnsRegistration = null;
        }
    }

    private partial async Task<NotificationPermissionStatus> CheckPlatformPermissionAsync(bool requestPermission)
    {
        var center = UNUserNotificationCenter.Current;
        var settings = await center.GetNotificationSettingsAsync();
        if (settings.AuthorizationStatus == UNAuthorizationStatus.NotDetermined && requestPermission)
        {
            var result = await center.RequestAuthorizationAsync(UNAuthorizationOptions.Alert
                | UNAuthorizationOptions.Badge | UNAuthorizationOptions.Sound);
            if (result.Item2 is { } error) throw new NSErrorException(error);
            settings = await center.GetNotificationSettingsAsync();
        }
        return HasPermission(settings) ? NotificationPermissionStatus.Allowed
            : settings.AuthorizationStatus == UNAuthorizationStatus.NotDetermined
                ? NotificationPermissionStatus.NotDetermined : NotificationPermissionStatus.Denied;
    }

    private partial async Task OpenPlatformSettingsAsync()
    {
        using var url = new NSUrl(UIApplication.OpenNotificationSettingsUrl);
        if (!await UIApplication.SharedApplication.OpenUrlAsync(url, new UIApplicationOpenUrlOptions()))
            throw new InvalidOperationException("The system notification settings could not be opened.");
    }

    internal void OnApnsRegistered(NSData deviceToken)
    {
        string token = Convert.ToHexString(deviceToken.ToArray()).ToLowerInvariant();
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            long revision = ++apnsCallbackRevision;
            if (apnsRegistration?.TrySetResult((revision, token)) == true) return;
            // APNs may respond after our timeout, or rotate the token independently.
            try { await AcceptApnsTokenAsync(token, revision); }
            catch (Exception ex)
            {
                if (revision == apnsCallbackRevision) SetState(PushRegistrationStatus.Failed, error: ex);
            }
        });
    }

    internal void OnApnsRegistrationFailed(NSError error) => MainThread.BeginInvokeOnMainThread(() =>
    {
        apnsCallbackRevision++;
        var exception = new NSErrorException(error);
        if (apnsRegistration?.TrySetException(exception) != true)
            SetState(PushRegistrationStatus.Failed, error: exception);
    });

    async Task AcceptApnsTokenAsync(string token, long revision)
    {
        if (revision != apnsCallbackRevision) return;
        var settings = await UNUserNotificationCenter.Current.GetNotificationSettingsAsync();
        if (revision != apnsCallbackRevision) return;
        if (!HasPermission(settings))
        {
            SetState(PushRegistrationStatus.PermissionDenied);
            return;
        }
        if (ApnsEnvironment is not ("development" or "production"))
        {
            SetState(PushRegistrationStatus.NotConfigured);
            return;
        }
        SetRegistration(new("apns", token, AppInfo.PackageName, ApnsEnvironment));
    }

    static bool HasPermission(UNNotificationSettings settings) => settings.AuthorizationStatus is
        UNAuthorizationStatus.Authorized or UNAuthorizationStatus.Provisional or UNAuthorizationStatus.Ephemeral;
}
#endif
