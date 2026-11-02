using System.Security.Cryptography;
using System.Text.Json;

namespace NeoOrder.OneGate.Services.Notifications;

/// <summary>Shares message identity between remote push and local desktop notifications.</summary>
internal sealed class PushNotificationHistory
{
    internal static PushNotificationHistory Shared { get; } = new();
    static readonly string[] identityKeys = ["og_network", "og_to", "og_device", "og_from", "og_message"];
    readonly HashSet<string> entries = [];
    readonly Queue<string> order = [];

    internal static string? GetIdentifier(IReadOnlyDictionary<string, string>? data)
    {
        if (data is null || !data.TryGetValue("og_v", out var version) || version != "1") return null;
        var values = new string[identityKeys.Length];
        for (int i = 0; i < values.Length; i++)
            if (!data.TryGetValue(identityKeys[i], out values[i]!) || string.IsNullOrEmpty(values[i])) return null;
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(values)));
    }

    internal bool TryClaim(string? identifier)
    {
        if (identifier is null) return true; // Manual test notifications have no message identity.
        lock (entries)
        {
            if (!entries.Add(identifier)) return false;
            order.Enqueue(identifier);
            if (order.Count > 128) entries.Remove(order.Dequeue());
            return true;
        }
    }

    internal void Forget(string? identifier)
    {
        if (identifier is null) return;
        lock (entries)
        {
            entries.Remove(identifier);
            // A failed presentation can be retried without leaving an older eviction entry behind.
            var keep = order.Where(value => value != identifier).ToArray();
            order.Clear();
            foreach (string value in keep) order.Enqueue(value);
        }
    }
}
