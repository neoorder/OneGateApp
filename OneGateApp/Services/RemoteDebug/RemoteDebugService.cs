using NeoOrder.OneGate.DebugProtocol;
using NeoOrder.OneGate.Pages;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NeoOrder.OneGate.Services.RemoteDebug;

public sealed class RemoteDebugService(IServiceProvider serviceProvider) : IAsyncDisposable
{
    const string SecureStorageKey = "onegate.remote-debug.state";
    static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(8);
    static readonly TimeSpan ApprovalTimeout = TimeSpan.FromMinutes(2);
    readonly SemaphoreSlim stateLock = new(1, 1);
    readonly SemaphoreSlim connectLock = new(1, 1);
    readonly ConcurrentDictionary<string, RemoteDebugSession> sessions = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, Task<JsonNode?>> operations = new(StringComparer.Ordinal);
    DebugIdentity? debugTargetIdentity;
    List<TrustedRemoteDebugger> debuggers = [];
    MdnsRemoteDebuggerDiscovery? discovery;
    RemoteDebugConnection? connection;
    bool initialized;
    bool developerModeEnabled;

    public event EventHandler? StateChanged;
    public bool IsDeveloperModeEnabled => developerModeEnabled;
    public bool IsConnected => connection is not null;
    public string? ConnectedDebuggerName => connection?.Debugger.Name;

