using System.Buffers.Binary;
using System.Text.Json;

namespace NeoOrder.OneGate.DebugProtocol;

public sealed class FramedStream(Stream stream, int maximumFrameLength = 16 * 1024 * 1024) : IAsyncDisposable
{
    readonly SemaphoreSlim writeLock = new(1, 1);

    public async Task<byte[]> ReadAsync(CancellationToken cancellationToken = default)
    {
        byte[] header = new byte[4];
        await stream.ReadExactlyAsync(header, cancellationToken);
        int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(header));
        if (length > maximumFrameLength)
            throw new InvalidDataException($"Frame exceeds {maximumFrameLength} bytes.");
        byte[] payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        return payload;
    }

    public async Task<T> ReadJsonAsync<T>(CancellationToken cancellationToken = default)
    {
        byte[] payload = await ReadAsync(cancellationToken);
        return JsonSerializer.Deserialize<T>(payload, RemoteDebugJson.Options)
            ?? throw new InvalidDataException("The remote peer sent an empty JSON value.");
    }

    public async Task WriteAsync(ReadOnlyMemory<byte> payload, CancellationToken cancellationToken = default)
    {
        if (payload.Length > maximumFrameLength)
            throw new InvalidDataException($"Frame exceeds {maximumFrameLength} bytes.");
        byte[] header = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(header, checked((uint)payload.Length));
        await writeLock.WaitAsync(cancellationToken);
        try
        {
            await stream.WriteAsync(header, cancellationToken);
            await stream.WriteAsync(payload, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        finally
        {
            writeLock.Release();
        }
    }

    public Task WriteJsonAsync<T>(T value, CancellationToken cancellationToken = default)
        => WriteAsync(JsonSerializer.SerializeToUtf8Bytes(value, RemoteDebugJson.Options), cancellationToken);

    public async ValueTask DisposeAsync()
    {
        writeLock.Dispose();
        await stream.DisposeAsync();
    }
}

public static class RemoteDebugJson
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };
}
