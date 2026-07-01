using CommunityToolkit.Maui.Alerts;
using Neo;
using Neo.Wallets;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;

namespace NeoOrder.OneGate.Pages;

public partial class ReceivePage : ContentPage, IQueryAttributable
{
    public Wallet Wallet { get; set { field = value; OnPropertyChanged(); } }
    public WalletAccount DefaultAccount => Wallet.GetDefaultAccount()!;
    public UInt160? Asset { get; set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(AddressUri)); } }
    public string AddressUri
    {
        get
        {
            string uri = $"neo:{Uri.EscapeDataString(DefaultAccount.Address)}";
            List<string> query = [];
            if (Asset is not null)
                query.Add($"asset={Uri.EscapeDataString(Asset.ToString())}");
            return query.Count == 0 ? uri : $"{uri}?{string.Join("&", query)}";
        }
    }

    public ReceivePage(IWalletProvider walletProvider)
    {
        Wallet = walletProvider.GetWallet()!;
        InitializeComponent();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("asset", out var asset))
            Asset = (string)asset;
    }

    async void OnShareQRCode(object sender, EventArgs e)
    {
        string fileName = $"onegate-request-{DateTime.Now:yyyyMMdd-HHmmss}.png";
        string path = Path.Combine(FileSystem.CacheDirectory, fileName);
        IScreenshotResult? result = await qrCodeCard.CaptureAsync();
        if (result is null)
        {
            await Toast.Show(Strings.ScreenshotFailed);
            return;
        }

        await using Stream screenshot = await result.OpenReadAsync();
        await using FileStream stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None);
        await screenshot.CopyToAsync(stream);
        await Share.RequestAsync(new ShareFileRequest
        {
            Title = Strings.Share,
            File = new ShareFile(path)
        });
    }

    async void OnSaveToPhotoLibrary(object sender, EventArgs e)
    {
        string fileName = $"{DateTime.Now:yyyy-MM-dd HHmmss}.png";
        var result = await qrCodeCard.CaptureAsync();
        if (result is null)
            await Toast.Show(Strings.ScreenshotFailed);
        else
            await PhotoLibraryService.SaveAsync(result, fileName);
    }
}
