using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Extensions;
using Microsoft.EntityFrameworkCore;
using Neo;
using Neo.Network.P2P.Payloads;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.Wallets;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Controls.Popups;
using NeoOrder.OneGate.Controls.Views;
using NeoOrder.OneGate.Controls.Views.Validation;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Models.Intents;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using NeoOrder.OneGate.Services.RPC;
using System.Globalization;
using System.Numerics;
using System.Web;
using Contact = NeoOrder.OneGate.Data.Contact;

namespace NeoOrder.OneGate.Pages;

public partial class SendPage : ContentPage, IQueryAttributable
{
    readonly IServiceProvider serviceProvider;
    readonly ProtocolSettings protocolSettings;
    readonly Wallet wallet;
    readonly RpcClient rpcClient;
    readonly TokenManager tokenManager;
    readonly ApplicationDbContext dbContext;
    Contact[]? contacts;
    int addressInsightVersion;

    public IReadOnlyList<AssetInfo>? Assets { get; set { field = value; OnPropertyChanged(); } }
    public required AssetInfo SelectedAsset { get; set { field = value; OnPropertyChanged(); } }
    public string? ToAddress
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasClipboardSuggestion));
            _ = RefreshAddressInsightsAsync();
        }
    }
    public decimal Amount { get; set { field = value; OnPropertyChanged(); } }
    public bool HasClipboardText { get; set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasClipboardSuggestion)); } }
    public bool HasClipboardSuggestion => HasClipboardText && string.IsNullOrWhiteSpace(ToAddress);
    public Contact? MatchedContact { get; set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasAddressInsight)); OnPropertyChanged(nameof(AddressInsightTitle)); OnPropertyChanged(nameof(AddressInsightText)); } }
    public bool IsOwnWalletAddress { get; set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasAddressInsight)); OnPropertyChanged(nameof(AddressInsightTitle)); OnPropertyChanged(nameof(AddressInsightText)); } }
    public bool IsUnknownValidAddress { get; set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasAddressInsight)); OnPropertyChanged(nameof(AddressInsightTitle)); OnPropertyChanged(nameof(AddressInsightText)); } }
    public bool HasAddressInsight => MatchedContact is not null || IsOwnWalletAddress || IsUnknownValidAddress;
    public string AddressInsightTitle
    {
        get
        {
            if (MatchedContact is not null) return Strings.SavedAddressTitle;
            if (IsOwnWalletAddress) return Strings.OwnWalletAddressTitle;
            if (IsUnknownValidAddress) return Strings.UnknownAddressRiskTitle;
            return string.Empty;
        }
    }
    public string AddressInsightText
    {
        get
        {
            if (MatchedContact is not null) return string.Format(Strings.SavedAddressText, MatchedContact.Label);
            if (IsOwnWalletAddress) return Strings.OwnWalletAddressText;
            if (IsUnknownValidAddress) return Strings.UnknownAddressRiskText;
            return string.Empty;
        }
    }

    public SendPage(IServiceProvider serviceProvider, ProtocolSettings protocolSettings, IWalletProvider walletProvider, RpcClient rpcClient, TokenManager tokenManager, ApplicationDbContext dbContext)
    {
        this.serviceProvider = serviceProvider;
        this.protocolSettings = protocolSettings;
        this.wallet = walletProvider.GetWallet()!;
        this.rpcClient = rpcClient;
        this.tokenManager = tokenManager;
        this.dbContext = dbContext;
        InitializeComponent();
    }

    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("address", out var obj_address))
        {
            ToAddress = (string)obj_address;
        }
        if (query.TryGetValue("amount", out var obj_amount))
        {
            if (Amount == 0) Amount = obj_amount switch
            {
                string s => decimal.Parse(s),
                decimal d => d,
                _ => 0
            };
        }
        if (Assets is null && query.TryGetValue("assets", out var obj_assets))
        {
            Assets = (AssetInfo[])obj_assets;
            SelectedAsset = Assets.FirstOrDefault(p => p.Token.Hash == NativeContract.NEO.Hash) ?? Assets[0];
        }
        else if (SelectedAsset is null && query.TryGetValue("asset", out var obj_asset))
        {
            switch (obj_asset)
            {
                case AssetInfo assetInfo:
                    SelectedAsset = assetInfo;
                    break;
                case UInt160 assetId:
                    try
                    {
                        SelectedAsset = await tokenManager.LoadAssetAsync(assetId);
                    }
                    catch (Exception ex)
                    {
                        await Toast.Show(ex.Message);
                        return;
                    }
                    break;
                case string s_assetId:
                    try
                    {
                        SelectedAsset = await tokenManager.LoadAssetAsync(UInt160.Parse(s_assetId));
                    }
                    catch (Exception ex)
                    {
                        await Toast.Show(ex.Message);
                        return;
                    }
                    break;
            }
        }
        if (Assets is null && SelectedAsset is null)
        {
            try
            {
                Assets = await tokenManager.LoadAssetsAsync();
            }
            catch (Exception ex)
            {
                await Toast.Show(ex.Message);
                return;
            }
            SelectedAsset = Assets.FirstOrDefault(p => p.Token.Hash == NativeContract.NEO.Hash) ?? Assets[0];
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        contacts = null;
        RefreshClipboardAvailability();
        await RefreshAddressInsightsAsync();
    }

    async Task EnsureContactsLoadedAsync()
    {
        contacts ??= await dbContext.Contacts.AsNoTracking().ToArrayAsync();
    }

    void RefreshClipboardAvailability()
    {
        try
        {
            HasClipboardText = Clipboard.HasText;
        }
        catch
        {
            HasClipboardText = false;
        }
    }

    async Task RefreshAddressInsightsAsync()
    {
        int version = ++addressInsightVersion;
        try
        {
            await EnsureContactsLoadedAsync();
        }
        catch
        {
            contacts = [];
        }
        if (version != addressInsightVersion) return;

        Contact? matchedContact = null;
        bool isOwnWalletAddress = false;
        bool isUnknownValidAddress = false;
        if (TryReadAddress(ToAddress, out string address))
        {
            UInt160 scriptHash = address.ToScriptHash(protocolSettings.AddressVersion);
            matchedContact = contacts!.FirstOrDefault(p => p.Address == address);
            isOwnWalletAddress = wallet.Contains(scriptHash);
            isUnknownValidAddress = matchedContact is null && !isOwnWalletAddress;
        }
        MatchedContact = matchedContact;
        IsOwnWalletAddress = isOwnWalletAddress;
        IsUnknownValidAddress = isUnknownValidAddress;
        OnPropertyChanged(nameof(HasClipboardSuggestion));
    }

    bool TryReadAddress(string? value, out string address)
    {
        return TryReadPaymentRequest(value, out address, out _);
    }

    bool TryReadPaymentRequest(string? value, out string address, out decimal? amount)
    {
        address = value?.Trim() ?? string.Empty;
        amount = null;
        if (address.StartsWith("neo:", StringComparison.OrdinalIgnoreCase) && Uri.TryCreate(address, UriKind.Absolute, out Uri? uri))
        {
            address = uri.LocalPath;
            var query = HttpUtility.ParseQueryString(uri.Query);
            string? rawAmount = query["amount"];
            if (!string.IsNullOrWhiteSpace(rawAmount)
                && decimal.TryParse(rawAmount, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal parsedAmount)
                && parsedAmount > 0)
            {
                amount = parsedAmount;
            }
        }
        if (string.IsNullOrWhiteSpace(address)) return false;
        try
        {
            address.ToScriptHash(protocolSettings.AddressVersion);
            return true;
        }
        catch
        {
            return false;
        }
    }

    async void OnSelectAsset(object sender, TappedEventArgs e)
    {
        if (Assets is null) return;
        var popup = serviceProvider.GetServiceOrCreateInstance<SelectAssetPopup>();
        popup.Assets = Assets;
        popup.SelectedHash = SelectedAsset.Token.Hash;
        var result = await this.ShowPopupAsync<AssetInfo?>(popup);
        if (result.Result is not null) SelectedAsset = result.Result;
    }

    async void OnSelectContact(object sender, EventArgs e)
    {
        var popup = serviceProvider.GetServiceOrCreateInstance<SelectContactPopup>();
        var result = await this.ShowPopupAsync<Contact?>(popup);
        if (result.Result is not null) ToAddress = result.Result.Address;
    }

    async void OnPasteClipboardAddress(object sender, EventArgs e)
    {
        try
        {
            if (!TryReadPaymentRequest(await Clipboard.GetTextAsync(), out string address, out decimal? amount))
            {
                RefreshClipboardAvailability();
                await Toast.Show(Strings.ClipboardAddressInvalid);
                return;
            }
            ToAddress = address;
            if (amount is decimal paymentAmount && Amount == 0)
                Amount = paymentAmount;
            HasClipboardText = false;
            await Toast.Show(Strings.AddressPasted);
        }
        catch (Exception ex)
        {
            await Toast.Show(ex.Message);
        }
    }

    void OnValidateAddress(object sender, CustomValidationEventArgs e)
    {
        e.IsValid = e.Value is string address && TryReadAddress(address, out _);
    }

    void OnSelectAllBalance(object sender, EventArgs e)
    {
        Amount = decimal.Parse(SelectedAsset.DecimalBalance.ToString());
    }

    void OnValidateAmount(object sender, CustomValidationEventArgs e)
    {
        string text = (string)e.Value!;
        e.IsValid = BigDecimal.TryParse(text, SelectedAsset.Token.Decimals, out _);
    }

    async void OnSubmitted(object sender, EventArgs e)
    {
        Submit submit = (Submit)sender;
        using (submit.EnterBusyState())
        {
            WalletAccount account = wallet.GetDefaultAccount()!;
            UInt160 from = account.ScriptHash;
            if (!TryReadAddress(ToAddress, out string toAddress)) return;
            UInt160 to = toAddress.ToScriptHash(protocolSettings.AddressVersion);
            BigInteger amount = BigDecimal.Parse(entryAmount.Text, SelectedAsset.Token.Decimals).Value;
            TransactionIntent[] intents = [new TransferIntent
            {
                Asset = SelectedAsset.Token,
                From = from,
                To = to,
                Amount = amount
            }];
            Transaction tx;
            try
            {
                tx = await rpcClient.MakeTransactionAsync(SelectedAsset.Token.Hash, from, to, amount, null);
            }
            catch (Exception ex)
            {
                await Toast.Show(ex.Message);
                return;
            }
            if (SelectedAsset.Token.Hash == NativeContract.GAS.Hash && tx.NetworkFee + tx.SystemFee + amount > SelectedAsset.Balance)
            {
                BigDecimal max = new(SelectedAsset.Balance - tx.NetworkFee - tx.SystemFee, SelectedAsset.Token.Decimals);
                validationAmount.SetError(string.Format(Strings.InsufficientBalanceForAmountAndFees, max));
                return;
            }
            var popup = serviceProvider.GetServiceOrCreateInstance<SendTransactionPopup>();
            popup.Message = Strings.SendTransactionByUserText;
            popup.Transaction = tx;
            popup.Intents = intents;
            var result = await this.ShowPopupAsync<bool>(popup);
            if (!result.Result) return;
            var context = new ContractParametersContext(null!, tx, protocolSettings.Network);
            if (!wallet.Sign(context) || !context.Completed)
                await Toast.Show(Strings.SignTransactionFailed);
            tx.Witnesses = context.GetWitnesses();
            try
            {
                await rpcClient.SendRawTransaction(tx);
            }
            catch (Exception ex)
            {
                await Toast.Show(ex.Message);
                return;
            }
            GlobalStates.Invalidate<WalletPage>();
            await Shell.Current.GoToAsync("//wallet/sending", new Dictionary<string, object>
            {
                ["tx"] = tx,
                ["intents"] = intents
            });
        }
    }
}
