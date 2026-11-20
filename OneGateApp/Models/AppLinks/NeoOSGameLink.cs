namespace NeoOrder.OneGate.Models.AppLinks;

/// <summary>
/// Recognizes NeoOS game play URLs. The NeoOS web host owns the only /play
/// surface (https://apps.neoos.network/play/&lt;appId&gt;?network=...), and every
/// NeoOS game release ships the NEP-21 dAPI platform bridge, so opening such a
/// link in the built-in dApp browser gives the game wallet access through the
/// standard dAPI. The allowlist is deliberately limited to that host: scanned
/// https links otherwise keep opening in the system browser, which keeps
/// arbitrary scanned URLs away from the wallet dAPI.
/// </summary>
static class NeoOSGameLink
{
    public const string GameHost = "apps.neoos.network";
    public const string PlayPath = "/play/";

    public static bool IsGamePlayUrl(Uri uri)
    {
        return uri.Scheme == "https"
            && uri.Authority.Equals(GameHost, StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.StartsWith(PlayPath, StringComparison.Ordinal)
            && uri.AbsolutePath.Length > PlayPath.Length;
    }
}
