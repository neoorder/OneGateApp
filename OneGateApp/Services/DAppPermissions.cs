namespace NeoOrder.OneGate.Services;

public sealed class DAppPermissionGrant
{
    public required string Host { get; init; }
    public int? DAppId { get; init; }
    public required string[] Scopes { get; init; }
    public required DateTimeOffset GrantedAt { get; init; }
    public required DateTimeOffset LastUsedAt { get; init; }
}

public static class DAppPermissions
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);
    public static readonly string[] DefaultScopes =
    [
        "read:accounts",
        "read:blockchain",
        "request:transactions",
        "request:signatures"
    ];

    static readonly HashSet<string> MethodsRequiringConnection = new(StringComparer.OrdinalIgnoreCase)
    {
        "authenticate",
        "getAccounts",
        "pickAddress",
        "getBalance",
        "send",
        "invoke",
        "makeTransaction",
        "sign",
        "signMessage",
        "relay"
    };

    public static bool RequiresConnection(string? method)
    {
        return !string.IsNullOrWhiteSpace(method) && MethodsRequiringConnection.Contains(method);
    }

    public static string NormalizeHost(string host)
    {
        string normalized = host.Trim().TrimEnd('.').ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Contains('/') ||
            Uri.CheckHostName(normalized) == UriHostNameType.Unknown)
            throw new ArgumentException("Invalid dApp host.", nameof(host));
        return normalized;
    }

    public static string SettingsKeyForHost(string host)
    {
        return $"dapps/permissions/{NormalizeHost(host)}";
    }

    public static bool IsFresh(DAppPermissionGrant? grant, DateTimeOffset now)
    {
        return grant is not null && grant.GrantedAt <= now && now - grant.GrantedAt <= Lifetime;
    }

    public static DAppPermissionGrant CreateGrant(string host, int? dAppId, DateTimeOffset now)
    {
        return new DAppPermissionGrant
        {
            Host = NormalizeHost(host),
            DAppId = dAppId,
            Scopes = [.. DefaultScopes],
            GrantedAt = now,
            LastUsedAt = now
        };
    }

    public static DAppPermissionGrant Touch(DAppPermissionGrant grant, DateTimeOffset now)
    {
        return new DAppPermissionGrant
        {
            Host = NormalizeHost(grant.Host),
            DAppId = grant.DAppId,
            Scopes = [.. grant.Scopes],
            GrantedAt = grant.GrantedAt,
            LastUsedAt = now
        };
    }
}
