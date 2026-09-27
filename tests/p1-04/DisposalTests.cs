using NeoOrder.OneGate.DebugProtocol;
using NeoOrder.OneGate.Services.RemoteDebug;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json.Nodes;
using Xunit;

public class DisposalTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    sealed class Services : IServiceProvider { public object? GetService(Type type) => null; }

    [Fact]
    public async Task DisposeDrainsConnectionBeforeReleasingItsDependencies()
    {
        var service = new RemoteDebugService(new Services());
        await service.SetDeveloperModeAsync(true);
        var gate = (SemaphoreSlim)typeof(RemoteDebugService).GetField("connectLock", Hidden)!.GetValue(service)!;
        await gate.WaitAsync();
        Task disposal = service.DisposeAsync().AsTask();
        try
        {
            Assert.False(disposal.IsCompleted);
        }
        finally
        {
            gate.Release();
        }
        await disposal.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => service.SetDeveloperModeAsync(true));
        await service.DisposeAsync();
    }

    [Fact]
    public async Task DisposeCancelsRealPausedHandshakeAndRejectsQueuedConnection()
    {
        var service = new RemoteDebugService(new Services());
        await service.SetDeveloperModeAsync(true);
        using var identity = DebugIdentity.Create();
        var debugger = new TrustedRemoteDebugger
        {
            Id = identity.KeyId, Name = "Test peer", PublicKey = Base64Url.Encode(identity.PublicKey),
            ReconnectSecret = Base64Url.Encode(new byte[32]), PairedAt = DateTimeOffset.UtcNow
        };
        ((List<TrustedRemoteDebugger>)typeof(RemoteDebugService).GetField("debuggers", Hidden)!.GetValue(service)!).Add(debugger);
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Task Connect() => (Task)typeof(RemoteDebugService).GetMethod("ConnectAsync", Hidden)!
            .Invoke(service, [null, (debugger, "127.0.0.1", port)])!;
        Task active = Connect();
        using TcpClient server = await listener.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
        await using var framed = new FramedStream(server.GetStream());
        // Observe the actual protocol hello; deliberately withhold serverHello.
        await framed.ReadJsonAsync<JsonObject>(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));
        Task queued = Connect();
        await service.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        Exception? activeError = await Record.ExceptionAsync(() => active.WaitAsync(TimeSpan.FromSeconds(5)));
        Exception? queuedError = await Record.ExceptionAsync(() => queued.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.IsAssignableFrom<OperationCanceledException>(activeError);
        var disposed = Assert.IsType<ObjectDisposedException>(queuedError);
        Assert.Equal(typeof(RemoteDebugService).FullName, disposed.ObjectName);
        Assert.False(service.IsConnected);
    }
}
