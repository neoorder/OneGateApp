using System.Net;

namespace NeoOrder.OneGate.Pages;

public partial class DeveloperToolsPage : ContentPage
{
    public Command DAppTestingCommand { get; } = new(static async parameter =>
    {
        if (Uri.TryCreate(parameter?.ToString(), UriKind.Absolute, out var uri))
            await Shell.Current.GoToAsync("//dapps/launch", new Dictionary<string, object>
            {
                ["uri"] = WebUtility.UrlEncode(uri.ToString())
            });
    });

    public DeveloperToolsPage()
    {
        InitializeComponent();
    }
}
