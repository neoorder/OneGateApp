#if ANDROID
using Android.App;
using Android.Gms.Common;
using Android.Gms.Extensions;
using Android.OS;
using AndroidX.Core.App;
using Firebase;
using Firebase.Messaging;

namespace NeoOrder.OneGate.Services.Notifications;

public sealed partial class PushRegistrationService
{
    const string PermissionRequestedKey = "onegate.push.permission-requested";
    internal const string NotificationChannelId = "onegate_messages";
    TaskCompletionSource? firebaseRegistration;

    private partial async Task RefreshPlatformAsync(bool requestPermission)
    {
        var permission = await CheckPlatformPermissionAsync(requestPermission);
        if (permission != NotificationPermissionStatus.Allowed)
        {
            SetState(permission == NotificationPermissionStatus.NotDetermined
                ? PushRegistrationStatus.PermissionRequired : PushRegistrationStatus.PermissionDenied);
            return;
        }
        var context = global::Android.App.Application.Context;
        var app = FirebaseApp.InitializeApp(context);
        if (app is null)
        {
            SetState(PushRegistrationStatus.NotConfigured);
            return;
        }
        if (GoogleApiAvailability.Instance.IsGooglePlayServicesAvailable(context) != ConnectionResult.Success)
        {
            SetState(PushRegistrationStatus.ServiceUnavailable);
            return;
        }

        CreateNotificationChannel();

        SetState(PushRegistrationStatus.Registering);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        firebaseRegistration = completion;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            // Register completes through OnRegistered even when this installation is already registered.
            // The manifest disables automatic registration until notification permission is granted.
            // Enabling auto-init reads the cached FID synchronously and cannot run on the UI thread.
            await Task.Run(() =>
            {
                var messaging = FirebaseMessaging.Instance;
                messaging.AutoInitEnabled = true;
                return messaging.Register().AsAsync();
            }, deadline.Token).WaitAsync(deadline.Token);
            await completion.Task.WaitAsync(deadline.Token);
        }
        finally
        {
            if (ReferenceEquals(firebaseRegistration, completion)) firebaseRegistration = null;
        }
    }

    private partial async Task<NotificationPermissionStatus> CheckPlatformPermissionAsync(bool requestPermission)
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
            var permission = await Permissions.CheckStatusAsync<Permissions.PostNotifications>();
            if (permission != PermissionStatus.Granted && requestPermission
                && !Preferences.Default.Get(PermissionRequestedKey, false))
            {
                // Do not repeat a declined or dismissed prompt on every foreground session.
                CreateNotificationChannel();
                Preferences.Default.Set(PermissionRequestedKey, true);
                permission = await Permissions.RequestAsync<Permissions.PostNotifications>();
            }
            if (permission != PermissionStatus.Granted)
                return Preferences.Default.Get(PermissionRequestedKey, false)
                    ? NotificationPermissionStatus.Denied : NotificationPermissionStatus.NotDetermined;
        }
        return AreNotificationsEnabled() ? NotificationPermissionStatus.Allowed : NotificationPermissionStatus.Denied;
    }

    private partial Task OpenPlatformSettingsAsync()
    {
        var context = global::Android.App.Application.Context;
        using var intent = new global::Android.Content.Intent(global::Android.Provider.Settings.ActionAppNotificationSettings);
        intent.PutExtra(global::Android.Provider.Settings.ExtraAppPackage, context.PackageName);
        intent.AddFlags(global::Android.Content.ActivityFlags.NewTask);
        try { context.StartActivity(intent); }
        catch (global::Android.Content.ActivityNotFoundException) { AppInfo.ShowSettingsUI(); }
        return Task.CompletedTask;
    }

    internal void OnFirebaseRegistered(string installationId) => MainThread.BeginInvokeOnMainThread(() =>
    {
        // Refreshes can arrive while no page is open, or after permission was revoked.
        if (string.IsNullOrWhiteSpace(installationId)) return;
        tokenRevision++;
        if (AreNotificationsEnabled())
            SetRegistration(new("fcm", installationId, AppInfo.PackageName, null));
        else
            SetState(PushRegistrationStatus.PermissionDenied);
        firebaseRegistration?.TrySetResult();
    });

    static bool AreNotificationsEnabled()
    {
        var context = global::Android.App.Application.Context;
        using var compat = NotificationManagerCompat.From(context);
        if (compat?.AreNotificationsEnabled() != true) return false;
        var manager = (NotificationManager?)context.GetSystemService(global::Android.Content.Context.NotificationService);
        return manager?.GetNotificationChannel(NotificationChannelId)?.Importance != NotificationImportance.None;
    }

    static void CreateNotificationChannel()
    {
        var context = global::Android.App.Application.Context;
        var manager = (NotificationManager?)context.GetSystemService(global::Android.Content.Context.NotificationService);
        using var channel = new NotificationChannel(NotificationChannelId, AppInfo.Name, NotificationImportance.High);
        manager?.CreateNotificationChannel(channel);
    }
}
#endif
