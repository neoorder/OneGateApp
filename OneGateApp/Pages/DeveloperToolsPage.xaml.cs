using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Services;
using NeoOrder.OneGate.Services.RemoteDebug;
using System.Net;
using System.Collections.ObjectModel;
using NeoOrder.OneGate.Properties;

namespace NeoOrder.OneGate.Pages;

public partial class DeveloperToolsPage : ContentPage
{
    readonly ApplicationDbContext dbContext;
    readonly RemoteDebugService remoteDebugService;

    public bool IsDeveloperModeEnabled { get; set { field = value; OnPropertyChanged(); } }
    public bool IsSettingsLoaded { get; private set { field = value; OnPropertyChanged(); } }
    public string RemoteDebugStatusText { get; private set { field = value; OnPropertyChanged(); } } = string.Empty;
    public ObservableCollection<TrustedRemoteDebugger> TrustedDebuggers { get; } = [];

    public Command DAppTestingCommand { get; } = new(static async parameter =>
    {
        await LaunchDAppAsync(parameter?.ToString());
    });

    public DeveloperToolsPage(ApplicationDbContext dbContext, RemoteDebugService remoteDebugService)
    {
        this.dbContext = dbContext;
        this.remoteDebugService = remoteDebugService;
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        remoteDebugService.StateChanged += OnRemoteDebugStateChanged;
        await LoadSettingsAsync();
        await LoadRemoteDebugStateAsync();
    }

    protected override void OnDisappearing()
    {
        remoteDebugService.StateChanged -= OnRemoteDebugStateChanged;
        base.OnDisappearing();
    }

    void OnRemoteDebugStateChanged(object? sender, EventArgs e)
        => MainThread.BeginInvokeOnMainThread(async () => await LoadRemoteDebugStateAsync());

    async Task LoadRemoteDebugStateAsync()
    {
        RemoteDebugStatusText = remoteDebugService.IsConnected
            ? string.Format(Strings.RemoteDebugConnected, remoteDebugService.ConnectedDebuggerName)
            : Strings.RemoteDebugWaiting;
        TrustedDebuggers.Clear();
        foreach (TrustedRemoteDebugger debugger in await remoteDebugService.GetTrustedDebuggersAsync())
            TrustedDebuggers.Add(debugger);
    }

    async void OnPairRemoteDebugClicked(object sender, EventArgs e)
    {
        if (!IsDeveloperModeEnabled) return;
        await Commands.ScanQRCode.ExecuteAsync("?action=PairRemoteDebug");
    }

    async void OnForgetRemoteDebugClicked(object sender, EventArgs e)
    {
        if (sender is not Button { CommandParameter: string debuggerId }) return;
        bool confirmed = await DisplayAlertAsync(
            Strings.RemoteDebugForgetDebugger,
            Strings.RemoteDebugForgetPrompt,
            Strings.OK,
            Strings.Cancel);
        if (!confirmed) return;
        await remoteDebugService.ForgetDebuggerAsync(debuggerId);
        await LoadRemoteDebugStateAsync();
    }

    async Task LoadSettingsAsync()
    {
        IsSettingsLoaded = false;
        IsDeveloperModeEnabled = await dbContext.Settings.GetAsync<bool>(DAppCatalogPolicy.DeveloperModeKey);
        IsSettingsLoaded = true;
    }

    async void OnDeveloperModeToggled(object sender, ToggledEventArgs e)
    {
        if (!IsSettingsLoaded) return;
        await dbContext.Settings.PutAsync(DAppCatalogPolicy.DeveloperModeKey, e.Value);
        await remoteDebugService.SetDeveloperModeAsync(e.Value);
        GlobalStates.Invalidate<DAppsPage>();
        GlobalStates.Invalidate<GamingPage>();
        GlobalStates.Invalidate<GlobalSearchPage>();
    }

    async void OnDAppSubmissionClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//home/settings/developer/submission");
    }

    async void OnDAppTestingClicked(object sender, EventArgs e)
    {
        await LaunchDAppAsync(dAppTestingEntry.Text);
    }

    static async Task LaunchDAppAsync(string? value)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            await Shell.Current.GoToAsync("launch", new Dictionary<string, object>
            {
                ["uri"] = WebUtility.UrlEncode(uri.ToString())
            });
    }
}