    public async Task SetDeveloperModeAsync(bool enabled)
    {
        await EnsureInitializedAsync();
        developerModeEnabled = enabled;
        if (enabled)
        {
            await StartDiscoveryAsync();
        }
        else
        {
            await StopConnectionAndSessionsAsync();
            await StopDiscoveryAsync();
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task<IReadOnlyList<TrustedRemoteDebugger>> GetTrustedDebuggersAsync()
    {
        await EnsureInitializedAsync();
        await stateLock.WaitAsync();
        try
        {
            return debuggers.Select(p => p with { }).ToArray();
        }
        finally
        {
            stateLock.Release();
        }
    }

    public async Task PairAsync(PairingInvitation invitation)
    {
        ArgumentNullException.ThrowIfNull(invitation);
        invitation.Validate();
        await EnsureInitializedAsync();
        if (!developerModeEnabled)
            throw new InvalidOperationException("Developer mode must be enabled before pairing a remote debugger.");
        try
        {
            await ConnectAsync(invitation, null);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(invitation.PairingSecret);
        }
    }

    public async Task ForgetDebuggerAsync(string debuggerId)
    {
        await EnsureInitializedAsync();
        if (connection?.Debugger.Id == debuggerId)
            await StopConnectionAndSessionsAsync();
        await stateLock.WaitAsync();
        try
        {
            debuggers.RemoveAll(p => p.Id == debuggerId);
            await SaveStateAsync();
        }
        finally
        {
            stateLock.Release();
        }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void AttachSessionHost(string sessionId, IRemoteDebugSessionHost host)
    {
        if (!sessions.TryGetValue(sessionId, out RemoteDebugSession? session))
            throw new InvalidOperationException($"Remote debug session was not found: {sessionId}");
        session.Host = host;
    }

    public void DetachSessionHost(string sessionId, IRemoteDebugSessionHost host)
    {
        if (sessions.TryGetValue(sessionId, out RemoteDebugSession? session) && ReferenceEquals(session.Host, host))
            session.Host = null;
    }

    public void NotifySessionHostClosed(string sessionId, IRemoteDebugSessionHost host)
    {
        if (!sessions.TryGetValue(sessionId, out RemoteDebugSession? session) || !ReferenceEquals(session.Host, host))
            return;
        sessions.TryRemove(sessionId, out _);
        session.RejectAll();
        _ = connection?.SendEventAsync("session.closed", new JsonObject { ["sessionId"] = sessionId });
    }

    public void RecordConsole(string sessionId, string level, JsonArray values)
    {
        if (sessions.TryGetValue(sessionId, out RemoteDebugSession? session))
            session.AddLog(level, values);
    }

    public void RecordDapiRequest(string sessionId, string method, JsonNode? parameters)
    {
        if (sessions.TryGetValue(sessionId, out RemoteDebugSession? session))
            session.AddTrace(method, "request", parameters);
    }

    public async Task<RemoteDebugApprovalResult> RequestDapiApprovalAsync(string sessionId, string method, JsonNode? parameters, CancellationToken cancellationToken = default)
    {
        if (!sessions.TryGetValue(sessionId, out RemoteDebugSession? session))
            return new RemoteDebugApprovalResult { Approved = false };
        Task<RemoteDebugApprovalResult> approval = session.RequestApprovalAsync(method, parameters, ApprovalTimeout, cancellationToken);
        await connection!.SendEventAsync("session.request.pending", new JsonObject
        {
            ["sessionId"] = sessionId,
            ["method"] = method
        });
        return await approval;
    }

    public void RecordDapiResponse(string sessionId, string method, JsonObject response)
    {
        if (!sessions.TryGetValue(sessionId, out RemoteDebugSession? session)) return;
        string phase = response.ContainsKey("error") ? "reject" : "resolve";
        session.AddTrace(method, phase, response[phase == "reject" ? "error" : "result"]);
    }

    async Task EnsureInitializedAsync()
    {
        if (initialized) return;
        await stateLock.WaitAsync();
        try
        {
            if (initialized) return;
            string? value = await SecureStorage.Default.GetAsync(SecureStorageKey);
            if (string.IsNullOrEmpty(value))
            {
                debugTargetIdentity = DebugIdentity.Create();
                debuggers = [];
                await SaveStateAsync();
            }
            else
            {
                RemoteDebugStateDocument state = JsonSerializer.Deserialize<RemoteDebugStateDocument>(value, RemoteDebugJson.Options)
                    ?? throw new InvalidDataException("The remote-debug trust document is invalid.");
                if (state.SchemaVersion != 1)
                    throw new NotSupportedException($"Unsupported remote-debug state version: {state.SchemaVersion}.");
                debugTargetIdentity = DebugIdentity.Import(Base64Url.Decode(state.DebugTargetPrivateKey));
                debuggers = state.Debuggers;
            }
            initialized = true;
        }
        finally
        {
            stateLock.Release();
        }
    }

    Task SaveStateAsync()
    {
        RemoteDebugStateDocument state = new()
        {
            DebugTargetPrivateKey = Base64Url.Encode(debugTargetIdentity!.PrivateKey),
            Debuggers = debuggers
        };
        return SecureStorage.Default.SetAsync(
            SecureStorageKey,
            JsonSerializer.Serialize(state, RemoteDebugJson.Options));
    }

    async Task StartDiscoveryAsync()
    {
        if (discovery is not null) return;
        discovery = new();
        discovery.RemoteDebuggerDiscovered += OnRemoteDebuggerDiscovered;
        try
        {
            await discovery.StartAsync();
        }
        catch
        {
            discovery.RemoteDebuggerDiscovered -= OnRemoteDebuggerDiscovered;
            await discovery.DisposeAsync();
            discovery = null;
        }
    }

    async Task StopDiscoveryAsync()
    {
        if (discovery is null) return;
        discovery.RemoteDebuggerDiscovered -= OnRemoteDebuggerDiscovered;
        await discovery.DisposeAsync();
        discovery = null;
    }

    void OnRemoteDebuggerDiscovered(string debuggerId, string host, int port)
    {
        if (!developerModeEnabled || connection is not null) return;
        TrustedRemoteDebugger? debugger = debuggers.FirstOrDefault(p => p.Id == debuggerId);
        if (debugger is null) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await ConnectAsync(null, (debugger, host, port));
            }
            catch
            {
            }
        });
    }

