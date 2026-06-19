using Neo;
using Neo.Network.P2P.Payloads;
using Neo.SmartContract.Native;
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

    public string Title { get; set { field = value; OnPropertyChanged(); } } = Strings.SendTransaction;
    public string Message { get; set { field = value; OnPropertyChanged(); } } = Strings.SendTransactionText;
    public required Transaction Transaction { get; set { field = value; OnPropertyChanged(null); } }
    public TransactionIntent[]? Intents { get; set { field = value; OnPropertyChanged(); } }
    public InvocationResult? InvocationResult { get; set { field = value; OnPropertyChanged(); } }

    public long Fee => (Transaction?.SystemFee + Transaction?.NetworkFee) ?? 0;
    public BigDecimal DecimalFee => new((BigInteger)Fee, NativeContract.GAS.Decimals);
    public string DisplayFee => $"{DecimalFee} {NativeContract.GAS.Symbol}";
    public BigDecimal DecimalSystemFee => new((BigInteger)(Transaction?.SystemFee ?? 0), NativeContract.GAS.Decimals);
    public BigDecimal DecimalNetworkFee => new((BigInteger)(Transaction?.NetworkFee ?? 0), NativeContract.GAS.Decimals);
    public string FeeDetails => $"{DecimalSystemFee} (sys) + {DecimalNetworkFee} (net)";

    public SendTransactionPopup(WalletAuthorizationService walletAuthorizationService)
    {
        this.walletAuthorizationService = walletAuthorizationService;
        InitializeComponent();
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
