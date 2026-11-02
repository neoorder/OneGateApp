#if WINDOWS
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Microsoft.Windows.PushNotifications;
using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;

namespace NeoOrder.OneGate.Services.Notifications;

/// <summary>Also runs during raw push activation, before MAUI or a wallet window exists.</summary>
internal static class WindowsPushNotifications
{
    static readonly Lazy<Guid?> remoteId = new(ReadConfiguration);
    internal static Guid? RemoteId => remoteId.Value;
    internal static bool IsConfigured => RemoteId is not null;
    internal static Exception? RegistrationError { get; private set; }
    static bool initialized;

    internal static void Initialize(Action<IReadOnlyDictionary<string, string>> activated)
    {
        if (initialized) return;
        initialized = true;
        try
        {
            AppNotificationManager.Default.NotificationInvoked += (_, args) => activated(new Dictionary<string, string>(args.Arguments));
            AppNotificationManager.Default.Register();
            if (!IsConfigured || !PushNotificationManager.IsSupported()) return;
            PushNotificationManager.Default.PushReceived += (_, args) => Show(args.Payload);
            PushNotificationManager.Default.Register();
        }
        catch (Exception ex)
        {
            RegistrationError = ex;
            Debug.WriteLine($"[Push] Windows activation registration failed: {ex.GetType().Name}.");
        }
    }

    static Guid? ReadConfiguration()
    {
        try
        {
            var assembly = typeof(WindowsPushNotifications).Assembly;
            using var stream = assembly.GetManifestResourceStream("NeoOrder.OneGate.Resources.Raw.wns.json");
            if (stream is null) return null;
            using var json = JsonDocument.Parse(stream);
            if (!Guid.TryParse(json.RootElement.GetProperty("clientId").GetString(), out var clientId) || clientId == Guid.Empty
                || !Guid.TryParse(json.RootElement.GetProperty("servicePrincipalObjectId").GetString(), out var objectId) || objectId == Guid.Empty)
                return null;
            // The installed MSIX must contain the matching COM activator, not just the JSON settings.
            var manifest = XDocument.Load(Path.Combine(global::Windows.ApplicationModel.Package.Current.InstalledLocation.Path, "AppxManifest.xml"));
            XNamespace com = "http://schemas.microsoft.com/appx/manifest/com/windows10";
            bool registered = manifest.Descendants(com + "ExeServer")
                .Where(e => (string?)e.Attribute("Arguments") == "----WindowsAppRuntimePushServer:")
                .Elements(com + "Class").Any(e => Guid.TryParse((string?)e.Attribute("Id"), out var id) && id == clientId);
            return registered ? objectId : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
            or System.Xml.XmlException or InvalidOperationException or KeyNotFoundException or System.Runtime.InteropServices.COMException)
        {
            Debug.WriteLine($"[Push] Windows configuration unavailable: {ex.GetType().Name}.");
            return null;
        }
    }

    internal static void Show(byte[] payload)
    {
        if (PushRegistrationService.IsAppForeground || payload.Length > 4096) return;
        try
        {
            using var json = JsonDocument.Parse(payload);
            var root = json.RootElement;
            if (root.GetProperty("version").GetInt32() != 1) return;
            string title = root.GetProperty("title").GetString() ?? "OneGate";
            string body = root.GetProperty("body").GetString() ?? "";
            var values = new Dictionary<string, string>();
            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
                foreach (string key in PushNavigationService.Keys)
                    if (data.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
                        values[key] = value.GetString()!;
            Show(title, body, values);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException
            or ArgumentException or FormatException or System.Runtime.InteropServices.COMException)
        {
            Debug.WriteLine($"[Push] Windows notification rejected: {ex.GetType().Name}.");
        }
    }

    internal static void Show(string title, string body, IReadOnlyDictionary<string, string> data)
    {
        if (PushRegistrationService.IsAppForeground || !NotificationPreferences.Allows(data)
            || AppNotificationManager.Default.Setting != AppNotificationSetting.Enabled) return;
        var builder = new AppNotificationBuilder().AddText(title).AddText(body);
        foreach (var pair in data) builder.AddArgument(pair.Key, pair.Value);
        string? identifier = PushNotificationHistory.GetIdentifier(data);
        if (!PushNotificationHistory.Shared.TryClaim(identifier)) return;
        try
        {
            var notification = builder.BuildNotification();
            notification.Tag = (identifier ?? Guid.NewGuid().ToString("N"))[..16];
            notification.Group = "messages";
            notification.Expiration = DateTimeOffset.UtcNow.AddHours(1);
            AppNotificationManager.Default.Show(notification);
        }
        catch
        {
            PushNotificationHistory.Shared.Forget(identifier);
            throw;
        }
    }
}
#endif