    async Task ConnectAsync(PairingInvitation? invitation, (TrustedRemoteDebugger Debugger, string Host, int Port)? trusted)
    {
        await connectLock.WaitAsync();
        try
        {
            if (!developerModeEnabled) return;
            if (connection is not null)
            {
                if (trusted?.Debugger.Id == connection.Debugger.Id) return;
                await StopConnectionAndSessionsAsync();
            }
            List<(string Host, int Port)> endpoints = invitation is not null
                ? invitation.Endpoints.Select(p => (p.Host, p.Port)).ToList()
                : [(trusted!.Value.Host, trusted.Value.Port)];
            Exception? lastError = null;
            foreach (var endpoint in endpoints)
            {
                try
                {
                    using CancellationTokenSource timeout = new(ConnectionTimeout);
                    TcpClient client = new();
                    await client.ConnectAsync(endpoint.Host, endpoint.Port, timeout.Token);
                    RemoteDebugConnection candidate = await RemoteDebugConnection.ConnectAsync(
                        client,
                        debugTargetIdentity!,
                        invitation,
                        trusted?.Debugger,
                        PersistDebuggerAsync,
                        HandleRequestAsync,
                        timeout.Token);
                    candidate.Disconnected += OnConnectionDisconnected;
                    connection = candidate;
                    candidate.Start();
                    StateChanged?.Invoke(this, EventArgs.Empty);
                    return;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
            }
            throw new IOException("Unable to connect to the remote debugger.", lastError);
        }
        finally
        {
            connectLock.Release();
        }
    }

    async Task PersistDebuggerAsync(TrustedRemoteDebugger debugger)
    {
        await stateLock.WaitAsync();
        try
        {
            debuggers.RemoveAll(p => p.Id == debugger.Id);
            debuggers.Add(debugger);
            await SaveStateAsync();
        }
        finally
        {
            stateLock.Release();
        }
    }

    void OnConnectionDisconnected(RemoteDebugConnection sender)
    {
        if (!ReferenceEquals(connection, sender)) return;
        sender.Disconnected -= OnConnectionDisconnected;
        connection = null;
        _ = StopAllSessionsAsync();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    async Task<JsonNode?> HandleRequestAsync(string method, JsonObject parameters)
    {
        return method switch
        {
            "session.start" => await StartSessionAsync(parameters),
            "session.status" => await RequireHost(parameters).GetRemoteStatusAsync(),
            "session.logs" => Logs(parameters),
            "session.trace" => Trace(parameters),
            "session.screenshot" => await ScreenshotAsync(parameters),
            "session.evaluate" => await EvaluateAsync(parameters),
            "session.reload" => await ReloadAsync(parameters),
            "session.stop" => await StopSessionAsync(parameters),
            "session.requests" => Requests(parameters),
            "session.request.approve" => ResolveRequest(parameters, true),
            "session.request.reject" => ResolveRequest(parameters, false),
            "session.operation" => await OperationAsync(parameters),
            _ => throw new RemoteDebugCommandException("METHOD_NOT_FOUND", $"Remote debug method was not found: {method}")
        };
    }

    async Task<JsonNode?> StartSessionAsync(JsonObject parameters)
    {
        string value = parameters["url"]?.GetValue<string>()
            ?? throw new RemoteDebugCommandException("INVALID_ARGUMENT", "url is required.");
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? url) || url.Scheme != Uri.UriSchemeHttps)
            throw new RemoteDebugCommandException("HTTPS_REQUIRED", "OneGate remote sessions require an HTTPS DApp URL.");
        string sessionId = Guid.NewGuid().ToString("N");
        RemoteDebugSession session = new(sessionId, url);
        if (!sessions.TryAdd(sessionId, session)) throw new InvalidOperationException();
        try
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                LaunchDAppPage page = serviceProvider.GetServiceOrCreateInstance<LaunchDAppPage>();
                page.ConfigureRemoteDebug(sessionId, this);
                page.ApplyQueryAttributes(new Dictionary<string, object>
                {
                    ["uri"] = url
                });
                Application.Current!.OpenWindow(new Window(new NavigationPage(page)));
                await Task.CompletedTask;
            });
            return new JsonObject
            {
                ["sessionId"] = sessionId,
                ["target"] = "onegate",
                ["debugTargetId"] = debugTargetIdentity!.KeyId,
                ["href"] = url.AbsoluteUri,
                ["origin"] = url.GetLeftPart(UriPartial.Authority),
                ["state"] = "starting"
            };
        }
        catch
        {
            sessions.TryRemove(sessionId, out _);
            throw;
        }
    }

    RemoteDebugSession GetSession(JsonObject parameters)
    {
        string id = parameters["sessionId"]?.GetValue<string>()
            ?? throw new RemoteDebugCommandException("INVALID_ARGUMENT", "sessionId is required.");
        return sessions.GetValueOrDefault(id)
            ?? throw new RemoteDebugCommandException("SESSION_NOT_FOUND", $"Remote debug session was not found: {id}");
    }

    IRemoteDebugSessionHost RequireHost(JsonObject parameters)
        => GetSession(parameters).Host
            ?? throw new RemoteDebugCommandException("SESSION_NOT_READY", "The DApp window is not ready.");

    JsonNode Logs(JsonObject parameters)
    {
        RemoteDebugSession session = GetSession(parameters);
        return new JsonObject
        {
            ["sessionId"] = session.Id,
            ["entries"] = session.GetLogs(parameters["afterSequence"]?.GetValue<long>() ?? 0).ToJsonArray()
        };
    }

    JsonNode Trace(JsonObject parameters)
    {
        RemoteDebugSession session = GetSession(parameters);
        return new JsonObject
        {
            ["sessionId"] = session.Id,
            ["entries"] = session.GetTrace().ToJsonArray()
        };
    }

    async Task<JsonNode?> ScreenshotAsync(JsonObject parameters)
    {
        byte[] data = await RequireHost(parameters).CaptureRemoteScreenshotAsync();
        return new JsonObject { ["mimeType"] = "image/png", ["data"] = Convert.ToBase64String(data) };
    }

    async Task<JsonNode?> EvaluateAsync(JsonObject parameters)
    {
        string expression = parameters["expression"]?.GetValue<string>()
            ?? throw new RemoteDebugCommandException("INVALID_ARGUMENT", "expression is required.");
        Task<JsonNode?> task = RequireHost(parameters).EvaluateRemoteAsync(expression);
        if (parameters["defer"]?.GetValue<bool>() != true)
            return new JsonObject { ["value"] = await task };
        string operationId = Guid.NewGuid().ToString("N");
        operations[operationId] = task;
        return new JsonObject { ["deferred"] = true, ["operationId"] = operationId };
    }

    async Task<JsonNode?> ReloadAsync(JsonObject parameters)
    {
        await RequireHost(parameters).ReloadRemoteAsync(parameters["ignoreCache"]?.GetValue<bool>() == true);
        return await RequireHost(parameters).GetRemoteStatusAsync();
    }

    async Task<JsonNode?> StopSessionAsync(JsonObject parameters)
    {
        RemoteDebugSession session = GetSession(parameters);
        sessions.TryRemove(session.Id, out _);
        session.RejectAll();
        if (session.Host is not null) await session.Host.StopRemoteAsync();
        return new JsonObject { ["sessionId"] = session.Id, ["stopped"] = true };
    }

    JsonNode Requests(JsonObject parameters)
    {
        RemoteDebugPendingRequest[] requests = GetSession(parameters).GetPendingRequests();
        return new JsonObject
        {
            ["sessionId"] = GetSession(parameters).Id,
            ["requests"] = JsonSerializer.SerializeToNode(requests, RemoteDebugJson.Options)
        };
    }

    JsonNode ResolveRequest(JsonObject parameters, bool approved)
    {
        string requestId = parameters["requestId"]?.GetValue<string>()
            ?? throw new RemoteDebugCommandException("INVALID_ARGUMENT", "requestId is required.");
        bool hasResult = approved && parameters.ContainsKey("result");
        JsonNode? result = hasResult ? parameters["result"]?.DeepClone() : null;
        if (!GetSession(parameters).ResolveRequest(requestId, approved, hasResult, result))
            throw new RemoteDebugCommandException("REQUEST_NOT_FOUND", $"Pending request was not found: {requestId}");
        JsonObject response = new() { ["requestId"] = requestId, ["approved"] = approved };
        if (hasResult) response["result"] = result?.DeepClone();
        return response;
    }

    async Task<JsonNode?> OperationAsync(JsonObject parameters)
    {
        string operationId = parameters["operationId"]?.GetValue<string>()
            ?? throw new RemoteDebugCommandException("INVALID_ARGUMENT", "operationId is required.");
        if (!operations.TryGetValue(operationId, out Task<JsonNode?>? operation))
            throw new RemoteDebugCommandException("OPERATION_NOT_FOUND", $"Deferred operation was not found: {operationId}");
        if (!operation.IsCompleted)
            return new JsonObject { ["operationId"] = operationId, ["state"] = "pending" };
        operations.TryRemove(operationId, out _);
        return new JsonObject { ["operationId"] = operationId, ["state"] = "completed", ["value"] = await operation };
    }

    async Task StopConnectionAndSessionsAsync()
    {
        RemoteDebugConnection? current = connection;
        connection = null;
        if (current is not null)
        {
            current.Disconnected -= OnConnectionDisconnected;
            await current.DisposeAsync();
        }
        await StopAllSessionsAsync();
    }

    async Task StopAllSessionsAsync()
    {
        RemoteDebugSession[] active = sessions.Values.ToArray();
        sessions.Clear();
        foreach (RemoteDebugSession session in active) session.RejectAll();
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            foreach (IRemoteDebugSessionHost host in active.Select(p => p.Host).OfType<IRemoteDebugSessionHost>())
                await host.StopRemoteAsync();
        });
    }

    public async ValueTask DisposeAsync()
    {
        await StopConnectionAndSessionsAsync();
        await StopDiscoveryAsync();
        debugTargetIdentity?.Dispose();
        stateLock.Dispose();
        connectLock.Dispose();
    }
}

