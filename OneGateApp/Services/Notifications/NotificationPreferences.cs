namespace NeoOrder.OneGate.Services.Notifications;

/// <summary>The notification master switch applies to this installation, including headless Windows activation.</summary>
internal static class NotificationPreferences
{
    const string EnabledKey = "onegate.notifications.enabled";
    public static event EventHandler? Changed;
    public static bool Enabled => Preferences.Default.Get(EnabledKey, true);

    // DeviceId includes the account identity; no account field is needed in registration requests.
    public static bool IsChatEnabled(string? deviceId) => !string.IsNullOrEmpty(deviceId)
        && Preferences.Default.Get(ChatKey(deviceId), true);

    public static void SetChatEnabled(string deviceId, bool enabled)
    {
        ArgumentException.ThrowIfNullOrEmpty(deviceId);
        if (IsChatEnabled(deviceId) == enabled) return;
        Preferences.Default.Set(ChatKey(deviceId), enabled);
        Changed?.Invoke(null, EventArgs.Empty);
    }

    internal static bool Allows(IReadOnlyDictionary<string, string> data) => Enabled
        && (!data.TryGetValue("og_device", out var deviceId) || IsChatEnabled(deviceId));

    static string ChatKey(string deviceId) => "onegate.notifications.chat." + deviceId;

    public static void SetEnabled(bool enabled)
    {
        if (Enabled == enabled) return;
        Preferences.Default.Set(EnabledKey, enabled);
        Changed?.Invoke(null, EventArgs.Empty);
    }
}
