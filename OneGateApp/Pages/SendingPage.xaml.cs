using Neo;
using Neo.Network.P2P.Payloads;
using Neo.SmartContract.Native;
using NeoOrder.OneGate.Models.Intents;
using NeoOrder.OneGate.Services.RPC;
using System.Numerics;
using System.Text.Json.Nodes;

namespace NeoOrder.OneGate.Pages;

public partial class SendingPage : ContentPage, IQueryAttributable
{
    CancellationTokenSource? pollingCancellation;
    readonly RpcClient rpcClient;

    public required Transaction Transaction { get; set { field = value; OnPropertyChanged(null); } }
    public required TransactionIntent[] Intents { get; set { field = value; OnPropertyChanged(); } }
    public ulong? BlockTime { get; set { field = value; OnPropertyChanged(); } }
    public bool? Succeeded
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsConfirming));
            OnPropertyChanged(nameof(IsSucceeded));
            OnPropertyChanged(nameof(IsFailed));
            OnPropertyChanged(nameof(IsTimedOut));
        }
    }
    public bool TimedOut
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsConfirming));
            OnPropertyChanged(nameof(IsTimedOut));
        }
    }
    public bool IsConfirming => Succeeded is null && !TimedOut;
    public bool IsSucceeded => Succeeded == true;
    public bool IsFailed => Succeeded == false;
    public bool IsTimedOut => Succeeded is null && TimedOut;

    public long Fee => (Transaction?.SystemFee + Transaction?.NetworkFee) ?? 0;
    public BigDecimal DecimalFee => new((BigInteger)Fee, NativeContract.GAS.Decimals);
    public string DisplayFee => $"{DecimalFee} {NativeContract.GAS.Symbol}";
    public BigDecimal DecimalSystemFee => new((BigInteger)(Transaction?.SystemFee ?? 0), NativeContract.GAS.Decimals);
    public BigDecimal DecimalNetworkFee => new((BigInteger)(Transaction?.NetworkFee ?? 0), NativeContract.GAS.Decimals);
    public string FeeDetails => $"{DecimalSystemFee} (sys) + {DecimalNetworkFee} (net)";

    public SendingPage(RpcClient rpcClient)
    {
        this.rpcClient = rpcClient;
        InitializeComponent();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        Transaction = (Transaction)query["tx"];
        Intents = (TransactionIntent[])query["intents"];
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = QueryTransactionStatusAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        CancellationTokenSource? cancellation = pollingCancellation;
        pollingCancellation = null;
        cancellation?.Cancel();
    }

    async Task QueryTransactionStatusAsync()
    {
        if (pollingCancellation is not null || Succeeded.HasValue) return;

        using var cancellation = new CancellationTokenSource();
        pollingCancellation = cancellation;
        try
        {
            TimedOut = false;
            var poller = new TransactionConfirmation((method, token) => method == "getrawtransaction"
                ? rpcClient.RpcSendAsync<JsonObject>(method, token, Transaction.Hash, true)
                : rpcClient.RpcSendAsync<JsonObject>(method, token, Transaction.Hash));
            ConfirmationResult result = await poller.PollAsync(cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();
            BlockTime = result.BlockTime;
            Succeeded = result.Succeeded;
            TimedOut = !Succeeded.HasValue;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(pollingCancellation, cancellation))
                pollingCancellation = null;
        }
    }

    void OnRetry(object sender, EventArgs e)
    {
        _ = QueryTransactionStatusAsync();
    }
}
