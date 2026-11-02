using Meshline.Models.Protocol;
using System.Buffers.Text;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NeoOrder.OneGate.Services.Files;

public sealed record FileUploadIdentity(string Certificate, Func<ReadOnlyMemory<byte>, CancellationToken, Task<byte[]>> SignAsync);

/// <summary>Identifies one user-initiated upload and its bounded retry window.</summary>
public sealed record FileUploadOperation(Guid Id, DateTimeOffset StartedAtUtc)
{
    public static FileUploadOperation Start(DateTimeOffset now) => new(Guid.NewGuid(), now);
    public bool IsExpired(DateTimeOffset now) => now >= StartedAtUtc.AddHours(24);
}

/// <summary>Uploads purpose-tagged content using the current account's authorized device.</summary>
public sealed class FileUploadClient
{
    static readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
    { Timeout = TimeSpan.FromSeconds(60) };

    readonly HttpClient client;
    readonly TimeProvider clock;

    public FileUploadClient() : this(http, TimeProvider.System) { }
    internal FileUploadClient(HttpClient client, TimeProvider clock) { this.client = client; this.clock = clock; }

    public async Task<ContentReference> UploadAsync(Guid uploadId, string purpose, string fileName, string contentType, byte[] bytes,
        FileUploadIdentity identity, CancellationToken cancellationToken = default)
    {
        if (uploadId == Guid.Empty) throw new ArgumentException("An upload operation ID is required.", nameof(uploadId));
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentType);
        string audience = $"https://{SharedOptions.OneGateDomain}";
        var payload = new FileUploadPayload(uploadId, identity.Certificate, purpose, fileName,
            "sha256:" + Base64Url.EncodeToString(SHA256.HashData(bytes)), contentType, bytes.LongLength, clock.GetUtcNow().ToUnixTimeMilliseconds());
        string json = JsonSerializer.Serialize(payload, FileUploadJsonContext.Default.FileUploadPayload);
        byte[] signature = await identity.SignAsync(Encoding.UTF8.GetBytes($"ONEGATE_FILE_UPLOAD_V1\n{audience}\n{json}"), cancellationToken);
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(json, Encoding.UTF8), "payload");
        form.Add(new StringContent(Convert.ToBase64String(signature)), "signature");
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(content, "file", fileName);
        using var request = new HttpRequestMessage(HttpMethod.Post, audience + "/api/files") { Content = form };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var output = new MemoryStream();
        byte[] buffer = new byte[4096];
        int read;
        while ((read = await stream.ReadAsync(buffer, timeout.Token)) > 0)
        {
            if (output.Length + read > 8192) throw new InvalidDataException("Upload response is too large.");
            output.Write(buffer, 0, read);
        }
        var reference = ProtocolModel.FromJson<ContentReference>(Encoding.UTF8.GetString(output.ToArray()));
        if (reference is null || reference.Validate() is not null || reference.Hash != payload.Hash || reference.Size != bytes.LongLength
            || reference.ContentType != contentType || reference.Encryption is not null
            || !reference.Uri.StartsWith(audience + "/api/files/", StringComparison.Ordinal))
            throw new InvalidDataException("Invalid file upload response.");
        return reference;
    }

}

internal sealed record FileUploadPayload(Guid UploadId, string DeviceCertificate, string Purpose, string FileName, string Hash, string ContentType, long Size, long IssuedAt);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(FileUploadPayload))]
internal partial class FileUploadJsonContext : JsonSerializerContext;
