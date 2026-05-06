using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Pages;
using NeoOrder.OneGate.Services;

namespace NeoOrder.OneGate.Controls.Handlers;

partial class DAppSearchHandler : SearchHandler
{
    public readonly static BindableProperty DAppsProperty = BindableProperty.Create(nameof(DApps), typeof(IList<DApp>), typeof(DAppSearchHandler));
    Uri? launchUri;
    int? launchAppId;

    public IList<DApp>? DApps
    {
        get => (IList<DApp>?)GetValue(DAppsProperty);
        set => SetValue(DAppsProperty, value);
    }

    protected override void OnQueryChanged(string oldValue, string newValue)
    {
        ItemsSource = Search(newValue, DApps, out launchUri, out launchAppId)?.ToArray();
#if IOS
        if (string.IsNullOrEmpty(newValue)) HideSoftInputAsync();
#endif
    }

    static IEnumerable<DApp>? Search(string? keyword, IEnumerable<DApp>? dapps, out Uri? launchUri, out int? launchAppId)
    {
        launchUri = null;
        launchAppId = null;
        if (string.IsNullOrWhiteSpace(keyword)) return null;
        if (dapps is null) return null;
        if (Uri.TryCreate(keyword, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme != "https") return null;
            if (DAppLaunchUri.TryGetAppId(uri, out int id))
            {
                launchUri = uri;
                launchAppId = id;
                return dapps.Where(p => p.Id == id);
            }
            else
                return dapps.Where(p => p.Url == keyword);
        }
        else
        {
            return dapps.Where(p => p.Name.Contains(keyword.Trim(), StringComparison.OrdinalIgnoreCase));
        }
    }

    protected override void OnItemSelected(object item)
    {
        Commands.LaunchDApp.Execute(GetLaunchParameter(item));
    }

    protected override void OnQueryConfirmed()
    {
        DApp[]? results = ItemsSource?.Cast<DApp>().ToArray();
        if (results?.Length == 1)
            Commands.LaunchDApp.Execute(GetLaunchParameter(results[0]));
    }

    object GetLaunchParameter(object item)
    {
        if (item is DApp dapp && launchUri is not null && launchAppId == dapp.Id)
            return launchUri;
        return item;
    }
}