sealed class RemoteDebugConnection : IAsyncDisposable
{
    readonly TcpClient client;
    readonly FramedStream framed;
    readonly SecureSession channel;
    readonly Func<string, JsonObject, Task<JsonNode?>> requestHandler;
    readonly SemaphoreSlim sendLock = new(1, 1);
    readonly CancellationTokenSource cancellation = new();
    Task? readLoop;

    RemoteDebugConnection(
        TcpClient client,
        FramedStream framed,
        SecureSession channel,
        TrustedRemoteDebugger debugger,
        Func<string, JsonObject, Task<JsonNode?>> requestHandler)
    {
        this.client = client;
        this.framed = framed;
        this.channel = channel;
        Debugger = debugger;
        this.requestHandler = requestHandler;
    }

    public TrustedRemoteDebugger Debugger { get; }
    public event Action<RemoteDebugConnection>? Disconnected;

    public static async Task<RemoteDebugConnection> ConnectAsync(
        TcpClient client,
        DebugIdentity debugTargetIdentity,
        PairingInvitation? invitation,
        TrustedRemoteDebugger? trusted,
        Func<TrustedRemoteDebugger, Task> persistDebugger,
        Func<string, JsonObject, Task<JsonNode?>> requestHandler,
        CancellationToken cancellationToken)
    {
        FramedStream framed = new(client.GetStream());
        using EphemeralKey ephemeral = new();
        JsonObject clientHello = new()
        {
            ["version"] = 1,
            ["mode"] = invitation is null ? "trusted" : "pair",
            ["pairingId"] = invitation?.PairingId.ToString("D"),
            ["debugTargetId"] = debugTargetIdentity.KeyId,
            ["debugTargetName"] = DeviceInfo.Name,
            ["platform"] = DeviceInfo.Platform.ToString(),
            ["identityKey"] = Base64Url.Encode(debugTargetIdentity.PublicKey),
            ["ephemeralKey"] = Base64Url.Encode(ephemeral.PublicKey),
            ["nonce"] = Base64Url.Encode(RandomNumberGenerator.GetBytes(16))
        };
        byte[] clientPayload = Encoding.UTF8.GetBytes(clientHello.ToJsonString(RemoteDebugJson.Options));
        await framed.WriteJsonAsync(new JsonObject
        {
            ["type"] = "clientHello",
            ["payload"] = Base64Url.Encode(clientPayload),
            ["signature"] = Base64Url.Encode(debugTargetIdentity.Sign(clientPayload))
        }, cancellationToken);
        JsonObject serverEnvelope = await framed.ReadJsonAsync<JsonObject>(cancellationToken);
        if (serverEnvelope["type"]?.GetValue<string>() != "serverHello")
            throw new InvalidDataException("The remote debugger sent an invalid server hello.");
        byte[] serverPayload = Base64Url.Decode(serverEnvelope["payload"]!.GetValue<string>());
        JsonObject serverHello = JsonNode.Parse(serverPayload)!.AsObject();
        byte[] debuggerPublicKey = Base64Url.Decode(serverHello["identityKey"]!.GetValue<string>());
        string debuggerId = Convert.ToHexStringLower(SHA256.HashData(debuggerPublicKey))[..16];
        if (debuggerId != serverHello["debuggerId"]?.GetValue<string>())
            throw new CryptographicException("The remote debugger identity id is invalid.");
        if (invitation is not null)
        {
            byte[] rawKey = DebugIdentity.ToUncompressedPublicKey(debuggerPublicKey);
            if (!CryptographicOperations.FixedTimeEquals(rawKey, invitation.DebuggerPublicKey))
                throw new CryptographicException("The remote debugger identity does not match the pairing QR code.");
        }
        else if (trusted is null || !CryptographicOperations.FixedTimeEquals(debuggerPublicKey, Base64Url.Decode(trusted.PublicKey)))
        {
            throw new CryptographicException("The discovered remote debugger identity is not trusted.");
        }
        byte[] transcriptHash = SHA256.HashData(Concat(clientPayload, serverPayload));
        byte[] serverSignature = Base64Url.Decode(serverEnvelope["signature"]!.GetValue<string>());
        if (!DebugIdentity.Verify(debuggerPublicKey, transcriptHash, serverSignature))
            throw new CryptographicException("The remote debugger handshake signature is invalid.");
        byte[] secret = invitation?.PairingSecret ?? Base64Url.Decode(trusted!.ReconnectSecret);
        SecureSession channel = SecureSession.Create(
            DebugPeerRole.DebugTarget,
            ephemeral.DeriveSecret(Base64Url.Decode(serverHello["ephemeralKey"]!.GetValue<string>())),
            secret,
            transcriptHash);
        if (invitation is null)
            CryptographicOperations.ZeroMemory(secret);
        JsonObject ready = JsonNode.Parse(channel.Decrypt(await framed.ReadAsync(cancellationToken)))!.AsObject();
        TrustedRemoteDebugger debugger;
        if (invitation is not null)
        {
            if (ready["kind"]?.GetValue<string>() != "pair.complete")
                throw new InvalidDataException("The remote debugger did not complete pairing.");
            debugger = new()
            {
                Id = debuggerId,
                Name = serverHello["debuggerName"]?.GetValue<string>() ?? invitation.DebuggerName ?? debuggerId,
                PublicKey = Base64Url.Encode(debuggerPublicKey),
                ReconnectSecret = ready["reconnectSecret"]!.GetValue<string>(),
                PairedAt = DateTimeOffset.UtcNow,
                LastConnectedAt = DateTimeOffset.UtcNow
            };
            await WriteSecureJsonAsync(framed, channel, new JsonObject { ["kind"] = "pair.ack" }, cancellationToken);
            await persistDebugger(debugger);
        }
        else
        {
            if (ready["kind"]?.GetValue<string>() != "ready")
                throw new InvalidDataException("The trusted remote debugger did not complete the handshake.");
            debugger = trusted! with { LastConnectedAt = DateTimeOffset.UtcNow };
            await WriteSecureJsonAsync(framed, channel, new JsonObject { ["kind"] = "ready.ack" }, cancellationToken);
            await persistDebugger(debugger);
        }
        return new(client, framed, channel, debugger, requestHandler);
    }

