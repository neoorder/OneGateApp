using CommunityToolkit.Maui.Alerts;
using Neo;
using Neo.Wallets;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using System.Globalization;

namespace NeoOrder.OneGate.Pages;

public partial class ReceivePage : ContentPage, IQueryAttributable
{
    string? amountText;
    string? memo;
    string? requestData;
    bool hasAmountError;

    public Wallet Wallet { get; set { field = value; OnPropertyChanged(); } }
    public WalletAccount DefaultAccount => Wallet.GetDefaultAccount()!;
    public UInt160? Asset { get; set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(AddressUri)); } }
    public string? AmountText
    {
        get => amountText;
        set
        {
            if (amountText == value) return;
            amountText = value;
            OnPropertyChanged();
            OnRequestDetailsChanged();
        }
    }
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
    public bool HasAmountError
    {
        get => hasAmountError;
        private set
        {
            if (hasAmountError == value) return;
            hasAmountError = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsRequestValid));
        }
    }
    public bool IsRequestValid => !HasAmountError;
    public bool HasRequestSummary => !string.IsNullOrEmpty(RequestSummary);
    public string RequestSummary
    {
        get
        {
            List<string> parts = [];
            if (!HasAmountError && TryGetRequestedAmount(out decimal amount))
                parts.Add($"{Strings.Amount}: {FormatAmount(amount)}");
            AddSummaryPart(parts, Strings.RequestMemo, Memo);
            AddSummaryPart(parts, Strings.RequestData, RequestData);
            return string.Join(Environment.NewLine, parts);
        }
    }
    public string AddressUri
    {
        get
        {
            string uri = $"neo:{DefaultAccount.Address}";
            List<string> query = [];
            if (Asset is not null)
                query.Add($"asset={Uri.EscapeDataString(Asset.ToString())}");
            if (!HasAmountError && TryGetRequestedAmount(out decimal amount))
                query.Add($"amount={Uri.EscapeDataString(FormatAmount(amount))}");
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
        ValidateAmount();
        OnPropertyChanged(nameof(AddressUri));
        OnPropertyChanged(nameof(RequestSummary));
        OnPropertyChanged(nameof(HasRequestSummary));
    }

    void ValidateAmount()
    {
        string? value = AmountText?.Trim();
        HasAmountError = !string.IsNullOrEmpty(value) && !TryGetRequestedAmount(out _);
    }

    bool TryGetRequestedAmount(out decimal amount)
    {
        string? value = AmountText?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            amount = 0;
            return false;
        }
        bool parsed = decimal.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out amount)
            || decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out amount);
        return parsed && amount > 0;
    }

    static void AddQueryParameter(List<string> query, string name, string? value)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value)) return;
        query.Add($"{name}={Uri.EscapeDataString(value)}");
    }

    static string FormatAmount(decimal amount)
    {
        return amount.ToString("0.############################", CultureInfo.InvariantCulture);
    }

    static void AddSummaryPart(List<string> parts, string label, string? value)
    {
        value = value?.Trim();
        if (string.IsNullOrEmpty(value)) return;
        parts.Add($"{label}: {value}");
    }

    async void OnCopyRequestUri(object sender, EventArgs e)
    {
        if (!IsRequestValid) return;
        await Clipboard.SetTextAsync(AddressUri);
        await Toast.Show(Strings.ReceiveRequestCopied);
    }

    async void OnShareQRCode(object sender, EventArgs e)
    {
        if (!IsRequestValid) return;
        string fileName = $"onegate-request-{DateTime.Now:yyyyMMdd-HHmmss}.png";
        string path = Path.Combine(FileSystem.CacheDirectory, fileName);
        var result = await qrCodeCard.CaptureAsync();
        if (result is null)
        {
            await Toast.Show(Strings.ScreenshotFailed);
            return;
        }
        await using (var stream = File.Open(path, FileMode.Create, FileAccess.Write, FileShare.None))
            await result.CopyToAsync(stream);
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
