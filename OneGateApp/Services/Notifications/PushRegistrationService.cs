using System.Diagnostics;

namespace NeoOrder.OneGate.Services.Notifications;

/// <summary>Obtains OS push registration only. Does not register with a gateway or a relay.</summary>
public sealed partial class PushRegistrationService
{
#if WINDOWS || MACCATALYST
    internal static volatile bool IsAppForeground;
#endif
    Task? refreshTask;
    long tokenRevision;

    public PushRegistrationStatus Status { get; private set; }
    public PushRegistration? Registration { get; private set; }
    public Exception? Error { get; private set; }

    /// <summary>Registration or permission changed. Delivered on the UI thread.</summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Refreshes platform registration, coalescing concurrent calls. Permission is requested only
    /// when allowed by the caller and not previously declined. Failures are exposed through Status/Error.
    /// </summary>
    public Task RefreshAsync(bool requestPermission = false) => MainThread.InvokeOnMainThreadAsync(() =>
        refreshTask is { IsCompleted: false } ? refreshTask : refreshTask = RefreshCoreAsync(requestPermission));

    async Task RefreshCoreAsync(bool requestPermission)
    {
        // Publish the shared task before a synchronous platform callback can raise Changed.
        await Task.Yield();
        long revision = tokenRevision;
        try
        {
            await RefreshPlatformAsync(requestPermission);
        }
        catch (Exception ex)
        {
            // A platform token-change callback may have completed a newer registration meanwhile.
            if (revision == tokenRevision || Status == PushRegistrationStatus.Registering)
                SetState(PushRegistrationStatus.Failed, error: ex);
        }
    }

    /// <summary>Checks system permission independently of remote push configuration.</summary>
    public Task<NotificationPermissionStatus> CheckPermissionAsync(bool requestPermission = false) =>
        MainThread.InvokeOnMainThreadAsync(() => CheckPlatformPermissionAsync(requestPermission));

    public Task OpenNotificationSettingsAsync() => MainThread.InvokeOnMainThreadAsync(OpenPlatformSettingsAsync);

    private partial Task<NotificationPermissionStatus> CheckPlatformPermissionAsync(bool requestPermission);
    private partial Task OpenPlatformSettingsAsync();
    private partial Task RefreshPlatformAsync(bool requestPermission);

    void SetState(PushRegistrationStatus status, PushRegistration? registration = null, Exception? error = null)
    {
        if (Status == status && Registration == registration && ReferenceEquals(Error, error)) return;
        Status = status;
        Registration = registration;
        Error = error;
        Debug.WriteLine($"[Push] Registration status: {status}.");
        if (error is not null)
            Debug.WriteLine($"[Push] Registration error: {error.GetType().FullName}.");
        Changed?.Invoke(this, EventArgs.Empty);
    }

    void SetRegistration(PushRegistration registration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registration.RegistrationId);
        tokenRevision++;
        SetState(PushRegistrationStatus.Registered, registration);
    }
}
