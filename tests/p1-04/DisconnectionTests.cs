using NeoOrder.OneGate;
using NeoOrder.OneGate.DebugProtocol;
using NeoOrder.OneGate.Services.RemoteDebug;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json.Nodes;
using Xunit;

public class DisconnectionTests
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly Type ServiceType = typeof(RemoteDebugService);
    sealed class Services : IServiceProvider { public object? GetService(Type type) => null; }
    static T Field<T>(RemoteDebugService service, string name) => (T)ServiceType.GetField(name, Hidden)!.GetValue(service)!;
    static void Publish(RemoteDebugService service, RemoteDebugConnection connection) => ServiceType.GetField("connection", Hidden)!.SetValue(service, connection);
    static Task Disconnect(RemoteDebugService service, RemoteDebugConnection connection) => (Task)ServiceType.GetMethod("HandleConnectionDisconnectedAsync", Hidden)!.Invoke(service, [connection])!;

    static RemoteDebugConnection Connection(TrustedRemoteDebugger debugger) => (RemoteDebugConnection)Activator.CreateInstance(typeof(RemoteDebugConnection), Hidden, null,
        [new TcpClient(), new FramedStream(new MemoryStream()), SecureSession.Create(DebugPeerRole.DebugTarget, new byte[32], new byte[32], new byte[32]), debugger,
            (Func<string, JsonObject, Task<JsonNode?>>)((_, _) => Task.FromResult<JsonNode?>(null))], null)!;

    static async Task<(RemoteDebugService Service, TrustedRemoteDebugger Debugger)> Create()
    {
        var service = new RemoteDebugService(new Services());
        await service.SetDeveloperModeAsync(true);
        using var identity = DebugIdentity.Create();
        var debugger = new TrustedRemoteDebugger { Id = identity.KeyId, Name = "Controlled test debugger", PublicKey = Base64Url.Encode(identity.PublicKey), ReconnectSecret = Base64Url.Encode(new byte[32]), PairedAt = DateTimeOffset.UtcNow };
        Field<List<TrustedRemoteDebugger>>(service, "debuggers").Add(debugger);
        return (service, debugger);
    }

    [Fact]
    public async Task QueuedOldDisconnectCannotDetachReplacementConnection()
    {
        var (service, debugger) = await Create();
        await using var old = Connection(debugger);
        var replacement = Connection(debugger);
        Publish(service, old);
        var gate = Field<SemaphoreSlim>(service, "stateLock");
        var sessions = Field<ConcurrentDictionary<string, RemoteDebugSession>>(service, "sessions");
        await gate.WaitAsync();
        Task pending;
        try
        {
            pending = Disconnect(service, old);
            Assert.False(pending.IsCompleted);
            Publish(service, replacement);
            sessions.TryAdd("replacement", new("replacement", new Uri("https://example.invalid")));
        }
        finally { gate.Release(); }
        await pending.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(replacement, Field<RemoteDebugConnection>(service, "connection"));
        Assert.True(sessions.ContainsKey("replacement"));
        await service.DisposeAsync();
    }

    [Fact]
    public async Task DisconnectCannotRunBetweenUiAdmissionAndOpeningItsWindow()
    {
        var (service, debugger) = await Create();
        var connection = Connection(debugger);
        Publish(service, connection);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        bool? connectedWhenOpened = null;
        int stoppedHosts = 0;
        BoundaryHooks.BeforePageCreate = () => { entered.SetResult(); if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); };
        BoundaryHooks.OnOpenWindow = () => connectedWhenOpened = service.IsConnected;
        BoundaryHooks.OnHostStopped = () => stoppedHosts++;
        try
        {
            Task start = Task.Run(async () => await (Task)ServiceType.GetMethod("HandleAuthorizedRequestAsync", Hidden)!.Invoke(service,
                [connection, "session.start", new JsonObject { ["url"] = "https://example.invalid" }])!);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Task disconnected = Disconnect(service, connection);
            Assert.False(disconnected.IsCompleted);
            Assert.True(service.IsConnected);
            release.Set();
            await start.WaitAsync(TimeSpan.FromSeconds(5));
            await disconnected.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(connectedWhenOpened);
            Assert.False(service.IsConnected);
            Assert.Empty(Field<ConcurrentDictionary<string, RemoteDebugSession>>(service, "sessions"));
            Assert.Equal(1, stoppedHosts);
        }
        finally
        {
            release.Set(); BoundaryHooks.BeforePageCreate = null; BoundaryHooks.OnOpenWindow = null; BoundaryHooks.OnHostStopped = null;
            await service.DisposeAsync();
        }
    }

    [Fact]
    public async Task StoppingCapturedOldHostsCannotSweepNewSessionsOrHoldStateLock()
    {
        var (service, debugger) = await Create();
        var old = Connection(debugger); Publish(service, old);
        var oldHost = new PausedHost();
        var replacementHost = new PausedHost();
        var sessions = Field<ConcurrentDictionary<string, RemoteDebugSession>>(service, "sessions");
        sessions.TryAdd("old", new("old", new Uri("https://example.invalid")) { Host = oldHost });
        Task stopping = Disconnect(service, old);
        await oldHost.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var gate = Field<SemaphoreSlim>(service, "stateLock");
        await gate.WaitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var replacement = Connection(debugger);
        try
        {
            Publish(service, replacement);
            sessions.TryAdd("new", new("new", new Uri("https://example.invalid")) { Host = replacementHost });
        }
        finally { gate.Release(); }
        oldHost.Release.SetResult();
        await stopping.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(replacement, Field<RemoteDebugConnection>(service, "connection"));
        Assert.True(sessions.ContainsKey("new"));
        Assert.False(replacementHost.Entered.Task.IsCompleted);
        replacementHost.Release.SetResult();
        await service.DisposeAsync();
    }

    sealed class PausedHost : IRemoteDebugSessionHost
    {
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<JsonObject> GetRemoteStatusAsync() => throw new NotSupportedException();
        public Task<JsonNode?> EvaluateRemoteAsync(string expression) => throw new NotSupportedException();
        public Task<byte[]> CaptureRemoteScreenshotAsync() => throw new NotSupportedException();
        public Task ReloadRemoteAsync(bool ignoreCache) => throw new NotSupportedException();
        public async Task StopRemoteAsync() { Entered.TrySetResult(); await Release.Task; }
    }
}
