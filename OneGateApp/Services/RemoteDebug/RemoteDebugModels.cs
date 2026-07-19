using System.Text.Json.Nodes;

namespace NeoOrder.OneGate.Services.RemoteDebug;

public sealed record TrustedRemoteDebugger
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string PublicKey { get; init; }
    public required string ReconnectSecret { get; init; }
    public required DateTimeOffset PairedAt { get; init; }
    public DateTimeOffset? LastConnectedAt { get; init; }
}

sealed record RemoteDebugStateDocument
{
    public int SchemaVersion { get; init; } = 1;
    public required string DebugTargetPrivateKey { get; init; }
    public List<TrustedRemoteDebugger> Debuggers { get; init; } = [];
}

public sealed record RemoteDebugPendingRequest
{
    public required string Id { get; init; }
    public required string Method { get; init; }
    public required JsonNode? Parameters { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed record RemoteDebugApprovalResult
{
    public required bool Approved { get; init; }
    public bool HasResult { get; init; }
    public JsonNode? Result { get; init; }
}

public interface IRemoteDebugSessionHost
{
    Task<JsonObject> GetRemoteStatusAsync();
    Task<JsonNode?> EvaluateRemoteAsync(string expression);
    Task<byte[]> CaptureRemoteScreenshotAsync();
    Task ReloadRemoteAsync(bool ignoreCache);
    Task StopRemoteAsync();
}

sealed class RemoteDebugSession(string id, Uri url)
{
    readonly object syncRoot = new();
    readonly List<JsonObject> logs = [];
    readonly List<JsonObject> trace = [];
    readonly Dictionary<string, (RemoteDebugPendingRequest Request, TaskCompletionSource<RemoteDebugApprovalResult> Completion)> pending = new(StringComparer.Ordinal);
    long nextLogSequence;
    long nextTraceSequence;

    public string Id { get; } = id;
    public Uri Url { get; } = url;
    public IRemoteDebugSessionHost? Host { get; set; }

    public JsonObject[] GetLogs(long afterSequence)
    {
        lock (syncRoot)
            return logs.Where(p => p["sequence"]?.GetValue<long>() > afterSequence).Select(p => (JsonObject)p.DeepClone()).ToArray();
    }

    public JsonObject[] GetTrace()
    {
        lock (syncRoot)
            return trace.Select(p => (JsonObject)p.DeepClone()).ToArray();
    }

    public void AddLog(string level, JsonArray values)
    {
        lock (syncRoot)
        {
            logs.Add(new JsonObject
            {
                ["sequence"] = ++nextLogSequence,
                ["timestamp"] = DateTimeOffset.UtcNow,
                ["level"] = level,
                ["message"] = string.Join(' ', values.Select(p => p?.ToJsonString() ?? "null"))
            });
            Trim(logs);
        }
    }

    public void AddTrace(string method, string phase, JsonNode? detail = null)
    {
        lock (syncRoot)
        {
            trace.Add(new JsonObject
            {
                ["sequence"] = ++nextTraceSequence,
                ["timestamp"] = DateTimeOffset.UtcNow,
                ["method"] = method,
                ["phase"] = phase,
                ["detail"] = detail?.DeepClone()
            });
            Trim(trace);
        }
    }

    public async Task<RemoteDebugApprovalResult> RequestApprovalAsync(string method, JsonNode? parameters, TimeSpan timeout, CancellationToken cancellationToken)
    {
        string requestId = Guid.NewGuid().ToString("N");
        TaskCompletionSource<RemoteDebugApprovalResult> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        RemoteDebugPendingRequest request = new()
        {
            Id = requestId,
            Method = method,
            Parameters = parameters?.DeepClone(),
            CreatedAt = DateTimeOffset.UtcNow
        };
        lock (syncRoot)
            pending.Add(requestId, (request, completion));
        AddTrace(method, "pending", new JsonObject { ["requestId"] = requestId });
        try
        {
            return await completion.Task.WaitAsync(timeout, cancellationToken);
        }
        finally
        {
            lock (syncRoot)
                pending.Remove(requestId);
        }
    }

    public RemoteDebugPendingRequest[] GetPendingRequests()
    {
        lock (syncRoot)
            return pending.Values.Select(p => p.Request with { Parameters = p.Request.Parameters?.DeepClone() }).ToArray();
    }

    public bool ResolveRequest(string requestId, bool approved, bool hasResult = false, JsonNode? result = null)
    {
        (RemoteDebugPendingRequest Request, TaskCompletionSource<RemoteDebugApprovalResult> Completion) entry;
        lock (syncRoot)
            if (!pending.TryGetValue(requestId, out entry)) return false;
        RemoteDebugApprovalResult approval = new()
        {
            Approved = approved,
            HasResult = approved && hasResult,
            Result = approved && hasResult ? result?.DeepClone() : null
        };
        JsonObject detail = new() { ["requestId"] = requestId };
        if (approval.HasResult) detail["result"] = approval.Result?.DeepClone();
        AddTrace(entry.Request.Method, approved ? "approved" : "rejected", detail);
        return entry.Completion.TrySetResult(approval);
    }

    public void RejectAll()
    {
        (RemoteDebugPendingRequest Request, TaskCompletionSource<RemoteDebugApprovalResult> Completion)[] entries;
        lock (syncRoot)
            entries = pending.Values.ToArray();
        foreach (var entry in entries)
            entry.Completion.TrySetResult(new RemoteDebugApprovalResult { Approved = false });
    }

    static void Trim(List<JsonObject> entries)
    {
        const int maximumEntries = 1_000;
        if (entries.Count > maximumEntries)
            entries.RemoveRange(0, entries.Count - maximumEntries);
    }
}
