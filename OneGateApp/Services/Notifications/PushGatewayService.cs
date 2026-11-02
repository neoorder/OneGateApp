using NeoOrder.OneGate.Services.Messaging;
using System.Diagnostics;
using System.Text.Json;

namespace NeoOrder.OneGate.Services.Notifications;

/// <summary>Reconciles device registration while the app is visible. Suspension never unregisters it.</summary>
public sealed class PushGatewayService(MessagingService messaging, PushRegistrationService platform, PushGatewayClient gateway)
{
    readonly SemaphoreSlim changed = new(0, 1);
    CancellationTokenSource? foreground;
    int forceRefresh;

    public void SetForeground(bool visible)
    {
        if (visible == (foreground is not null)) return;
        if (!visible)
        {
            var previous = foreground;
            foreground = null;
            previous?.Cancel();
            return;
        }
        var cancellation = new CancellationTokenSource();
        foreground = cancellation;
        Interlocked.Exchange(ref forceRefresh, 1);
        RequestSync();
        _ = Task.Run(() => RunAsync(cancellation));
    }

    public void RequestSync(bool force = false)
    {
        if (force) Interlocked.Exchange(ref forceRefresh, 1);
        try { changed.Release(); }
        catch (SemaphoreFullException) { }
    }

    async Task RunAsync(CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        try
        {
            while (true)
            {
                await changed.WaitAsync(token);
                int failures = 0;
                DateTimeOffset retryAt = DateTimeOffset.MinValue;
                (string DeviceId, string Certificate, PushRegistration? Registration)? lastAttempt = null;
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    if (Connectivity.Current.NetworkAccess != NetworkAccess.Internet) break;
                    TimeSpan? retry = null;
                    try
                    {
                        var identity = await messaging.GetPushIdentityAsync(cancellationToken: token);
                        if (identity is null) break;
                        var snapshot = await MainThread.InvokeOnMainThreadAsync(() => (platform.Status, platform.Registration));
                        // Disabling notifications must unregister even without a current platform token.
                        bool revoke = !NotificationPreferences.Enabled || !NotificationPreferences.IsChatEnabled(identity.DeviceId)
                            || !messaging.HasPushGateway
                            || snapshot.Status is PushRegistrationStatus.PermissionDenied or PushRegistrationStatus.PermissionRequired;
                        if (!revoke && (snapshot.Status != PushRegistrationStatus.Registered || snapshot.Registration is null)) break;
                        bool force = Interlocked.Exchange(ref forceRefresh, 0) != 0;
                        var desired = (identity.DeviceId, identity.Certificate, revoke ? null : snapshot.Registration);
                        var remaining = retryAt - DateTimeOffset.UtcNow;
                        if (!force && lastAttempt == desired && remaining > TimeSpan.Zero)
                        {
                            // Ordinary chat updates must not bypass the backoff for an unchanged request.
                            await changed.WaitAsync(remaining, token);
                            continue;
                        }
                        lastAttempt = desired;
                        if (await gateway.SynchronizeAsync(identity, desired.Item3, force, token))
                            Debug.WriteLine(revoke ? "[Push] Gateway unregistered." : "[Push] Gateway registered.");
                        break;
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (PushGatewayException ex)
                    {
                        Debug.WriteLine($"[Push] Gateway error: {ex.Code}.");
                        if (!ex.Retryable) break;
                        retry = TimeSpan.FromSeconds(ex.RetryAfterSeconds ?? Math.Min(300, 5 * Math.Pow(2, Math.Min(failures++, 6))));
                    }
                    catch (Exception ex)
                    {
                        // Platform identity/storage and network failures must not block wallet or chat startup.
                        Debug.WriteLine($"[Push] Gateway error: {ex.GetType().Name}.");
                        retry = TimeSpan.FromSeconds(Math.Min(300, 5 * Math.Pow(2, Math.Min(failures++, 6))));
                    }
                    // A new token or permission change interrupts the backoff and uses the latest state.
                    retryAt = DateTimeOffset.UtcNow + retry.Value;
                    await changed.WaitAsync(retry.Value, token);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        finally { cancellation.Dispose(); }
    }

    public async Task UnregisterAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            var identity = await messaging.GetPushIdentityAsync(unregister: true, cancellationToken: timeout.Token);
            if (identity is not null) await gateway.SynchronizeAsync(identity, null, cancellationToken: timeout.Token);
        }
        catch (Exception ex)
        {
            // Wallet removal is allowed offline. Remote cleanup is best effort in that case.
            Debug.WriteLine($"[Push] Gateway unregister error: {ex.GetType().Name}.");
        }
    }
}

sealed class SecurePushGatewayStore : IPushGatewayStore
{
    const string Key = "onegate.push.gateway-state";
    public Task<string?> ReadAsync() => SecureStorage.Default.GetAsync(Key);
    public Task WriteAsync(string value) => SecureStorage.Default.SetAsync(Key, value);
}

static class PushGatewayConfiguration
{
    static bool ValidAudience(string? value)
    {
        if (value is null || value.Length > 255 || !value.StartsWith("https://", StringComparison.Ordinal)
            || System.Text.Encoding.UTF8.GetByteCount(value.TrimEnd('/') + "/api/push/notifications") > 512
            || value.Any(char.IsWhiteSpace) || value.Contains('?') || value.Contains('#') || value.Contains('\\')
            || !Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Host.Length == 0 || uri.UserInfo.Length != 0) return false;
        int path = value.IndexOf('/', "https://".Length);
        return path < 0 || path == value.Length - 1;
    }

    public static PushGatewayOptions Load()
    {
#if DEBUG
        // Device development only; Release always uses the application's public server.
        string path = Path.Combine(FileSystem.AppDataDirectory, "push-development.json");
        if (File.Exists(path))
        {
            var value = JsonSerializer.Deserialize(File.ReadAllText(path), PushGatewayJsonContext.Default.PushGatewayOptions)
                ?? throw new InvalidDataException("Invalid development push configuration.");
            if (!ValidAudience(value.Audience) || !value.Endpoint.IsAbsoluteUri || value.Endpoint.Scheme != "https" && !(value.Endpoint.IsLoopback && value.Endpoint.Scheme == "http"))
                throw new InvalidDataException("Invalid development push endpoint or audience.");
            return value;
        }
#endif
        return new(new Uri($"https://{SharedOptions.OneGateDomain}/api/push/devices"), $"https://{SharedOptions.OneGateDomain}");
    }
}
