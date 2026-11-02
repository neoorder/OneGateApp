using NeoOrder.OneGate.Services.Notifications;

namespace NeoOrder.OneGate;

public partial class App
{
    bool pushRegistrationAttempted;

    void OnMessagingChangedForPush(object? sender, EventArgs e)
    {
        UpdatePushRegistration();
        pushNavigationService.TryOpen();
    }

    void OnPlatformPushChanged(object? sender, EventArgs e) => pushGatewayService.RequestSync();

    void OnNotificationPreferencesChanged(object? sender, EventArgs e) => MainThread.BeginInvokeOnMainThread(() =>
    {
        pushRegistrationAttempted = false;
        pushGatewayService.RequestSync(force: true);
        UpdatePushRegistration();
    });

    void OnPushConnectivityChanged(object? sender, ConnectivityChangedEventArgs e)
    {
        if (e.NetworkAccess != NetworkAccess.Internet) return;
        MainThread.BeginInvokeOnMainThread(() =>
        {
            pushRegistrationAttempted = false;
            pushGatewayService.RequestSync(force: true);
            UpdatePushRegistration();
        });
    }

    void UpdatePushRegistration()
    {
        bool foreground = visibleMainWindows.Any(window => window.Page is AppShell);
        if (!foreground)
        {
            pushRegistrationAttempted = false;
            return;
        }
        pushGatewayService.RequestSync();
        if (!messagingService.IsEnabled || !NotificationPreferences.Enabled || !messagingService.ChatNotificationsEnabled
            || !messagingService.HasPushGateway)
        {
            pushRegistrationAttempted = false;
            return;
        }
        if (pushRegistrationAttempted) return;
        pushRegistrationAttempted = true;
        // Platform registration may display a permission dialog or wait for a network callback.
        // Neither operation belongs on the chat startup's critical path.
        _ = pushRegistrationService.RefreshAsync(requestPermission: true);
    }
}
