using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Services;
using System.Collections.ObjectModel;

namespace NeoOrder.OneGate.Pages;

public partial class HiddenAssetsPage : ContentPage
{
    readonly TokenManager tokenManager;

    public LoadingService LoadingService { get; set { field = value; OnPropertyChanged(); } }
    public ObservableCollection<ITokenInfo>? HiddenAssets { get; set { field = value; OnPropertyChanged(); } }

    public HiddenAssetsPage(TokenManager tokenManager)
    {
        this.LoadingService = new(LoadAssetsAsync);
        this.tokenManager = tokenManager;
        InitializeComponent();
        LoadingService.BeginLoad();
    }

    async Task LoadAssetsAsync()
    {
        var hiddens = await tokenManager.LoadHiddenTokens();
        HiddenAssets = new ObservableCollection<ITokenInfo>(hiddens);
    }

    async void OnUnhideClicked(object sender, EventArgs e)
    {
        Button button = (Button)sender;
        ITokenInfo token = (ITokenInfo)button.CommandParameter;
        HiddenAssets!.Remove(token);
        await tokenManager.UnhideTokenAsync(token.Hash);
        GlobalStates.Invalidate<SettingsPage>();
        GlobalStates.Invalidate<WalletPage>();
    }
}
