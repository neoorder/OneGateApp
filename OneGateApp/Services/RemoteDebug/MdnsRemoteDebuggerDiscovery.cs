using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NeoOrder.OneGate.Services.RemoteDebug;

sealed class MdnsRemoteDebuggerDiscovery : IAsyncDisposable
{
    const string ServiceName = "_onegate-debug._tcp.local";
    static readonly IPAddress MulticastAddress = IPAddress.Parse("224.0.0.251");
    readonly CancellationTokenSource cancellation = new();
    UdpClient? client;
    Task? receiveLoop;
#if ANDROID
    Android.Net.Wifi.WifiManager.MulticastLock? multicastLock;
#endif

    public event Action<string, string, int>? RemoteDebuggerDiscovered;

    public async Task StartAsync()
    {
        if (client is not null) return;
#if ANDROID
        var wifiManager = (Android.Net.Wifi.WifiManager?)Android.App.Application.Context.GetSystemService(Android.Content.Context.WifiService);
        multicastLock = wifiManager?.CreateMulticastLock("OneGateRemoteDebug");
        multicastLock?.SetReferenceCounted(false);
        multicastLock?.Acquire();
#endif
        client = new UdpClient(AddressFamily.InterNetwork);
        client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        client.Client.Bind(new IPEndPoint(IPAddress.Any, 5353));
        client.JoinMulticastGroup(MulticastAddress);
        receiveLoop = ReceiveLoopAsync(cancellation.Token);
        await SendQueryAsync(cancellation.Token);
    }

    async Task SendQueryAsync(CancellationToken cancellationToken)
    {
        byte[] name = EncodeName(ServiceName);
        byte[] packet = new byte[12 + name.Length + 4];
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(4), 1);
        name.CopyTo(packet, 12);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(12 + name.Length), 12);
        BinaryPrimitives.WriteUInt16BigEndian(packet.AsSpan(14 + name.Length), 1);
        await client!.SendAsync(packet, new IPEndPoint(MulticastAddress, 5353), cancellationToken);
    }

    async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                UdpReceiveResult result = await client!.ReceiveAsync(cancellationToken);
                ParseResponse(result.Buffer, result.RemoteEndPoint.Address);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    void ParseResponse(byte[] packet, IPAddress sourceAddress)
    {
        if (packet.Length < 12) return;
        ushort flags = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(2));
        // Only complete, successful standard responses can advertise a debugger.
        if ((flags & 0xfa0f) != 0x8000) return;
        int questionCount = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(4));
        int recordCount = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(6))
            + BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(8))
            + BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(10));
        int offset = 12;
        for (int i = 0; i < questionCount; i++)
        {
            if (!TryReadName(packet, ref offset, packet.Length, out _) || offset + 4 > packet.Length) return;
            offset += 4;
        }
        Dictionary<string, (string Host, int Port)> services = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> debuggerIds = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, IPAddress> addresses = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < recordCount; i++)
        {
            if (!TryReadName(packet, ref offset, packet.Length, out string name)) return;
            if (offset + 10 > packet.Length) return;
            ushort type = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(offset));
            int dataLength = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(offset + 8));
            offset += 10;
            if (offset + dataLength > packet.Length) return;
            int dataOffset = offset;
            bool debuggerRecord = name.EndsWith("." + ServiceName, StringComparison.OrdinalIgnoreCase);
            if (type == 33 && debuggerRecord)
            {
                if (dataLength < 7) return;
                int port = BinaryPrimitives.ReadUInt16BigEndian(packet.AsSpan(dataOffset + 4));
                int nameOffset = dataOffset + 6;
                if (!TryReadName(packet, ref nameOffset, dataOffset + dataLength, out string host)
                    || nameOffset != dataOffset + dataLength) return;
                if (port > 0 && host.Length > 0) services[name] = (host, port);
            }
            else if (type == 16 && debuggerRecord)
            {
                int end = dataOffset + dataLength;
                int cursor = dataOffset;
                while (cursor < end)
                {
                    int length = packet[cursor++];
                    if (cursor + length > end) return;
                    string value = Encoding.UTF8.GetString(packet, cursor, length);
                    cursor += length;
                    if (value.StartsWith("debuggerId=", StringComparison.Ordinal))
                        debuggerIds[name] = value[11..];
                }
            }
            else if (type == 1 && dataLength == 4)
            {
                addresses[name] = new IPAddress(packet.AsSpan(dataOffset, 4));
            }
            offset += dataLength;
        }
        foreach (var (instance, service) in services)
        {
            if (!debuggerIds.TryGetValue(instance, out string? debuggerId) || string.IsNullOrWhiteSpace(debuggerId)) continue;
            IPAddress address = addresses.GetValueOrDefault(service.Host) ?? sourceAddress;
            RemoteDebuggerDiscovered?.Invoke(debuggerId, address.ToString(), service.Port);
        }
    }

    // Malformed multicast traffic is expected input, not an exception in the receive loop.
    static bool TryReadName(byte[] packet, ref int offset, int end, out string name)
    {
        name = "";
        List<string> labels = [];
        int cursor = offset;
        bool jumped = false;
        int wireLength = 1; // Include the terminating root label in the 255-byte DNS name limit.
        while (cursor < end)
        {
            byte length = packet[cursor++];
            if (length == 0)
            {
                if (!jumped) offset = cursor;
                name = string.Join('.', labels);
                return true;
            }
            if ((length & 0xc0) == 0xc0)
            {
                if (cursor >= end) return false;
                int pointer = ((length & 0x3f) << 8) | packet[cursor++];
                // Compression refers to a prior name. Each jump tightens the read boundary,
                // rejecting cycles without an arbitrary limit on the number of labels.
                if (pointer < 12 || pointer >= cursor - 2) return false;
                if (!jumped) offset = cursor;
                end = cursor - 2;
                cursor = pointer;
                jumped = true;
                continue;
            }
            if ((length & 0xc0) != 0 || cursor + length > end) return false;
            wireLength += length + 1;
            if (wireLength > 255) return false;
            labels.Add(Encoding.UTF8.GetString(packet, cursor, length));
            cursor += length;
        }
        return false;
    }

    static byte[] EncodeName(string value)
    {
        using MemoryStream stream = new();
        foreach (string label in value.Split('.'))
        {
            byte[] bytes = Encoding.UTF8.GetBytes(label);
            stream.WriteByte(checked((byte)bytes.Length));
            stream.Write(bytes);
        }
        stream.WriteByte(0);
        return stream.ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        cancellation.Cancel();
        client?.Dispose();
        if (receiveLoop is not null) await receiveLoop;
#if ANDROID
        if (multicastLock?.IsHeld == true) multicastLock.Release();
        multicastLock?.Dispose();
#endif
        cancellation.Dispose();
    }
}
