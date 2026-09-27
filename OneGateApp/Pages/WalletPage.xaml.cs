using CommunityToolkit.Maui.Alerts;
using Neo;
using Neo.Wallets;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using System.Collections.ObjectModel;

namespace NeoOrder.OneGate.Pages;

public partial class WalletPage : ContentPage
{
    readonly ApplicationDbContext dbContext;
    readonly TokenManager tokenManager;
    NftPageSession? nftSession;

    public LoadingService LoadingService { get; set { field = value; OnPropertyChanged(); } }
    public Wallet Wallet { get; set { field = value; OnPropertyChanged(); } }
    public bool ShowBalance { get; set { field = value; OnPropertyChanged(); } }
    public WalletAccount DefaultAccount => Wallet.GetDefaultAccount()!;
    public UInt160 ScriptHash => DefaultAccount.ScriptHash;
    public IReadOnlyList<AssetInfo>? Assets { get; set { field = value; OnPropertyChanged(); } }
    public ObservableCollection<NFT> NFTs { get; } = [];
    public bool HasMoreNFTs { get; set { field = value; OnPropertyChanged(); } }
    public bool IsLoadingNFTs { get; set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowNftEmptyState)); } }
    public bool NftLoadFailed { get; set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(ShowNftEmptyState)); } }
    public bool ShowNftEmptyState => !IsLoadingNFTs && !NftLoadFailed;
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
        else if (nftSession is null)
            _ = LoadNFTsAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        NftPageSession? session = nftSession;
        nftSession = null;
        HasMoreNFTs = false;
        IsLoadingNFTs = false;
        if (session is not null) _ = CloseNftSessionAsync(session);
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
        NftPageSession? previous = nftSession;
        var session = new NftPageSession(token => tokenManager.LoadNFTPagesAsync(cancellationToken: token));
        nftSession = session;
        NFTs.Clear();
        NftLoadFailed = false;
        HasMoreNFTs = false;
        IsLoadingNFTs = false;
        if (previous is not null) await CloseNftSessionAsync(previous);
        await LoadNFTPageAsync(session);
    }

    async Task LoadNFTPageAsync(NftPageSession session)
    {
        if (!ReferenceEquals(nftSession, session) || IsLoadingNFTs) return;
        IsLoadingNFTs = true;
        try
        {
            NFT[]? page = await session.ReadNextAsync();
            if (!ReferenceEquals(nftSession, session)) return;
            if (page is not null) foreach (NFT nft in page) NFTs.Add(nft);
            HasMoreNFTs = page?.Length == 100;
            if (!HasMoreNFTs) await CloseNftSessionAsync(session);
        }
        catch (OperationCanceledException) when (!ReferenceEquals(nftSession, session)) { }
        catch (Exception ex)
        {
            if (ReferenceEquals(nftSession, session))
            {
                HasMoreNFTs = false;
                NftLoadFailed = true;
                await Toast.Show(ex.Message);
            }
            await CloseNftSessionAsync(session);
        }
        finally
        {
            if (ReferenceEquals(nftSession, session)) IsLoadingNFTs = false;
        }
    }

    async void OnLoadMoreNFTs(object sender, EventArgs e)
    {
        if (!HasMoreNFTs || IsLoadingNFTs) return;
        if (nftSession is not null) await LoadNFTPageAsync(nftSession);
    }

    static async Task CloseNftSessionAsync(NftPageSession session)
    {
        try { await session.DisposeAsync(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"NFT pagination cleanup failed: {ex.Message}"); }
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
