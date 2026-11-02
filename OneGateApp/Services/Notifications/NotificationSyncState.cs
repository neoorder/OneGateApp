namespace NeoOrder.OneGate.Services.Notifications;

/// <summary>Arms local alerts after catch-up and invalidates queued alerts across reconnects.</summary>
internal sealed class NotificationSyncState
{
    readonly Dictionary<string, long> ready = new(StringComparer.Ordinal);
    long generation;

    internal void CaughtUp(string resource)
    {
        lock (ready)
            if (!ready.ContainsKey(resource)) ready[resource] = ++generation;
    }

    internal void Pause(string resource) { lock (ready) ready.Remove(resource); }
    internal void Reset() { lock (ready) ready.Clear(); }
    internal long? Capture(string resource) { lock (ready) return ready.TryGetValue(resource, out long value) ? value : null; }
    internal bool IsCurrent(string resource, long value) => Capture(resource) == value;
}
