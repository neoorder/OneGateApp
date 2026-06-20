using NeoOrder.OneGate.Pages;
using NeoOrder.OneGate.Services;

namespace NeoOrder.OneGate.Models.AppLinks;

class LaunchDAppAction : AppLinkAction
{
    public LaunchDAppAction(Uri uri)
    {
        if (!DAppLaunchUri.TryGetAppId(uri, out int appId))
            throw new ArgumentException("Invalid dApp URI.", nameof(uri));
        Uri = uri;
        AppId = appId;
    }

    protected override string Route => "launch";
    public Uri Uri { get; }
    public int AppId { get; }

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
