using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Models;
using System.Text.Json;

namespace NeoOrder.OneGate.Services;

public sealed class GameManifestService(HttpClient httpClient)
{
    const int MaxManifestBytes = 64 * 1024;

    public async Task<GameManifest?> LoadAsync(DApp dapp, CancellationToken cancellationToken = default)
    {
        if (!dapp.IsGamingApp || string.IsNullOrWhiteSpace(dapp.GameManifestUrl))
            return null;
        if (!Uri.TryCreate(dapp.GameManifestUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return null;

        try
        {
            using var response = await httpClient.GetAsync(uri, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;
            if (response.Content.Headers.ContentLength > MaxManifestBytes)
                return null;

            byte[] content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (content.Length > MaxManifestBytes)
                return null;

            var manifest = JsonSerializer.Deserialize<GameManifest>(content, GameManifest.JsonSerializerOptions);
            return manifest?.IsSupportedSchemaVersion == true ? manifest : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException or TaskCanceledException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
}
