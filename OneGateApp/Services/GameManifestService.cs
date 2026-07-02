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
            using var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;
            if (response.Content.Headers.ContentLength > MaxManifestBytes)
                return null;

            await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var content = new MemoryStream();
            byte[] buffer = new byte[8192];
            int totalBytesRead = 0;
            int bytesRead;
            while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                totalBytesRead += bytesRead;
                if (totalBytesRead > MaxManifestBytes)
                    return null;

                content.Write(buffer, 0, bytesRead);
            }

            content.Position = 0;
            var manifest = await JsonSerializer.DeserializeAsync<GameManifest>(content, GameManifest.JsonSerializerOptions, cancellationToken);
            return manifest?.IsSupportedSchemaVersion == true ? manifest : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException or TaskCanceledException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
}
