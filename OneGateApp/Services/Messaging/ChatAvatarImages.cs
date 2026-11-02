using SkiaSharp;

namespace NeoOrder.OneGate.Services.Messaging;

internal static class ChatAvatarImages
{
    public const int UploadLimit = 128 * 1024;
    public const int DownloadLimit = 1024 * 1024;

    public static string GetUploadFileName(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        return Path.ChangeExtension(Path.GetFileName(fileName.Replace('\\', '/')), ".jpg");
    }

    public static bool IsSupported(byte[] bytes, string contentType)
    {
        using var codec = SKCodec.Create(new MemoryStream(bytes, writable: false));
        if (codec is null || codec.Info.Width is <= 0 or > 4096 || codec.Info.Height is <= 0 or > 4096 || codec.FrameCount > 1)
            return false;
        string? actual = codec.EncodedFormat switch
        {
            SKEncodedImageFormat.Jpeg => "image/jpeg", SKEncodedImageFormat.Png => "image/png",
            SKEncodedImageFormat.Webp => "image/webp", _ => null
        };
        if (actual != contentType) return false;
        using var bitmap = new SKBitmap(codec.Info.WithColorType(SKColorType.Bgra8888).WithAlphaType(SKAlphaType.Premul));
        return codec.GetPixels(bitmap.Info, bitmap.GetPixels()) == SKCodecResult.Success;
    }

    public static async Task<byte[]> ReadBoundedAsync(Stream stream, int maximum, CancellationToken cancellationToken)
    {
        using var output = new MemoryStream();
        byte[] buffer = new byte[16384];
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            if (output.Length + read > maximum) throw new InvalidDataException("Image exceeds the size limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    public static SKBitmap PreparePhoto(byte[] bytes)
    {
        using var codec = SKCodec.Create(new MemoryStream(bytes, writable: false));
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0
            || (long)codec.Info.Width * codec.Info.Height > 64_000_000 || codec.FrameCount > 1)
            throw new InvalidDataException("Unsupported photo.");
        var size = codec.GetScaledDimensions(Math.Min(1f, 2048f / Math.Max(codec.Info.Width, codec.Info.Height)));
        if ((long)size.Width * size.Height > 16_777_216) throw new InvalidDataException("The photo cannot be decoded within the pixel limit.");
        using var decoded = new SKBitmap(size.Width, size.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        if (codec.GetPixels(decoded.Info, decoded.GetPixels()) != SKCodecResult.Success)
            throw new InvalidDataException("The photo could not be decoded.");
        int w = decoded.Width, h = decoded.Height;
        bool swap = (int)codec.EncodedOrigin >= 5;
        var result = new SKBitmap(swap ? h : w, swap ? w : h, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.White);
        var matrix = codec.EncodedOrigin switch
        {
            SKEncodedOrigin.TopRight => Matrix(-1, 0, w, 0, 1, 0),
            SKEncodedOrigin.BottomRight => Matrix(-1, 0, w, 0, -1, h),
            SKEncodedOrigin.BottomLeft => Matrix(1, 0, 0, 0, -1, h),
            SKEncodedOrigin.LeftTop => Matrix(0, 1, 0, 1, 0, 0),
            SKEncodedOrigin.RightTop => Matrix(0, -1, h, 1, 0, 0),
            SKEncodedOrigin.RightBottom => Matrix(0, -1, h, -1, 0, w),
            SKEncodedOrigin.LeftBottom => Matrix(0, 1, 0, -1, 0, w),
            _ => SKMatrix.Identity
        };
        canvas.SetMatrix(matrix);
        canvas.DrawBitmap(decoded, 0, 0);
        return result;
    }

    static SKMatrix Matrix(float a, float b, float c, float d, float e, float f) => new()
    { ScaleX = a, SkewX = b, TransX = c, SkewY = d, ScaleY = e, TransY = f, Persp2 = 1 };

    public static byte[] Crop(SKBitmap photo, SKRect source)
    {
        using var result = new SKBitmap(512, 512, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(result))
        {
            canvas.Clear(SKColors.White);
            using var image = SKImage.FromBitmap(photo);
            canvas.DrawImage(image, source, new SKRect(0, 0, 512, 512), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        }
        using var output = SKImage.FromBitmap(result);
        for (int quality = 90; quality >= 10; quality -= 10)
        {
            using var data = output.Encode(SKEncodedImageFormat.Jpeg, quality);
            if (data.Size <= UploadLimit) return data.ToArray();
        }
        throw new InvalidDataException("The cropped image cannot fit the upload limit.");
    }
}
