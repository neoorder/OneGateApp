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
    Contact? selectedRecipient;
    bool suppressAddressChange;

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
            SelectRecipient(result.Result);
        }
    }

    void OnAddressTextChanged(object sender, TextChangedEventArgs e)
    {
        if (suppressAddressChange) return;
        if (selectedRecipient is not null && IsTextForRecipient(e.NewTextValue, selectedRecipient)) return;
        selectedRecipient = null;
        ResolvedToAddress = null;
    }

    void SelectRecipient(Contact contact)
    {
        suppressAddressChange = true;
        selectedRecipient = contact;
        ResolvedToAddress = contact.Address;
        ToAddress = contact.IsAddressBookEntry && !string.IsNullOrWhiteSpace(contact.Label)
            ? contact.Label
            : contact.Address;
        suppressAddressChange = false;
    }

    void OnValidateAddress(object sender, CustomValidationEventArgs e)
    {
        if (e.Value is not string address)
        {
            e.IsValid = false;
            return;
        }
        e.IsValid = ResolveRecipientForText(address) is not null;
    }

    async void OnSubmitted(object sender, EventArgs e)
    {
        Submit submit = (Submit)sender;
        using (submit.EnterBusyState())
        {
            WalletAccount account = wallet.GetDefaultAccount()!;
            UInt160 from = account.ScriptHash;
            string? toAddress = ResolveCurrentRecipient();
            if (toAddress is null)
            {
                await Toast.Show(string.Format(Strings.DefaultValidatorErrorMessage, Strings.ReceivingAddress));
                return;
            }
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
            }
            catch (Exception ex)
            {
                await Toast.Show(ex.Message);
                return;
            }
            try
            {
                await addressBookService.RecordTransferAsync(
                    toAddress,
                    tx.Hash.ToString(),
                    NFT.TokenInfo?.Symbol ?? "NFT",
                    NFT.Name,
                    "NFT");
            }
            catch
            {
                // The transaction was already relayed; local history must not turn success into a send failure.
            }
            GlobalStates.Invalidate<WalletPage>();
            await Shell.Current.GoToAsync("//wallet/sending", new Dictionary<string, object>
            {
                ["tx"] = tx,
                ["intents"] = intents
            });
        }
    }

    string? ResolveCurrentRecipient()
    {
        string? address = ResolveRecipientForText(ToAddress);
        ResolvedToAddress = address;
        return address;
    }

    string? ResolveRecipientForText(string? text)
    {
        if (selectedRecipient is not null && IsTextForRecipient(text, selectedRecipient))
            return selectedRecipient.Address;
        return addressBookService.ResolveAddress(text);
    }

    static bool IsTextForRecipient(string? text, Contact contact)
    {
        string value = text?.Trim() ?? "";
        return string.Equals(value, contact.Address, StringComparison.OrdinalIgnoreCase) ||
            contact.IsAddressBookEntry &&
            !string.IsNullOrWhiteSpace(contact.Label) &&
            string.Equals(value, contact.Label.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
