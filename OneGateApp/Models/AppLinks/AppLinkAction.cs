using NeoOrder.OneGate.Services;

namespace NeoOrder.OneGate.Models.AppLinks;

abstract class AppLinkAction
{
    protected abstract string Route { get; }

    protected abstract Page CreatePage(IServiceProvider serviceProvider);

    protected virtual IDictionary<string, object>? CreateQuery() => null;

    public Page GetPage(IServiceProvider serviceProvider)
    {
        Page page = CreatePage(serviceProvider);
        if (page is IQueryAttributable attributable && CreateQuery() is IDictionary<string, object> query)
            attributable.ApplyQueryAttributes(query);
        return new NavigationPage(page);
    }

    public async Task GotoRoute(Shell shell)
    {
        if (CreateQuery() is IDictionary<string, object> query)
            await shell.GoToAsync(Route, query);
        else
            await shell.GoToAsync(Route);
    }

    public static AppLinkAction? TryCreate(string? uri)
    {
        if (string.IsNullOrEmpty(uri)) return null;
        if (Uri.TryCreate(uri, UriKind.Absolute, out var result))
            return TryCreate(result);
        return null;
    }

    public static AppLinkAction? TryCreate(Uri? uri)
    {
        if (uri is null) return null;
        if (!uri.IsAbsoluteUri) return null;
        return uri.Scheme switch
        {
            "neo" => PaymentAction.TryCreate(uri),
            "neoauth" => AuthenticationAction.TryCreate(uri),
            SharedOptions.OneGateScheme => ProcessOneGateScheme(uri),
            "https" => ProcessHttpsScheme(uri),
            _ => null
        };
    }

    static AppLinkAction? ProcessOneGateScheme(Uri uri)
    {
        if (!TryNormalizeOneGateUri(uri, out Uri? webUri)) return null;
        return ProcessHttpsScheme(webUri);
    }

    static AppLinkAction? ProcessHttpsScheme(Uri uri)
    {
        if (uri.Authority != SharedOptions.OneGateDomain) return null;
        return uri.Segments switch
        {
            ["/", "app/", _] => LaunchDAppAction.TryCreate(uri),
            ["/", "news/", _] => ViewNewsAction.TryCreate(uri),
            _ => null
        };
    }

    static bool TryNormalizeOneGateUri(Uri uri, out Uri webUri)
    {
        webUri = null!;
        string path = uri.Authority switch
        {
            "" => uri.AbsolutePath,
            SharedOptions.OneGateDomain => uri.AbsolutePath,
            _ => $"/{uri.Authority}{uri.AbsolutePath}"
        };

        if (string.IsNullOrWhiteSpace(path) || path == "/") return false;

        var builder = new UriBuilder(Uri.UriSchemeHttps, SharedOptions.OneGateDomain)
        {
            Path = path.TrimStart('/'),
            Query = uri.Query.TrimStart('?'),
            Fragment = uri.Fragment.TrimStart('#')
        };
        webUri = builder.Uri;
        return true;
    }
}
