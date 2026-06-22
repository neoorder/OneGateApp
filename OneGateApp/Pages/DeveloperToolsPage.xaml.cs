using NeoOrder.OneGate.Data;
using System.Net;

namespace NeoOrder.OneGate.Pages;

public partial class DeveloperToolsPage : ContentPage
{
    const string DeveloperModeKey = "preference/developer_mode_enabled";

    readonly ApplicationDbContext dbContext;

    public bool IsDAppDebugPanelEnabled { get; set { field = value; OnPropertyChanged(); } }
    public bool IsSettingsLoaded { get; private set { field = value; OnPropertyChanged(); } }

    public Command DAppTestingCommand { get; } = new(static async parameter =>
    {
        if (Uri.TryCreate(parameter?.ToString(), UriKind.Absolute, out var uri))
            await Shell.Current.GoToAsync("launch", new Dictionary<string, object>
            {
                ["uri"] = WebUtility.UrlEncode(uri.ToString())
            });
    });

    public DeveloperToolsPage(ApplicationDbContext dbContext)
    {
        this.dbContext = dbContext;
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadSettingsAsync();
    }

    async Task LoadSettingsAsync()
    {
        IsSettingsLoaded = false;
        IsDAppDebugPanelEnabled = await dbContext.Settings.GetAsync<bool>(DeveloperModeKey);
        IsSettingsLoaded = true;
    }

    async void OnDAppDebugPanelToggled(object sender, ToggledEventArgs e)
    {
        if (!IsSettingsLoaded) return;
        await dbContext.Settings.PutAsync(DeveloperModeKey, e.Value);
    }
}
