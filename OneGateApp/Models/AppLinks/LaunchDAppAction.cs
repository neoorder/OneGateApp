using NeoOrder.OneGate.Pages;
using NeoOrder.OneGate.Services;

namespace NeoOrder.OneGate.Models.AppLinks;

class LaunchDAppAction : AppLinkAction
{
    readonly Uri uri;

    public LaunchDAppAction(Uri uri)
    {
        if (!DAppLaunchUri.TryGetAppId(uri, out int appId))
            throw new ArgumentException("Invalid dApp URI.", nameof(uri));
        this.uri = uri;
        AppId = appId;
    }

    protected override string Route => "//dapps/launch";
    public int AppId { get; }

    protected override Page CreatePage(IServiceProvider serviceProvider)
    {
        return serviceProvider.GetServiceOrCreateInstance<LaunchDAppPage>();
    }

    protected override IDictionary<string, object> CreateQuery()
    {
        return new Dictionary<string, object>
        {
            ["uri"] = uri
        };
    }
}
