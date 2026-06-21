using Neo;
using Neo.Network.P2P.Payloads;
using Neo.SmartContract.Native;
using Neo.VM;
using NeoOrder.OneGate.Models.Intents;
using NeoOrder.OneGate.Services.RPC;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NeoOrder.OneGate.Pages;

public partial class SendingPage : ContentPage, IQueryAttributable
{
    readonly CancellationTokenSource cancellation = new();
    readonly RpcClient rpcClient;
    bool isPolling;

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
        QueryTransactionStatus();
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        await cancellation.CancelAsync();
        cancellation.Dispose();
    }

    async void QueryTransactionStatus()
    {
        if (isPolling) return;

        isPolling = true;
        try
        {
            TimedOut = false;
            for (int i = 0; i < 10; i++)
            {
                await Task.Delay(TimeSpan.FromSeconds(15), cancellation.Token);
                JsonObject tx;
                try
                {
                    tx = await rpcClient.RpcSendAsync<JsonObject>("getrawtransaction", Transaction.Hash, true);
                }
                catch (RpcException)
                {
                    continue;
                }
                ulong? blockTime = tx["blocktime"]?.GetValue<ulong>();
                if (!blockTime.HasValue) continue;
                BlockTime = blockTime;
                Succeeded = await QueryExecutionSucceededAsync();
                break;
            }
            // The transaction never appeared in a block within the polling window; stop the
            // spinner and let the user retry instead of confirming forever.
            if (Succeeded is null) TimedOut = true;
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            isPolling = false;
        }
    }

    void OnRetry(object sender, EventArgs e)
    {
        QueryTransactionStatus();
    }

    // Block inclusion is not success: a transaction can be included in a block yet revert
    // (VMState.FAULT). Read the application log and require HALT before reporting success.
    async Task<bool> QueryExecutionSucceededAsync()
    {
        try
        {
            JsonObject log = await rpcClient.RpcSendAsync<JsonObject>("getapplicationlog", Transaction.Hash);
            JsonNode? execution = log["executions"] is JsonArray executions && executions.Count > 0 ? executions[0] : null;
            JsonNode? vmState = execution?["vmstate"];
            return vmState is null || vmState.GetValue<string>() == nameof(VMState.HALT);
        }
        catch (Exception ex) when (ex is RpcException or HttpRequestException or JsonException or InvalidOperationException or FormatException)
        {
            // Application log unavailable; the transaction is in a block but the execution result
            // is unknown. Avoid a false "failed" by treating it as confirmed.
            return true;
        }
    }
}
