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
            "https" => ProcessHttpsScheme(uri),
            _ => null
        };
    }

    static AppLinkAction? ProcessHttpsScheme(Uri uri)
    {
        if (!string.Equals(uri.Host, SharedOptions.OneGateDomain, StringComparison.OrdinalIgnoreCase))
            return null;
        return uri.Segments switch
        {
            ["/", "app/", _] => LaunchDAppAction.TryCreate(uri),
            ["/", "news/", _] => ViewNewsAction.TryCreate(uri),
            _ => null
        };
    }
}
