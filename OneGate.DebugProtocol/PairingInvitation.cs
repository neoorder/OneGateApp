using System.Collections.ObjectModel;

namespace NeoOrder.OneGate.DebugProtocol;

public sealed class PairingInvitation
{
    public const int CurrentVersion = 1;
    public const string Scheme = "onegate-debug";

    public required Guid PairingId { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public required byte[] DebuggerPublicKey { get; init; }
    public required byte[] PairingSecret { get; init; }
    public required IReadOnlyList<Uri> Endpoints { get; init; }
    public string? DebuggerName { get; init; }

    public Uri ToUri()
    {
        Validate();
        List<string> query =
        [
            $"v={CurrentVersion}",
            $"id={PairingId:D}",
            $"expires={ExpiresAt.ToUnixTimeSeconds()}",
            $"debuggerKey={Base64Url.Encode(DebuggerPublicKey)}",
            $"secret={Base64Url.Encode(PairingSecret)}"
        ];
        if (!string.IsNullOrWhiteSpace(DebuggerName))
            query.Add($"debuggerName={Uri.EscapeDataString(DebuggerName)}");
        query.AddRange(Endpoints.Select(p => $"endpoint={Uri.EscapeDataString(p.AbsoluteUri)}"));
        return new Uri($"{Scheme}://pair?{string.Join('&', query)}");
    }

    public void Validate(DateTimeOffset? now = null)
    {
        if (PairingId == Guid.Empty)
            throw new InvalidOperationException("Pairing id is required.");
        if (DebuggerPublicKey.Length != 65 || DebuggerPublicKey[0] != 0x04)
            throw new InvalidOperationException("Remote debugger public key is invalid.");
        if (PairingSecret.Length != 32)
            throw new InvalidOperationException("Pairing secret must be 32 bytes.");
        if (Endpoints.Count == 0 || Endpoints.Any(p => !p.IsAbsoluteUri || p.Scheme != "tcp"))
            throw new InvalidOperationException("At least one absolute TCP endpoint is required.");
        if (ExpiresAt <= (now ?? DateTimeOffset.UtcNow))
            throw new InvalidOperationException("Pairing invitation has expired.");
    }

    public static PairingInvitation Parse(string value, DateTimeOffset? now = null)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Scheme
            || uri.Host != "pair")
            throw new FormatException("Invalid OneGate debug pairing URI.");

        Dictionary<string, List<string>> query = ParseQuery(uri.Query);
        int version = int.Parse(Required(query, "v"), System.Globalization.CultureInfo.InvariantCulture);
        if (version != CurrentVersion)
            throw new NotSupportedException($"Unsupported pairing protocol version: {version}.");

        PairingInvitation invitation = new()
        {
            PairingId = Guid.Parse(Required(query, "id")),
            ExpiresAt = DateTimeOffset.FromUnixTimeSeconds(long.Parse(Required(query, "expires"), System.Globalization.CultureInfo.InvariantCulture)),
            DebuggerPublicKey = Base64Url.Decode(Required(query, "debuggerKey")),
            PairingSecret = Base64Url.Decode(Required(query, "secret")),
            DebuggerName = Optional(query, "debuggerName"),
            Endpoints = new ReadOnlyCollection<Uri>(query.GetValueOrDefault("endpoint", []).Select(p => new Uri(p, UriKind.Absolute)).ToArray())
        };
        invitation.Validate(now);
        return invitation;
    }

    static Dictionary<string, List<string>> ParseQuery(string query)
    {
        Dictionary<string, List<string>> result = new(StringComparer.Ordinal);
        foreach (string item in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pair = item.Split('=', 2);
            string key = Uri.UnescapeDataString(pair[0]);
            string value = pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : string.Empty;
            if (!result.TryGetValue(key, out List<string>? values))
                result[key] = values = [];
            values.Add(value);
        }
        return result;
    }

    static string Required(Dictionary<string, List<string>> query, string key)
    {
        if (!query.TryGetValue(key, out List<string>? values) || values.Count != 1 || string.IsNullOrWhiteSpace(values[0]))
            throw new FormatException($"Pairing URI field is missing or repeated: {key}.");
        return values[0];
    }

    static string? Optional(Dictionary<string, List<string>> query, string key)
    {
        if (!query.TryGetValue(key, out List<string>? values)) return null;
        if (values.Count != 1)
            throw new FormatException($"Pairing URI field is repeated: {key}.");
        return values[0];
    }
}
