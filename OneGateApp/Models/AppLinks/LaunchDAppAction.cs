using NeoOrder.OneGate.Pages;
using NeoOrder.OneGate.Services;

namespace NeoOrder.OneGate.Models.AppLinks;

class LaunchDAppAction(Uri uri) : AppLinkAction
{
    protected override string Route => "launch";
    public int AppId { get; } = int.Parse(uri.Segments[2]);

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
