using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NeoOrder.OneGate.Services.Notifications;

public sealed record PushGatewayOptions(Uri Endpoint, string Audience)
{
    [JsonIgnore]
    public string CallbackUrl => Audience.TrimEnd('/') + "/api/push/notifications";
}

public sealed class PushDeviceIdentity(string deviceId, string certificate,
    Func<ReadOnlyMemory<byte>, CancellationToken, Task<byte[]>> sign)
{
    public string DeviceId { get; } = deviceId;
    public string Certificate { get; } = certificate;
    public Task<byte[]> SignAsync(ReadOnlyMemory<byte> input, CancellationToken cancellationToken) => sign(input, cancellationToken);
}

public interface IPushGatewayStore
{
    Task<string?> ReadAsync();
    Task WriteAsync(string value);
}

/// <summary>Serializes signed registration changes and preserves their ordering across restarts.</summary>
public sealed class PushGatewayClient(HttpClient http, PushGatewayOptions options, IPushGatewayStore store, TimeProvider clock)
{
    readonly SemaphoreSlim gate = new(1, 1);
    PushGatewayState? state;
    readonly Dictionary<string, string> rejected = [];

    public async Task<bool> SynchronizeAsync(PushDeviceIdentity identity, PushRegistration? registration,
        bool force = false, CancellationToken cancellationToken = default)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            state ??= await LoadAsync();
            string key = options.Endpoint.AbsoluteUri + "\n" + options.Audience + "\n" + identity.DeviceId;
            if (!state.Bindings.TryGetValue(key, out var binding))
            {
                if (registration is null) return false;
                state.Bindings[key] = binding = new();
            }
            if (registration is not null)
            {
                binding.ApplicationId = registration.ApplicationId;
                binding.Provider = registration.Provider;
                binding.Environment = registration.Environment;
            }
            var payload = new PushGatewayPayload(registration is null ? "unregister" : "register", identity.Certificate,
                binding.ApplicationId, binding.Provider, registration?.RegistrationId, binding.Environment, 0, (System.Globalization.CultureInfo.DefaultThreadCurrentUICulture ?? System.Globalization.CultureInfo.CurrentUICulture).Name);
            string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(payload, PushGatewayJsonContext.Default.PushGatewayPayload))));
            if (!force && binding.Pending is null && binding.AcknowledgedFingerprint == fingerprint) return false;
            // Chat updates can be frequent. Retry a rejected state only after a real change or explicit refresh.
            if (!force && rejected.GetValueOrDefault(key) == fingerprint) return false;
            rejected.Remove(key);

            long now = clock.GetUtcNow().ToUnixTimeMilliseconds();
            var pending = binding.Pending;
            if (pending is null || pending.Fingerprint != fingerprint || pending.IssuedAt <= now - 240_000)
            {
                long issuedAt = Math.Max(now, checked(binding.LastIssuedAt + 1));
                if (issuedAt > now + 30_000) throw new PushGatewayException("clock_out_of_sync", true, 60);
                string json = JsonSerializer.Serialize(payload with { IssuedAt = issuedAt }, PushGatewayJsonContext.Default.PushGatewayPayload);
                byte[] signature = await identity.SignAsync(Encoding.UTF8.GetBytes("ONEGATE_PUSH_V1\n" + options.Audience + "\n" + json), cancellationToken);
                pending = new(new(json, Convert.ToBase64String(signature)), fingerprint, issuedAt);
                binding.LastIssuedAt = issuedAt;
                binding.Pending = pending;
                // Persist before sending: a lost response or process exit must not allow an older request to win.
            }
            await SaveAsync();
            cancellationToken.ThrowIfCancellationRequested();
            using var content = JsonContent.Create(pending.Request, PushGatewayJsonContext.Default.PushGatewayRequest);
            using var response = await http.PostAsync(options.Endpoint, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                string code = "http_" + (int)response.StatusCode;
                try
                {
                    using var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                    if (error.RootElement.TryGetProperty("title", out var title) && title.GetString() is { Length: > 0 and <= 64 } value
                        && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_')) code = value;
                }
                catch (JsonException) { }
                bool retryable = response.StatusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests
                    || (int)response.StatusCode >= 500 || code is "concurrent_registration" or "expired_request";
                if (!retryable) rejected[key] = fingerprint;
                if (code == "expired_request") { binding.Pending = null; await SaveAsync(); }
                var retry = response.Headers.RetryAfter;
                double? seconds = retry?.Delta?.TotalSeconds ?? (retry?.Date - clock.GetUtcNow())?.TotalSeconds;
                throw new PushGatewayException(code, retryable, seconds is > 0 ? (int)Math.Min(seconds.Value, 3600) : null);
            }
            var status = await response.Content.ReadFromJsonAsync(PushGatewayJsonContext.Default.PushGatewayResponse, cancellationToken)
                ?? throw new PushGatewayException("invalid_response", true);
            if (status.Id <= 0 || status.Provider != binding.Provider || status.Environment != binding.Environment
                || status.LastIssuedAt < pending.IssuedAt)
                throw new PushGatewayException("invalid_response", true);
            binding.LastIssuedAt = Math.Max(binding.LastIssuedAt, status.LastIssuedAt);
            binding.Pending = null;
            // An idempotent replay can report a registration that the provider has since invalidated.
            binding.AcknowledgedFingerprint = status.Active == (registration is not null) ? fingerprint : null;
            await SaveAsync();
            if (binding.AcknowledgedFingerprint is null) throw new PushGatewayException("registration_state_changed", true);
            return true;
        }
        finally { gate.Release(); }
    }

    async Task<PushGatewayState> LoadAsync()
    {
        string? json = await store.ReadAsync();
        return json is null ? new() : JsonSerializer.Deserialize(json, PushGatewayJsonContext.Default.PushGatewayState)
            ?? throw new InvalidDataException("Invalid push registration state.");
    }

    Task SaveAsync() => store.WriteAsync(JsonSerializer.Serialize(state!, PushGatewayJsonContext.Default.PushGatewayState));
}

public sealed class PushGatewayException(string code, bool retryable, int? retryAfterSeconds = null) : Exception(code)
{
    public string Code { get; } = code;
    public bool Retryable { get; } = retryable;
    public int? RetryAfterSeconds { get; } = retryAfterSeconds;
}

public sealed record PushGatewayPayload(string Action, string DeviceCertificate, string ApplicationId,
    string Provider, string? RegistrationId, string? Environment, long IssuedAt, string? Language = null);
public sealed record PushGatewayRequest(string Payload, string Signature)
{
    public override string ToString() => nameof(PushGatewayRequest);
}
public sealed record PushGatewayResponse(long Id, string Provider, string? Environment, bool Active, long LastIssuedAt);
public sealed record PendingPushRequest(PushGatewayRequest Request, string Fingerprint, long IssuedAt);
public sealed class PushGatewayBinding
{
    public string ApplicationId { get; set; } = "";
    public string Provider { get; set; } = "";
    public string? Environment { get; set; }
    public long LastIssuedAt { get; set; }
    public string? AcknowledgedFingerprint { get; set; }
    public PendingPushRequest? Pending { get; set; }
}
public sealed class PushGatewayState
{
    public Dictionary<string, PushGatewayBinding> Bindings { get; set; } = [];
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(PushGatewayPayload))]
[JsonSerializable(typeof(PushGatewayRequest))]
[JsonSerializable(typeof(PushGatewayResponse))]
[JsonSerializable(typeof(PushGatewayState))]
[JsonSerializable(typeof(PushGatewayOptions))]
internal partial class PushGatewayJsonContext : JsonSerializerContext;
