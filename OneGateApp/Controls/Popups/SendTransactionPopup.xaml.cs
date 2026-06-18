using Neo;
using Neo.Network.P2P.Payloads;
using Neo.SmartContract.Native;
using Neo.Wallets;
using NeoOrder.OneGate.Controls.Views;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Models.Intents;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using System.Numerics;

namespace NeoOrder.OneGate.Controls.Popups;

public partial class SendTransactionPopup : MyPopup<bool>
{
    readonly WalletAuthorizationService walletAuthorizationService;
    readonly ProtocolSettings protocolSettings;

    public string Title { get; set { field = value; OnPropertyChanged(); } } = Strings.SendTransaction;
    public string Message { get; set { field = value; OnPropertyChanged(); } } = Strings.SendTransactionText;
    public required Transaction Transaction { get; set { field = value; OnPropertyChanged(null); } }
    public TransactionIntent[]? Intents { get; set { field = value; OnPropertyChanged(); } }
    public InvocationResult? InvocationResult { get; set { field = value; OnPropertyChanged(); } }
    public string? Origin { get; set { field = value; OnPropertyChanged(null); } }

    public long Fee => (Transaction?.SystemFee + Transaction?.NetworkFee) ?? 0;
    public BigDecimal DecimalFee => new((BigInteger)Fee, NativeContract.GAS.Decimals);
    public string DisplayFee => $"{DecimalFee} {NativeContract.GAS.Symbol}";
    public BigDecimal DecimalSystemFee => new((BigInteger)(Transaction?.SystemFee ?? 0), NativeContract.GAS.Decimals);
    public BigDecimal DecimalNetworkFee => new((BigInteger)(Transaction?.NetworkFee ?? 0), NativeContract.GAS.Decimals);
    public string FeeDetails => $"{DecimalSystemFee} (sys) + {DecimalNetworkFee} (net)";
    public string DisplayOrigin => string.IsNullOrWhiteSpace(Origin) ? AppInfo.Name : Origin;
    public string DisplayNetwork => protocolSettings.Network.ToString();
    public string DisplayValidUntilBlock => Transaction?.ValidUntilBlock.ToString() ?? string.Empty;
    public string DisplayTransactionHash => Transaction?.Hash.ToString() ?? string.Empty;
    public string DisplaySigners => Transaction is null
        ? string.Empty
        : string.Join(Environment.NewLine, Transaction.Signers.Select(FormatSigner));

    public SendTransactionPopup(WalletAuthorizationService walletAuthorizationService, ProtocolSettings protocolSettings)
    {
        this.walletAuthorizationService = walletAuthorizationService;
        this.protocolSettings = protocolSettings;
        InitializeComponent();
    }

    string FormatSigner(Signer signer)
    {
        return $"{signer.Account.ToAddress(protocolSettings.AddressVersion)} · {signer.Scopes}";
    }

    async void OnContinue(object sender, EventArgs e)
    {
        SpinnerButton button = (SpinnerButton)sender;
        using (button.EnterBusyState())
        {
            if (await walletAuthorizationService.RequestAuthorizationAsync(this.FindAncestor<Page>(), Strings.SendTransaction))
                await CloseAsync(true);
        }
    }

    async void OnCancel(object sender, EventArgs e)
    {
        await CloseAsync(false);
    }
}
