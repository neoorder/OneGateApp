using NeoOrder.OneGate.Pages;
using NeoOrder.OneGate.Services;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;

namespace NeoOrder.OneGate.Models.AppLinks;

partial class AuthenticationAction : AppLinkAction
{
    [GeneratedRegex(@"^[a-z][a-z0-9]*(\.[a-z][a-z0-9]*)+$")]
    static partial Regex IdentifierRegex { get; }

    protected override string Route => "authenticate";
    public string Host { get; }
    public string Method { get; }
    public string DAppIdentifier { get; }
    public AuthenticationChallengePayload Payload { get; }

    public AuthenticationAction(Uri uri)
    {
        if (!uri.IsAbsoluteUri)
            throw new ArgumentException("URI must be absolute.", nameof(uri));
        if (uri.Scheme != "neoauth")
            throw new ArgumentException("Invalid scheme for AuthenticationAction", nameof(uri));
        if (uri.Authority != "wallet" && uri.Authority != SharedOptions.OneGateDomain)
            throw new ArgumentException("Invalid host for authentication action.", nameof(uri));
        Host = uri.Host;
        if (uri.LocalPath != "/authenticate")
            throw new ArgumentException("Invalid path for authentication action.", nameof(uri));
        Method = uri.LocalPath[1..];
        var nv = HttpUtility.ParseQueryString(uri.Query);
        if (nv["dapp"] is not string s_dapp || !IdentifierRegex.IsMatch(s_dapp))
            throw new ArgumentException("Invalid or missing dapp identifier for authentication action.", nameof(uri));
        DAppIdentifier = s_dapp;
        if (nv["payload"] is not string s_payload)
            throw new ArgumentException("Missing payload for authentication action.", nameof(uri));
        Payload = JsonSerializer.Deserialize<AuthenticationChallengePayload>(s_payload, SharedOptions.JsonSerializerOptions)
            ?? throw new ArgumentException("Invalid payload for authentication action.", nameof(uri));
    }

    protected override Page CreatePage(IServiceProvider serviceProvider)
    {
        return serviceProvider.GetServiceOrCreateInstance<AuthenticatePage>();
    }

    protected override IDictionary<string, object>? CreateQuery()
    {
        return new Dictionary<string, object>
        {
            ["dapp"] = DAppIdentifier,
            ["payload"] = Payload
        };
    }

    public static new AuthenticationAction? TryCreate(string? uri)
    {
        if (string.IsNullOrEmpty(uri)) return null;
        if (Uri.TryCreate(uri, UriKind.Absolute, out var result))
            return TryCreate(result);
        return null;
    }

    public static new AuthenticationAction? TryCreate(Uri? uri)
    {
        if (uri is null) return null;
        try
        {
            return new(uri);
        }
        catch
        {
            return null;
        }
    }
}
