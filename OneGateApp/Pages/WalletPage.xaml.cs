using CommunityToolkit.Maui.Alerts;
using Neo;
using Neo.Wallets;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;

namespace NeoOrder.OneGate.Pages;

public partial class WalletPage : ContentPage
{
    readonly ApplicationDbContext dbContext;
    readonly TokenManager tokenManager;

    public LoadingService LoadingService { get; set { field = value; OnPropertyChanged(); } }
    public Wallet Wallet { get; set { field = value; OnPropertyChanged(); } }
    public bool ShowBalance { get; set { field = value; OnPropertyChanged(); } }
    public WalletAccount DefaultAccount => Wallet.GetDefaultAccount()!;
    public UInt160 ScriptHash => DefaultAccount.ScriptHash;
    public IReadOnlyList<AssetInfo>? Assets { get; set { field = value; OnPropertyChanged(); } }
    public IReadOnlyList<NFT>? NFTs { get; set { field = value; OnPropertyChanged(); } }
    public string TotalValuation { get; set { field = value; OnPropertyChanged(); } } = "N/A";

    public WalletPage(ApplicationDbContext dbContext, IWalletProvider walletProvider, TokenManager tokenManager)
    {
        this.LoadingService = new(RefreshWallet, LoadAssetsAsync, LoadNFTsAsync);
        this.dbContext = dbContext;
        this.tokenManager = tokenManager;
        Wallet = walletProvider.GetWallet()!;
        ShowBalance = dbContext.Settings.Get<bool>("wallet/showBalance");
        InitializeComponent();
        LoadingService.BeginLoad();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (this.ShouldRefresh())
            LoadingService.BeginLoad();
    }

    async void OnToggleShowBalance(object sender, EventArgs e)
    {
        ShowBalance = !ShowBalance;
        await dbContext.Settings.PutAsync("wallet/showBalance", ShowBalance);
    }

    Task RefreshWallet()
    {
        OnPropertyChanged(nameof(Wallet));
        return Task.CompletedTask;
    }

    async Task LoadAssetsAsync()
    {
        Assets = await tokenManager.LoadAssetsAsync();
        decimal totalValuation = Assets
            .Where(p => p.Valuation.HasValue)
            .Sum(p => p.Valuation!.Value);
        TotalValuation = $"$ {totalValuation:N2}";
    }

    async Task LoadNFTsAsync()
    {
        NFTs = await tokenManager.LoadNFTsAsync();
    }

    async void OnSendClicked(object sender, EventArgs e)
    {
        if (Assets is null)
        {
            LoadingService.BeginLoad();
            await Toast.Show(Strings.LoadingWalletData + "…");
            return;
        }
        await Shell.Current.GoToAsync("//wallet/send", new Dictionary<string, object>
        {
            ["assets"] = Assets
        });
    }

    async void OnAssetTapped(object sender, TappedEventArgs e)
    {
        AssetInfo asset = (AssetInfo)e.Parameter!;
        await Shell.Current.GoToAsync("//wallet/asset/details", new Dictionary<string, object> { ["asset"] = asset });
    }

    async void OnNFTTapped(object sender, TappedEventArgs e)
    {
        NFT nft = (NFT)e.Parameter!;
        await Shell.Current.GoToAsync("//wallet/nft/details", new Dictionary<string, object> { ["nft"] = nft });
    }
}
