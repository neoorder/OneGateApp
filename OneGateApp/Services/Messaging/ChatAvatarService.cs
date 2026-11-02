using Meshline.Models.Protocol;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace NeoOrder.OneGate.Services.Messaging;

public sealed class ChatAvatarService
{
    static readonly IPNetwork[] excluded = new[]
    {
        "0.0.0.0/8", "10.0.0.0/8", "100.64.0.0/10", "127.0.0.0/8", "169.254.0.0/16",
        "172.16.0.0/12", "192.0.0.0/24", "192.0.2.0/24", "192.168.0.0/16", "198.18.0.0/15",
        "198.51.100.0/24", "203.0.113.0/24", "224.0.0.0/4", "240.0.0.0/4",
        "2001::/32", "2001:db8::/32", "2002::/16"
    }.Select(IPNetwork.Parse).ToArray();
    static readonly IPNetwork globalV6 = IPNetwork.Parse("2000::/3");
    static readonly HttpClient http = CreateClient();
    readonly SemaphoreSlim downloads = new(4, 4);
    readonly SemaphoreSlim[] locks = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();
    readonly Dictionary<string, DateTimeOffset> failures = [];
    readonly object failureLock = new();

    static HttpClient CreateClient() => new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false, UseCookies = false, UseProxy = false, AutomaticDecompression = DecompressionMethods.None,
        ConnectCallback = async (context, token) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token);
            // Connect to the exact validated address, avoiding a second DNS lookup/rebinding.
            if (addresses.Length == 0 || addresses.Any(p => !IsPublicAddress(p)))
                throw new HttpRequestException("Avatar host is not public.");
            foreach (var address in addresses)
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), token);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch { socket.Dispose(); if (token.IsCancellationRequested) throw; }
            }
            throw new HttpRequestException("Avatar host is unavailable.");
        }
    }) { Timeout = TimeSpan.FromSeconds(30) };

    internal static bool IsPublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (address.AddressFamily == AddressFamily.InterNetworkV6 && !globalV6.Contains(address)) return false;
        return !excluded.Any(p => p.Contains(address));
    }

    internal static bool IsAllowedUri(Uri uri) => uri.IsAbsoluteUri && uri.Scheme == "https"
        && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0
        && uri.HostNameType is UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6
        && (!IPAddress.TryParse(uri.Host, out var address) || IsPublicAddress(address));

    public async Task<string?> GetImageAsync(ContentReference? reference, CancellationToken cancellationToken = default)
    {
        if (reference is null || reference.Validate() is not null || reference.Encryption is not null
            || reference.Size is <= 0 or > ChatAvatarImages.DownloadLimit
            || reference.ContentType is not ("image/jpeg" or "image/png" or "image/webp")) return null;
        string hash = reference.Hash;
        byte[] digest = System.Buffers.Text.Base64Url.DecodeFromChars(hash.AsSpan(7));
        string directory = Path.Combine(FileSystem.CacheDirectory, "chat-avatars");
        string extension = reference.ContentType switch { "image/jpeg" => ".jpg", "image/png" => ".png", _ => ".webp" };
        string path = Path.Combine(directory, Convert.ToHexStringLower(digest) + extension);
        var gate = locks[digest[0] % locks.Length];
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(path))
            {
                byte[] cached = new FileInfo(path).Length == reference.Size ? await File.ReadAllBytesAsync(path, cancellationToken) : [];
                if (reference.Verify(cached) is null && await Task.Run(() => ChatAvatarImages.IsSupported(cached, reference.ContentType), cancellationToken)) return path;
                File.Delete(path);
            }
            lock (failureLock)
                if (failures.TryGetValue(reference.Uri + hash, out var until) && until > DateTimeOffset.UtcNow) return null;
            await downloads.WaitAsync(cancellationToken);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                byte[] bytes = await DownloadAsync(new Uri(reference.Uri), reference.Size, timeout.Token);
                if (reference.Verify(bytes) is not null || !await Task.Run(() => ChatAvatarImages.IsSupported(bytes, reference.ContentType), timeout.Token))
                    throw new InvalidDataException("Avatar verification failed.");
                Directory.CreateDirectory(directory);
                string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    await File.WriteAllBytesAsync(temporary, bytes, cancellationToken);
                    File.Move(temporary, path, overwrite: true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
                return path;
            }
            finally { downloads.Release(); }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            lock (failureLock)
            {
                foreach (string key in failures.Where(p => p.Value <= DateTimeOffset.UtcNow).Select(p => p.Key).ToArray()) failures.Remove(key);
                if (failures.Count < 1024) failures[reference.Uri + hash] = DateTimeOffset.UtcNow.AddMinutes(1);
            }
            return null;
        }
        finally { gate.Release(); }
    }

    static async Task<byte[]> DownloadAsync(Uri uri, long expectedSize, CancellationToken cancellationToken)
    {
        for (int redirects = 0; redirects <= 3; redirects++)
        {
            if (!IsAllowedUri(uri)) throw new InvalidDataException("Unsupported avatar location.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                uri = response.Headers.Location is { } location ? new Uri(uri, location) : throw new InvalidDataException("Missing redirect location.");
                continue;
            }
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } size && size != expectedSize)
                throw new InvalidDataException("Avatar size mismatch.");
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await ChatAvatarImages.ReadBoundedAsync(stream, checked((int)expectedSize), cancellationToken);
        }
        throw new HttpRequestException("Too many avatar redirects.");
    }
}
