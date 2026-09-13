namespace NeoOrder.OneGate.Services.RemoteDebug;

sealed class RemoteDebugOperationStore<T> : IDisposable
{
    sealed class Entry
    {
        public Entry(string sessionId)
        {
            SessionId = sessionId;
            Token = Cancellation.Token;
        }
        public string SessionId { get; }
        public TaskCompletionSource<T> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationTokenSource Cancellation { get; } = new(Lifetime);
        public CancellationToken Token { get; }
        public DateTimeOffset ExpiresAt { get; } = DateTimeOffset.UtcNow + Lifetime;
    }
    readonly object gate = new();
    readonly Dictionary<string, Entry> entries = new(StringComparer.Ordinal);
    readonly HashSet<string> removingSessions = new(StringComparer.Ordinal);
    static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(2);
    readonly int maximumOperations;
    readonly Timer cleanup;
    bool clearing;
    bool disposed;

    public RemoteDebugOperationStore(int maximumOperations = 64)
    {
        this.maximumOperations = maximumOperations;
        cleanup = new Timer(_ => RemoveExpired(), null, Lifetime, Lifetime);
    }

    public string Start(string sessionId, Func<CancellationToken, Task<T>> start)
    {
        ArgumentNullException.ThrowIfNull(start);
        RemoveExpired();
        string id = Guid.NewGuid().ToString("N");
        Entry entry;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (clearing || removingSessions.Contains(sessionId))
                throw new InvalidOperationException("The deferred operation session is stopping.");
            if (entries.Count >= maximumOperations)
                throw new InvalidOperationException("Too many deferred operations; collect or stop existing operations first.");
            entry = new(sessionId);
            entries.Add(id, entry);
        }
        // Reserve capacity first, but never execute host code while holding the
        // registry lock. The completion proxy exists even during synchronous start.
        _ = entry.Completion.Task.ContinueWith(t => _ = t.Exception,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        try
        {
            entry.Token.ThrowIfCancellationRequested();
            Task<T> task = start(entry.Token);
            _ = task.ContinueWith(t => _ = t.Exception,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
            _ = CompleteAsync(entry, task);
            return id;
        }
        catch (Exception ex)
        {
            bool ownsEntry;
            lock (gate) ownsEntry = entries.Remove(id);
            if (ex is OperationCanceledException)
                entry.Completion.TrySetCanceled();
            else
                entry.Completion.TrySetException(ex);
            if (ownsEntry) entry.Cancellation.Dispose();
            throw;
        }
    }

    static async Task CompleteAsync(Entry entry, Task<T> task)
    {
        try { entry.Completion.TrySetResult(await task.WaitAsync(entry.Token)); }
        catch (OperationCanceledException) { entry.Completion.TrySetCanceled(); }
        catch (Exception ex) { entry.Completion.TrySetException(ex); }
    }

    public bool TryGet(string sessionId, string id, out Task<T>? operation)
    {
        RemoveExpired();
        lock (gate)
        {
            operation = null;
            if (!entries.TryGetValue(id, out var entry) || entry.SessionId != sessionId) return false;
            operation = entry.Completion.Task;
            if (operation.IsCompleted)
            {
                entries.Remove(id);
                entry.Cancellation.Dispose();
            }
            return true;
        }
    }

    public void RemoveSession(string sessionId)
    {
        Entry[] removed;
        lock (gate)
        {
            if (!removingSessions.Add(sessionId)) return;
            removed = TakeEntries(p => p.SessionId == sessionId);
        }
        try { Cancel(removed); }
        finally { lock (gate) removingSessions.Remove(sessionId); }
    }

    void RemoveExpired()
    {
        Entry[] removed;
        lock (gate) removed = TakeEntries(p => p.ExpiresAt <= DateTimeOffset.UtcNow);
        Cancel(removed);
    }

    // Caller holds gate. Cancellation and its arbitrary callbacks happen later.
    Entry[] TakeEntries(Func<Entry, bool> predicate)
    {
        var removed = entries.Where(p => predicate(p.Value)).ToArray();
        foreach (var pair in removed) entries.Remove(pair.Key);
        return removed.Select(p => p.Value).ToArray();
    }

    static void Cancel(Entry[] removed)
    {
        foreach (var entry in removed)
        {
            entry.Completion.TrySetCanceled();
            try { entry.Cancellation.Cancel(); }
            // A failing consumer callback must not prevent cleanup of the rest.
            catch (AggregateException) { }
            finally { entry.Cancellation.Dispose(); }
        }
    }

    public void Clear()
    {
        Entry[] removed;
        lock (gate)
        {
            if (clearing) return;
            clearing = true;
            removed = TakeEntries(_ => true);
        }
        try { Cancel(removed); }
        finally { lock (gate) clearing = false; }
    }

    public void Dispose()
    {
        Entry[] removed;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            removed = TakeEntries(_ => true);
        }
        cleanup.Dispose();
        Cancel(removed);
    }
}
