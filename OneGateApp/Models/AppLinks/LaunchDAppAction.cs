using NeoOrder.OneGate.Pages;
using NeoOrder.OneGate.Services;

namespace NeoOrder.OneGate.Models.AppLinks;

class LaunchDAppAction : AppLinkAction
{
    protected override string Route => "launch";
    public Uri Uri { get; }
    public int AppId { get; }

    public LaunchDAppAction(Uri uri)
    {
        if (!uri.IsAbsoluteUri)
            throw new ArgumentException("URI must be absolute.", nameof(uri));
        if (uri.Scheme != "https")
            throw new ArgumentException("Invalid scheme for LaunchDAppAction", nameof(uri));
        if (uri.Authority != SharedOptions.OneGateDomain)
            throw new ArgumentException("Invalid authority for LaunchDAppAction", nameof(uri));
        if (uri.Segments is not ["/", "app/", var appIdSegment])
            throw new ArgumentException("Invalid path for LaunchDAppAction", nameof(uri));
        this.Uri = uri;
        AppId = int.Parse(appIdSegment);
        if (AppId <= 0)
            throw new ArgumentException("Invalid app id", nameof(uri));
    }

    protected override Page CreatePage(IServiceProvider serviceProvider)
    {
        return serviceProvider.GetServiceOrCreateInstance<LaunchDAppPage>();
    }

    protected override IDictionary<string, object> CreateQuery()
    {
        return new Dictionary<string, object>
        {
            ["uri"] = Uri
        };
    }

    public static new LaunchDAppAction? TryCreate(string? uri)
    {
        if (string.IsNullOrEmpty(uri)) return null;
        if (Uri.TryCreate(uri, UriKind.Absolute, out var result))
            return TryCreate(result);
        return null;
    }

    public static new LaunchDAppAction? TryCreate(Uri? uri)
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
