#if WINDOWS

using NeoOrder.OneGate.Data;
using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace NeoOrder.OneGate.Services;

class HomeShortcutService(HttpClient httpClient) : IHomeShortcutService
{
    public bool IsSupported => !string.IsNullOrWhiteSpace(GetDesktopDirectory());

    public async Task<bool> AddShortcutAsync(DApp dapp)
    {
        string title = dapp.NameLocalizer.Localize()!;
        string deepLink = $"https://{SharedOptions.OneGateDomain}/app/{dapp.Id}";
        string shortcutPath = Path.Combine(GetDesktopDirectory(), CreateShortcutFileName(title));
        string? iconPath = await CreateShortcutIconAsync(dapp, $"dapp-{dapp.Id}");
        List<string> lines =
        [
            "[InternetShortcut]",
            $"URL={deepLink}"
        ];
        if (!string.IsNullOrWhiteSpace(iconPath))
        {
            lines.Add($"IconFile={iconPath}");
            lines.Add("IconIndex=0");
        }
        await File.WriteAllLinesAsync(shortcutPath, lines);
        return true;
    }

    static string GetDesktopDirectory()
    {
        return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    }

    static string CreateShortcutFileName(string title)
    {
        string safeTitle = CreateSafeFileName(title);
        if (string.IsNullOrWhiteSpace(safeTitle))
            safeTitle = "DApp";
        string fileName = $"OneGate - {safeTitle}";
        if (fileName.Length > 120)
            fileName = fileName[..120].TrimEnd();
        return fileName + ".url";
    }

    static string CreateSafeFileName(string value)
    {
        char[] invalidChars = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (char ch in value.Trim())
        {
            if (invalidChars.Contains(ch) || char.IsControl(ch))
                builder.Append('_');
            else
                builder.Append(ch);
        }
        return builder.ToString().Trim().TrimEnd('.');
    }

    async Task<string?> CreateShortcutIconAsync(DApp dapp, string shortcutId)
    {
        if (string.IsNullOrWhiteSpace(dapp.IconUrl) || !TryCreateIconUri(dapp.IconUrl, out Uri? iconUri))
            return null;
        try
        {
            byte[] imageBytes = await httpClient.GetByteArrayAsync(iconUri);
            if (!TryCreateIcoBytesFromPng(imageBytes, out byte[]? icoBytes))
                return null;
            string iconDirectory = Path.Combine(FileSystem.AppDataDirectory, "ShortcutIcons");
            Directory.CreateDirectory(iconDirectory);
            string iconPath = Path.Combine(iconDirectory, CreateSafeFileName(shortcutId) + ".ico");
            await File.WriteAllBytesAsync(iconPath, icoBytes);
            return iconPath;
        }
        catch
        {
            return null;
        }
    }

    bool TryCreateIconUri(string iconUrl, out Uri? uri)
    {
        if (Uri.TryCreate(iconUrl, UriKind.Absolute, out uri))
            return true;
        if (httpClient.BaseAddress is not null && Uri.TryCreate(httpClient.BaseAddress, iconUrl, out uri))
            return true;
        uri = null;
        return false;
    }

    static bool TryCreateIcoBytesFromPng(byte[] pngBytes, [NotNullWhen(true)] out byte[]? icoBytes)
    {
        icoBytes = null;
        if (!TryGetPngSize(pngBytes, out int width, out int height))
            return false;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write((ushort)0);      // Reserved
        writer.Write((ushort)1);      // ICO type
        writer.Write((ushort)1);      // Image count
        writer.Write((byte)(width >= 256 ? 0 : width));
        writer.Write((byte)(height >= 256 ? 0 : height));
        writer.Write((byte)0);        // Color count
        writer.Write((byte)0);        // Reserved
        writer.Write((ushort)1);      // Color planes
        writer.Write((ushort)32);     // Bits per pixel
        writer.Write(pngBytes.Length);
        writer.Write(22);             // Image data offset
        writer.Write(pngBytes);
        icoBytes = stream.ToArray();
        return true;
    }

    static bool TryGetPngSize(byte[] bytes, out int width, out int height)
    {
        width = 0;
        height = 0;
        ReadOnlySpan<byte> span = bytes;
        ReadOnlySpan<byte> pngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
        ReadOnlySpan<byte> ihdr = [73, 72, 68, 82];
        if (span.Length < 24)
            return false;
        if (!span[..8].SequenceEqual(pngSignature))
            return false;
        if (!span.Slice(12, 4).SequenceEqual(ihdr))
            return false;
        width = BinaryPrimitives.ReadInt32BigEndian(span.Slice(16, 4));
        height = BinaryPrimitives.ReadInt32BigEndian(span.Slice(20, 4));
        return width > 0 && height > 0;
    }
}

#endif
