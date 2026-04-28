using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Pages;
using NeoOrder.OneGate.Services;

namespace NeoOrder.OneGate.Controls.Handlers;

partial class DAppSearchHandler : SearchHandler
{
    public readonly static BindableProperty DAppsProperty = BindableProperty.Create(nameof(DApps), typeof(IList<DApp>), typeof(DAppSearchHandler));

    public IList<DApp>? DApps
    {
        get => (IList<DApp>?)GetValue(DAppsProperty);
        set => SetValue(DAppsProperty, value);
    }

    protected override void OnQueryChanged(string oldValue, string newValue)
    {
        ItemsSource = Search(newValue, DApps)?.ToArray();
    }

    static IEnumerable<DApp>? Search(string? keyword, IEnumerable<DApp>? dapps)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return null;
        if (dapps is null) return null;
        if (Uri.TryCreate(keyword, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme != "https") return null;
            if (uri.Authority == SharedOptions.OneGateDomain && uri.Segments.Length == 3 && uri.Segments[1] == "app/" && int.TryParse(uri.Segments[2], out int id))
                return dapps.Where(p => p.Id == id);
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
        Commands.LaunchDApp.Execute(item);
    }

    protected override void OnQueryConfirmed()
    {
        DApp[]? results = ItemsSource?.Cast<DApp>().ToArray();
        if (results?.Length == 1)
            Commands.LaunchDApp.Execute(results[0]);
    }
}
