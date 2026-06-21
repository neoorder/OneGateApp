using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Extensions;
using Neo;
using Neo.Network.P2P.Payloads;
using Neo.SmartContract;
using Neo.SmartContract.Native;
using Neo.Wallets;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Controls.Popups;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Models.Intents;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using NeoOrder.OneGate.Services.RPC;
using System.Numerics;

namespace NeoOrder.OneGate.Pages;

public partial class AssetDetailsPage : ContentPage, IQueryAttributable
{
    readonly TokenManager tokenManager;
    readonly RpcClient rpcClient;
    readonly IWalletProvider walletProvider;
    readonly ProtocolSettings protocolSettings;
    readonly IServiceProvider serviceProvider;

    public required AssetInfo Asset { get; set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNeo)); } }
    public bool ShowBalance { get; set { field = value; OnPropertyChanged(); } }
    public bool IsNeo => Asset?.Token.Hash == NativeContract.NEO.Hash;
    public BigInteger UnclaimedGas
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayUnclaimedGas));
            OnPropertyChanged(nameof(HasUnclaimedGas));
        }
    }
    public string DisplayUnclaimedGas => $"{new BigDecimal(UnclaimedGas, NativeContract.GAS.Decimals)} {NativeContract.GAS.Symbol}";
    public bool HasUnclaimedGas => UnclaimedGas > 0;

    public AssetDetailsPage(ApplicationDbContext dbContext, TokenManager tokenManager, RpcClient rpcClient, IWalletProvider walletProvider, ProtocolSettings protocolSettings, IServiceProvider serviceProvider)
    {
        this.tokenManager = tokenManager;
        this.rpcClient = rpcClient;
        this.walletProvider = walletProvider;
        this.protocolSettings = protocolSettings;
        this.serviceProvider = serviceProvider;
        ShowBalance = dbContext.Settings.Get<bool>("wallet/showBalance");
        InitializeComponent();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        Asset = (AssetInfo)query["asset"];
        if (Asset.Token.Hash == NativeContract.NEO.Hash || Asset.Token.Hash == NativeContract.GAS.Hash)
            ToolbarItems.Remove(hideButton);
        if (IsNeo)
            _ = LoadUnclaimedGasAsync();
    }

    async Task LoadUnclaimedGasAsync()
    {
        try
        {
            WalletAccount account = walletProvider.GetWallet()!.GetDefaultAccount()!;
            UnclaimedGas = await rpcClient.GetUnclaimedGasAsync(account.ScriptHash);
        }
        catch
        {
        }
    }

    async void OnHideClicked(object sender, EventArgs e)
    {
        await tokenManager.HideTokenAsync(Asset.Token.Hash);
        GlobalStates.Invalidate<WalletPage>();
        await Shell.Current.GoToAsync("..");
        await Toast.Show(Strings.AssetHidden);
    }

    async void OnSendClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//wallet/asset/details/send", new Dictionary<string, object>
        {
            ["asset"] = Asset
        });
    }

    async void OnClaimGas(object sender, EventArgs e)
    {
        Wallet wallet = walletProvider.GetWallet()!;
        WalletAccount account = wallet.GetDefaultAccount()!;
        UInt160 from = account.ScriptHash;
        if (Asset.Balance <= 0)
        {
            await Toast.Show(Strings.NoNeoToClaimGas);
            return;
        }
        // Accrued GAS is distributed by a NEO transfer; a self-transfer claims it without moving funds elsewhere.
        TransactionIntent[] intents = [new TransferIntent
        {
            Asset = Asset.Token,
            From = from,
            To = from,
            Amount = Asset.Balance
        }];
        Transaction tx;
        try
        {
            tx = await rpcClient.MakeTransactionAsync(NativeContract.NEO.Hash, from, from, Asset.Balance, null);
        }
        catch (Exception ex)
        {
            await Toast.Show(ex.Message);
            return;
        }
        SendTransactionPopup popup = serviceProvider.GetServiceOrCreateInstance<SendTransactionPopup>();
        popup.Message = Strings.ClaimGasConfirmText;
        popup.Transaction = tx;
        popup.Intents = intents;
        var result = await this.ShowPopupAsync<bool>(popup);
        if (!result.Result) return;
        var context = new ContractParametersContext(null!, tx, protocolSettings.Network);
        if (!wallet.Sign(context) || !context.Completed)
        {
            await Toast.Show(Strings.SignTransactionFailed);
            return;
        }
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
