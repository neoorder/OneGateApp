namespace NeoOrder.OneGate.Services;

static class DAppLaunchUri
{
    public static bool TryGetAppId(Uri uri, out int appId)
    {
        appId = 0;
        if (!uri.IsAbsoluteUri) return false;
        if (!string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals(uri.Host, SharedOptions.OneGateDomain, StringComparison.OrdinalIgnoreCase)) return false;
        if (uri.Segments.Length != 3) return false;
        if (!string.Equals(uri.Segments[1], "app/", StringComparison.Ordinal)) return false;
        return int.TryParse(uri.Segments[2], out appId) && appId > 0;
    }

    public static bool HasLaunchParameters(Uri uri)
    {
        return !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment);
    }

    public static string ApplyLaunchParameters(string dappUrl, Uri launchUri)
    {
        if (!HasLaunchParameters(launchUri)) return dappUrl;

        UriBuilder builder = new(dappUrl);
        if (!string.IsNullOrEmpty(launchUri.Query))
        {
            string baseQuery = builder.Query.TrimStart('?');
            string launchQuery = launchUri.Query.TrimStart('?');
            builder.Query = string.IsNullOrEmpty(baseQuery)
                ? launchQuery
                : $"{baseQuery}&{launchQuery}";
        }
        if (!string.IsNullOrEmpty(launchUri.Fragment))
            builder.Fragment = launchUri.Fragment.TrimStart('#');
        return builder.Uri.AbsoluteUri;
    }
}
