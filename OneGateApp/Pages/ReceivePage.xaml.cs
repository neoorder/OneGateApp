using CommunityToolkit.Maui.Alerts;
using Neo;
using Neo.Wallets;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;

namespace NeoOrder.OneGate.Pages;

public partial class ReceivePage : ContentPage, IQueryAttributable
{
    string? memo;
    string? requestData;

    public Wallet Wallet { get; set { field = value; OnPropertyChanged(); } }
    public WalletAccount DefaultAccount => Wallet.GetDefaultAccount()!;
    public UInt160? Asset { get; set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(AddressUri)); } }
    public string? Memo
    {
        get => memo;
        set
        {
            if (memo == value) return;
            memo = value;
            OnPropertyChanged();
            OnRequestDetailsChanged();
        }
    }
    public string? RequestData
    {
        get => requestData;
        set
        {
            if (requestData == value) return;
            requestData = value;
            OnPropertyChanged();
            OnRequestDetailsChanged();
        }
    }
    public bool HasRequestSummary => !string.IsNullOrWhiteSpace(Memo) || !string.IsNullOrWhiteSpace(RequestData);
    public string RequestSummary
    {
        get
        {
            List<string> parts = [];
            AddSummaryPart(parts, Strings.RequestMemo, Memo);
            AddSummaryPart(parts, Strings.RequestData, RequestData);
            return string.Join(Environment.NewLine, parts);
        }
    }
    public string AddressUri
    {
        get
        {
            string uri = $"neo:{Uri.EscapeDataString(DefaultAccount.Address)}";
            List<string> query = [];
            if (Asset is not null)
                query.Add($"asset={Uri.EscapeDataString(Asset.ToString())}");
            AddQueryParameter(query, "memo", Memo);
            AddQueryParameter(query, "data", RequestData);
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

    void OnRequestDetailsChanged()
    {
        OnPropertyChanged(nameof(AddressUri));
        OnPropertyChanged(nameof(RequestSummary));
        OnPropertyChanged(nameof(HasRequestSummary));
    }

    static void AddQueryParameter(List<string> query, string name, string? value)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value)) return;
        query.Add($"{name}={Uri.EscapeDataString(value)}");
    }

    static void AddSummaryPart(List<string> parts, string label, string? value)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value)) return;
        parts.Add($"{label}: {value}");
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
            Title = Strings.ShareQRCode,
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
