using Neo;
using Neo.Network.P2P.Payloads;
using Neo.SmartContract.Native;
using Neo.VM;
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
    readonly Wallet wallet;

    public string Title { get; set { field = value; OnPropertyChanged(); } } = Strings.SendTransaction;
    public string Message { get; set { field = value; OnPropertyChanged(); } } = Strings.SendTransactionText;
    public required Transaction Transaction { get; set { field = value; OnPropertyChanged(null); RefreshPreview(); } }
    public TransactionIntent[]? Intents { get { return field; } set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasIntents)); RefreshPreview(); } }
    public InvocationResult? InvocationResult { get { return field; } set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasExecutionWarning)); RefreshPreview(); } }
    public TransactionPreviewAssetChange[] AssetChanges { get; private set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasAssetChanges)); } } = [];
    public TransactionPreviewWarning[] RiskWarnings { get; private set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasRiskWarnings)); } } = [];

    public long Fee => (Transaction?.SystemFee + Transaction?.NetworkFee) ?? 0;
    public BigDecimal DecimalFee => new((BigInteger)Fee, NativeContract.GAS.Decimals);
    public string DisplayFee => $"{DecimalFee} {NativeContract.GAS.Symbol}";
    public BigDecimal DecimalSystemFee => new((BigInteger)(Transaction?.SystemFee ?? 0), NativeContract.GAS.Decimals);
    public BigDecimal DecimalNetworkFee => new((BigInteger)(Transaction?.NetworkFee ?? 0), NativeContract.GAS.Decimals);
    public string FeeDetails => $"{DecimalSystemFee} (sys) + {DecimalNetworkFee} (net)";
    public bool HasAssetChanges => AssetChanges.Length > 0;
    public bool HasIntents => Intents?.Length > 0;
    public bool HasRiskWarnings => RiskWarnings.Length > 0;
    public bool HasExecutionWarning => InvocationResult is { State: not VMState.HALT } || !string.IsNullOrWhiteSpace(InvocationResult?.Exception);

    public SendTransactionPopup(WalletAuthorizationService walletAuthorizationService, ProtocolSettings protocolSettings, IWalletProvider walletProvider)
    {
        this.walletAuthorizationService = walletAuthorizationService;
        this.protocolSettings = protocolSettings;
        this.wallet = walletProvider.GetWallet()!;
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

    void RefreshPreview()
    {
        AssetChanges = BuildAssetChanges();
        RiskWarnings = BuildRiskWarnings();
        OnPropertyChanged(nameof(Fee));
        OnPropertyChanged(nameof(DecimalFee));
        OnPropertyChanged(nameof(DisplayFee));
        OnPropertyChanged(nameof(DecimalSystemFee));
        OnPropertyChanged(nameof(DecimalNetworkFee));
        OnPropertyChanged(nameof(FeeDetails));
        OnPropertyChanged(nameof(HasExecutionWarning));
    }

    TransactionPreviewAssetChange[] BuildAssetChanges()
    {
        if (Intents is null || Intents.Length == 0) return [];

        List<TransactionPreviewAssetChange> changes = [];
        foreach (TransactionIntent intent in Intents)
        {
            switch (intent)
            {
                case TransferIntent transfer:
                    bool fromWallet = wallet.Contains(transfer.From);
                    bool toWallet = wallet.Contains(transfer.To);
                    changes.Add(new TransactionPreviewAssetChange
                    {
                        Title = transfer.Asset.Symbol,
                        AmountText = $"{GetAmountPrefix(fromWallet, toWallet)}{transfer.DisplayAmount}",
                        DetailText = GetTransferDetail(transfer.From, transfer.To, fromWallet, toWallet),
                        IsOutgoing = fromWallet && !toWallet,
                        IsIncoming = toWallet && !fromWallet
                    });
                    break;
                case Nep11TransferIntent transfer:
                    bool nftFromWallet = wallet.Contains(transfer.From);
                    bool nftToWallet = wallet.Contains(transfer.To);
                    changes.Add(new TransactionPreviewAssetChange
                    {
                        Title = transfer.Asset.Name,
                        AmountText = $"{GetAmountPrefix(nftFromWallet, nftToWallet)}{Strings.NFT}",
                        DetailText = GetTransferDetail(transfer.From, transfer.To, nftFromWallet, nftToWallet),
                        IsOutgoing = nftFromWallet && !nftToWallet,
                        IsIncoming = nftToWallet && !nftFromWallet
                    });
                    break;
            }
        }
        return changes.ToArray();
    }

    TransactionPreviewWarning[] BuildRiskWarnings()
    {
        List<TransactionPreviewWarning> warnings = [];
        if (Intents is null || Intents.Length == 0)
        {
            warnings.Add(new TransactionPreviewWarning
            {
                Title = Strings.UnknownAssetChanges,
                Message = Strings.UnknownAssetChangesText,
                IsHighRisk = true
            });
        }
        else if (Intents.Any(p => p is InvocationIntent))
        {
            warnings.Add(new TransactionPreviewWarning
            {
                Title = Strings.ContractRequest,
                Message = Strings.ContractRequestRiskText
            });
        }
        if (HasExecutionWarning)
        {
            warnings.Add(new TransactionPreviewWarning
            {
                Title = Strings.ExecutionWarning,
                Message = Strings.ExecutionWarningText,
                IsHighRisk = true
            });
        }
        if (Fee > 0)
        {
            warnings.Add(new TransactionPreviewWarning
            {
                Title = Strings.NetworkFeeNotice,
                Message = Strings.NetworkFeeNoticeText
            });
        }
        return warnings.ToArray();
    }

    string GetTransferDetail(UInt160 from, UInt160 to, bool fromWallet, bool toWallet)
    {
        if (fromWallet && !toWallet)
            return string.Format(Strings.SendingToFormat, ShortAddress(to));
        if (toWallet && !fromWallet)
            return string.Format(Strings.ReceivingFromFormat, ShortAddress(from));
        if (fromWallet && toWallet)
            return Strings.InternalWalletTransfer;
        return string.Format(Strings.ExternalTransferFormat, ShortAddress(from), ShortAddress(to));
    }

    string ShortAddress(UInt160 hash)
    {
        string address = hash.ToAddress(protocolSettings.AddressVersion);
        return address.Length <= 12 ? address : $"{address[..6]}...{address[^4..]}";
    }

    static string GetAmountPrefix(bool fromWallet, bool toWallet)
    {
        if (fromWallet && !toWallet) return "-";
        if (toWallet && !fromWallet) return "+";
        return "";
    }
}
