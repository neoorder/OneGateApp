namespace NeoOrder.OneGate.Services.Notifications;

public enum NotificationPermissionStatus
{
    Unknown,
    NotDetermined,
    Allowed,
    Denied
}

public enum PushRegistrationStatus
{
    NotStarted,
    NotSupported,
    NotConfigured,
    ServiceUnavailable,
    PermissionRequired,
    PermissionDenied,
    Registering,
    Registered,
    Failed
}

/// <summary>A platform-issued address for this app installation, not a Meshline device identity.</summary>
public sealed record PushRegistration(string Provider, string RegistrationId, string ApplicationId, string? Environment)
{
    // Registration tokens must not accidentally appear in logs or diagnostics.
    public override string ToString() => $"{Provider}: {ApplicationId} ({Environment ?? "default"})";
}
