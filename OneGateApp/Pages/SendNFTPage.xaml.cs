using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Extensions;
using Neo;
using Neo.Network.P2P.Payloads;
using Neo.SmartContract;
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
using Contact = NeoOrder.OneGate.Data.Contact;

namespace NeoOrder.OneGate.Pages;

public partial class SendNFTPage : ContentPage, IQueryAttributable
{
    readonly IServiceProvider serviceProvider;
    readonly ProtocolSettings protocolSettings;
    readonly Wallet wallet;
    readonly RpcClient rpcClient;
    readonly AddressBookService addressBookService;

    public required NFT NFT { get; set { field = value; OnPropertyChanged(); } }
    public string? ToAddress { get; set { field = value; OnPropertyChanged(); } }
    public string? ResolvedToAddress { get; set; }

    public SendNFTPage(IServiceProvider serviceProvider, ProtocolSettings protocolSettings, IWalletProvider walletProvider, RpcClient rpcClient, AddressBookService addressBookService)
    {
        this.serviceProvider = serviceProvider;
        this.protocolSettings = protocolSettings;
        this.wallet = walletProvider.GetWallet()!;
        this.rpcClient = rpcClient;
        this.addressBookService = addressBookService;
        InitializeComponent();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        NFT ??= (NFT)query["nft"];
        if (query.TryGetValue("address", out var address))
        {
            ToAddress = (string)address;
        }
    }

    async void OnSelectContact(object sender, EventArgs e)
    {
        var popup = serviceProvider.GetServiceOrCreateInstance<SelectContactPopup>();
        var result = await this.ShowPopupAsync<Contact?>(popup);
        if (result.Result is not null)
        {
            ResolvedToAddress = result.Result.Address;
            ToAddress = result.Result.IsAddressBookEntry && !string.IsNullOrWhiteSpace(result.Result.Label)
                ? result.Result.Label
                : result.Result.Address;
        }
    }

    void OnValidateAddress(object sender, CustomValidationEventArgs e)
    {
        if (e.Value is not string address)
        {
            e.IsValid = false;
            return;
        }
        ResolvedToAddress = addressBookService.ResolveAddress(address);
        e.IsValid = ResolvedToAddress is not null;
    }

    async void OnSubmitted(object sender, EventArgs e)
    {
        Submit submit = (Submit)sender;
        using (submit.EnterBusyState())
        {
            WalletAccount account = wallet.GetDefaultAccount()!;
            UInt160 from = account.ScriptHash;
            string toAddress = ResolvedToAddress ?? addressBookService.ResolveAddress(ToAddress) ?? ToAddress!;
            UInt160 to = toAddress.ToScriptHash(protocolSettings.AddressVersion);
            TransactionIntent[] intents = [new Nep11TransferIntent
            {
                Asset = NFT,
                From = from,
                To = to
            }];
            Transaction tx;
            try
            {
                tx = await rpcClient.MakeTransactionAsync(NFT.CollectionId, NFT.TokenId, to, null);
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
            if (!wallet.Sign(context) || !context.Completed)
                await Toast.Show(Strings.SignTransactionFailed);
            tx.Witnesses = context.GetWitnesses();
            try
            {
                await rpcClient.SendRawTransaction(tx);
                await addressBookService.RecordTransferAsync(
                    toAddress,
                    tx.Hash.ToString(),
                    NFT.TokenInfo?.Symbol ?? "NFT",
                    NFT.Name,
                    "NFT");
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
