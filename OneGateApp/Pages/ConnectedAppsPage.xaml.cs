using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Extensions;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Controls.Popups;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using System.Collections.ObjectModel;

namespace NeoOrder.OneGate.Pages;

public partial class ConnectedAppsPage : ContentPage
{
    readonly IServiceProvider serviceProvider;
    readonly ConnectedDAppService connectedDAppService;

    public ObservableCollection<ConnectedDApp> ConnectedApps { get; } = [];
    public Command RefreshCommand { get; }
    public bool IsRefreshing { get; set { field = value; OnPropertyChanged(); } }
    public bool HasConnectedApps => ConnectedApps.Count > 0;
    public string SummaryText => ConnectedApps.Count == 0
        ? Strings.ConnectedAppsSummaryEmpty
        : string.Format(Strings.ConnectedAppsSummaryConnected, ConnectedApps.Count);

    public ConnectedAppsPage(IServiceProvider serviceProvider, ConnectedDAppService connectedDAppService)
    {
        this.serviceProvider = serviceProvider;
        this.connectedDAppService = connectedDAppService;
        RefreshCommand = new Command(async () => await RefreshAsync());
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        RefreshCommand.Execute(null);
    }

    async Task RefreshAsync()
    {
        IsRefreshing = true;
        try
        {
            ConnectedApps.Clear();
            foreach (ConnectedDApp app in await connectedDAppService.LoadAsync())
                ConnectedApps.Add(app);
            RefreshListState();
        }
        finally
        {
            IsRefreshing = false;
        }
    }

    void OnMenuClicked(object sender, EventArgs e)
    {
        Button button = (Button)sender;
        SwipeView swipeView = (SwipeView)button.CommandParameter;
        swipeView.Open(OpenSwipeItem.RightItems);
    }

    async void OnDisconnectClicked(object sender, EventArgs e)
    {
        ConnectedDApp? app = sender switch
        {
            SwipeItem swipeItem => swipeItem.CommandParameter as ConnectedDApp,
            Button button => button.CommandParameter as ConnectedDApp,
            _ => null
        };
        if (app is null) return;

        if (!await ConfirmDisconnectAsync(app)) return;
        await connectedDAppService.DisconnectAsync(app.Domain);
        ConnectedDApp? item = ConnectedApps.FirstOrDefault(p => string.Equals(p.Domain, app.Domain, StringComparison.OrdinalIgnoreCase));
        if (item is not null)
            ConnectedApps.Remove(item);
        RefreshListState();
        await Toast.Show(Strings.DAppDisconnected);
    }

    async void OnDisconnectAllClicked(object sender, EventArgs e)
    {
        if (ConnectedApps.Count == 0) return;

        var popup = serviceProvider.GetServiceOrCreateInstance<ConfirmationPopup>();
        popup.Title = Strings.DisconnectAllDApps;
        popup.Message = Strings.DisconnectAllDAppsText;
        popup.AcceptText = Strings.Disconnect;
        popup.IsDanger = true;
        var result = await this.ShowPopupAsync<bool>(popup);
        if (!result.Result) return;

        await connectedDAppService.DisconnectAllAsync();
        ConnectedApps.Clear();
        RefreshListState();
        await Toast.Show(Strings.AllDAppsDisconnected);
    }

    async Task<bool> ConfirmDisconnectAsync(ConnectedDApp app)
    {
        var popup = serviceProvider.GetServiceOrCreateInstance<ConfirmationPopup>();
        popup.Title = Strings.DisconnectDApp;
        popup.Message = string.Format(Strings.DisconnectDAppText, app.DisplayName);
        popup.AcceptText = Strings.Disconnect;
        popup.IsDanger = true;
        var result = await this.ShowPopupAsync<bool>(popup);
        return result.Result;
    }

    void RefreshListState()
    {
        OnPropertyChanged(nameof(HasConnectedApps));
        OnPropertyChanged(nameof(SummaryText));
    }
}
