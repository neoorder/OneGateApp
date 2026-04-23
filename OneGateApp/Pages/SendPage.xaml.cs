using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Extensions;
using Neo;
using Neo.Network.P2P.Payloads;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.Wallets;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Controls.Popups;
using NeoOrder.OneGate.Controls.Views;
using NeoOrder.OneGate.Controls.Views.Validation;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Models.Intents;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using NeoOrder.OneGate.Services.RPC;
using System.Numerics;
using Contact = NeoOrder.OneGate.Data.Contact;

namespace NeoOrder.OneGate.Pages;

public partial class SendPage : ContentPage, IQueryAttributable
{
    readonly IServiceProvider serviceProvider;
    readonly ProtocolSettings protocolSettings;
    readonly Wallet wallet;
    readonly RpcClient rpcClient;
    readonly TokenManager tokenManager;

    public IReadOnlyList<AssetInfo>? Assets { get; set { field = value; OnPropertyChanged(); } }
    public required AssetInfo SelectedAsset { get; set { field = value; OnPropertyChanged(); } }
    public string? ToAddress { get; set { field = value; OnPropertyChanged(); } }
    public decimal Amount { get; set { field = value; OnPropertyChanged(); } }

    public SendPage(IServiceProvider serviceProvider, ProtocolSettings protocolSettings, IWalletProvider walletProvider, RpcClient rpcClient, TokenManager tokenManager)
    {
        this.serviceProvider = serviceProvider;
        this.protocolSettings = protocolSettings;
        this.wallet = walletProvider.GetWallet()!;
        this.rpcClient = rpcClient;
        this.tokenManager = tokenManager;
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

    void OnValidateAddress(object sender, CustomValidationEventArgs e)
    {
        if (e.Value is not string address)
        {
            e.IsValid = false;
            return;
        }
        try
        {
            address.ToScriptHash(protocolSettings.AddressVersion);
        }
        catch
        {
            e.IsValid = false;
        }
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
            UInt160 to = ToAddress!.ToScriptHash(protocolSettings.AddressVersion);
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
            var popup = serviceProvider.GetServiceOrCreateInstance<SendTransactionPopup>();
            popup.Message = Strings.SendTransactionByUserText;
            popup.Transaction = tx;
            popup.Intents = intents;
            var result = await this.ShowPopupAsync<bool>(popup);
            if (!result.Result) return;
            var context = new ContractParametersContext(null!, tx, protocolSettings.Network);
            if (!wallet.SignWithWorkaround(context) || !context.Completed)
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