    public void Start()
    {
        readLoop = ReadLoopAsync(cancellation.Token);
        _ = readLoop.ContinueWith(_ => Disconnected?.Invoke(this), TaskScheduler.Default);
    }

    public Task SendEventAsync(string method, JsonObject parameters)
        => SendAsync(new JsonObject { ["kind"] = "event", ["method"] = method, ["params"] = parameters }, cancellation.Token);

    async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            byte[] encrypted = await framed.ReadAsync(cancellationToken);
            JsonObject message = JsonNode.Parse(channel.Decrypt(encrypted))!.AsObject();
            if (message["kind"]?.GetValue<string>() != "request")
                throw new InvalidDataException("The remote debugger sent an invalid secure message.");
            string id = message["id"]!.GetValue<string>();
            string method = message["method"]!.GetValue<string>();
            JsonObject parameters = message["params"] as JsonObject ?? new();
            _ = HandleRequestAsync(id, method, parameters, cancellationToken);
        }
    }

    async Task HandleRequestAsync(string id, string method, JsonObject parameters, CancellationToken cancellationToken)
    {
        JsonObject response = new() { ["kind"] = "response", ["id"] = id };
        try
        {
            response["result"] = await requestHandler(method, parameters);
        }
        catch (RemoteDebugCommandException ex)
        {
            response["error"] = new JsonObject { ["code"] = ex.Code, ["message"] = ex.Message };
        }
        catch (Exception ex)
        {
            response["error"] = new JsonObject { ["code"] = "DEBUG_TARGET_ERROR", ["message"] = ex.Message };
        }
        await SendAsync(response, cancellationToken);
    }

    async Task SendAsync(JsonObject message, CancellationToken cancellationToken)
    {
        await sendLock.WaitAsync(cancellationToken);
        try
        {
            await WriteSecureJsonAsync(framed, channel, message, cancellationToken);
        }
        finally
        {
            sendLock.Release();
        }
    }

    static Task WriteSecureJsonAsync(FramedStream framed, SecureSession channel, JsonObject value, CancellationToken cancellationToken)
    {
        byte[] payload = Encoding.UTF8.GetBytes(value.ToJsonString(RemoteDebugJson.Options));
        return framed.WriteAsync(channel.Encrypt(payload), cancellationToken);
    }

    static byte[] Concat(byte[] first, byte[] second)
    {
        byte[] result = new byte[first.Length + second.Length];
        first.CopyTo(result, 0);
        second.CopyTo(result, first.Length);
        return result;
    }

    public async ValueTask DisposeAsync()
    {
        cancellation.Cancel();
        client.Close();
        if (readLoop is not null)
        {
            try { await readLoop; } catch { }
        }
        channel.Dispose();
        await framed.DisposeAsync();
        sendLock.Dispose();
        cancellation.Dispose();
    }
}

sealed class RemoteDebugCommandException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

static class RemoteDebugJsonExtensions
{
    public static JsonArray ToJsonArray(this IEnumerable<JsonObject> values)
        => new(values.Select(p => (JsonNode)p).ToArray());
}
