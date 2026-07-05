using Neo;
using Neo.SmartContract.Native;
using Neo.Wallets;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using NeoOrder.OneGate.Services.RPC;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Json;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Input;

namespace NeoOrder.OneGate.Pages;

public partial class TransactionHistoryPage : ContentPage
{
    const int DisplayLimit = 50;
    static readonly TimeSpan AccountHistoryTimeout = TimeSpan.FromSeconds(6);
    const long EarliestTransferTimestamp = 1;
    static readonly Uri OneGateExplorerApiUri = new("https://explorer.onegate.space/api");
    static readonly Uri N3IndexAccountTransactionsUri = new("https://api.n3index.dev/mainnet/accounts/");

    readonly IWalletProvider walletProvider;
    readonly ProtocolSettings protocolSettings;
    readonly RpcClient rpcClient;
    readonly HttpClient httpClient;
    readonly Dictionary<UInt160, TokenInfo?> nep17TokenCache = [];
    readonly Dictionary<UInt160, Nep11TokenInfo?> nep11TokenCache = [];
    bool hasLoaded;

    public IReadOnlyList<TransactionHistoryItem> Transactions { get; set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsEmpty)); } } = [];
    public bool IsLoading { get; set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsEmpty)); } }
    public bool IsRefreshing { get; set { field = value; OnPropertyChanged(); } }
    public bool HasLoadError { get; set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsEmpty)); } }
    public string LoadErrorMessage { get; set { field = value; OnPropertyChanged(); } } = "";
    public bool IsEmpty => !IsLoading && !HasLoadError && Transactions.Count == 0;
    public ICommand RefreshCommand { get; }

    public TransactionHistoryPage(IWalletProvider walletProvider, ProtocolSettings protocolSettings, RpcClient rpcClient, HttpClient httpClient)
    {
        this.walletProvider = walletProvider;
        this.protocolSettings = protocolSettings;
        this.rpcClient = rpcClient;
        this.httpClient = httpClient;
        RefreshCommand = new Command(async () => await LoadTransactionsAsync(true), () => !IsLoading);
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (!hasLoaded)
            _ = LoadTransactionsAsync(false);
    }

    async Task LoadTransactionsAsync(bool isRefresh)
    {
        if (IsLoading) return;
        IsLoading = true;
        IsRefreshing = isRefresh;
        HasLoadError = false;
        ((Command)RefreshCommand).ChangeCanExecute();
        try
        {
            WalletAccount account = walletProvider.GetWallet()!.GetDefaultAccount()!;
            string address = account.ScriptHash.ToAddress(protocolSettings.AddressVersion);
            IReadOnlyList<TransactionHistoryItem> accountTransactions = await LoadAccountTransactionsAsync(account.ScriptHash, address);
            if (accountTransactions.Count > 0)
            {
                Transactions = accountTransactions;
                hasLoaded = true;
                return;
            }

            List<RawTransfer> transfers = [];
            transfers.AddRange(await LoadNep17TransfersAsync(address));
            transfers.AddRange(await LoadNep11TransfersAsync(address));

            List<TransactionHistoryItem> items = [];
            foreach (RawTransfer transfer in transfers
                .OrderByDescending(p => p.Timestamp)
                .ThenByDescending(p => p.BlockIndex ?? 0)
                .Take(DisplayLimit))
            {
                items.Add(await CreateItemAsync(transfer));
            }

            Transactions = items;
            hasLoaded = true;
        }
        catch (Exception ex)
        {
            LoadErrorMessage = ex.Message;
            HasLoadError = true;
        }
        finally
        {
            IsLoading = false;
            IsRefreshing = false;
            ((Command)RefreshCommand).ChangeCanExecute();
        }
    }

    async Task<IReadOnlyList<TransactionHistoryItem>> LoadAccountTransactionsAsync(UInt160 scriptHash, string address)
    {
        IReadOnlyList<RawAccountTransaction> transactions = await LoadOneGateAccountTransactionsAsync(scriptHash);
        if (transactions.Count == 0)
            transactions = await LoadN3IndexAccountTransactionsAsync(address);

        return transactions
            .OrderByDescending(p => p.Timestamp)
            .ThenByDescending(p => p.BlockIndex ?? 0)
            .Take(DisplayLimit)
            .Select(CreateTransactionItem)
            .ToArray();
    }

    async Task<IReadOnlyList<RawAccountTransaction>> LoadOneGateAccountTransactionsAsync(UInt160 scriptHash)
    {
        try
        {
            var request = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = "GetRawTransactionByAddress",
                ["params"] = new JsonObject
                {
                    ["Address"] = scriptHash.ToString(),
                    ["Limit"] = DisplayLimit,
                    ["Skip"] = 0
                }
            };
            using var timeout = new CancellationTokenSource(AccountHistoryTimeout);
            using HttpResponseMessage response = await httpClient.PostAsJsonAsync(OneGateExplorerApiUri, request, SharedOptions.JsonSerializerOptions, timeout.Token);
            if (!response.IsSuccessStatusCode)
                return [];

            JsonObject? payload = await response.Content.ReadFromJsonAsync<JsonObject>(SharedOptions.JsonSerializerOptions, timeout.Token);
            if (payload?["error"] is not null)
                return [];

            return EnumerateAccountTransactions(payload?["result"]?["result"] as JsonArray).ToArray();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            Debug.WriteLine(ex);
            return [];
        }
    }

    async Task<IReadOnlyList<RawAccountTransaction>> LoadN3IndexAccountTransactionsAsync(string address)
    {
        try
        {
            Uri uri = new(N3IndexAccountTransactionsUri, $"{Uri.EscapeDataString(address)}/transactions?limit={DisplayLimit}&offset=0");
            using var timeout = new CancellationTokenSource(AccountHistoryTimeout);
            JsonObject? payload = await httpClient.GetFromJsonAsync<JsonObject>(uri, SharedOptions.JsonSerializerOptions, timeout.Token);
            return EnumerateAccountTransactions(payload?["data"] as JsonArray).ToArray();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            Debug.WriteLine(ex);
            return [];
        }
    }

    static IEnumerable<RawAccountTransaction> EnumerateAccountTransactions(JsonArray? transactions)
    {
        if (transactions is null)
            yield break;

        foreach (JsonNode? node in transactions)
        {
            if (node is not JsonObject transaction)
                continue;

            string? hash = ReadString(transaction, "hash", "txid");
            if (string.IsNullOrWhiteSpace(hash))
                continue;

            yield return new RawAccountTransaction
            {
                Hash = hash,
                Timestamp = ReadInt64(transaction, "blocktime", "block_time_ms", "timestamp"),
                BlockIndex = ReadUInt32(transaction, "blockIndex", "block_index"),
                Sender = ReadString(transaction, "sender", "sender_address") ?? "",
                NetworkFee = ReadInt64(transaction, "netfee", "net_fee"),
                SystemFee = ReadInt64(transaction, "sysfee", "sys_fee"),
                VmState = ReadString(transaction, "vmstate", "vm_state") ?? ""
            };
        }
    }

    async Task<IEnumerable<RawTransfer>> LoadNep17TransfersAsync(string address)
    {
        long endTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        JsonObject result = await rpcClient.RpcSendAsync<JsonObject>("getnep17transfers", address, EarliestTransferTimestamp, endTime);
        return EnumerateTransfers(result, isNep11: false);
    }

    async Task<IEnumerable<RawTransfer>> LoadNep11TransfersAsync(string address)
    {
        long endTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        JsonObject result = await rpcClient.RpcSendAsync<JsonObject>("getnep11transfers", address, EarliestTransferTimestamp, endTime);
        return EnumerateTransfers(result, isNep11: true);
    }

    static IEnumerable<RawTransfer> EnumerateTransfers(JsonObject result, bool isNep11)
    {
        foreach (RawTransfer transfer in EnumerateDirection(result, "received", isIncoming: true, isNep11))
            yield return transfer;
        foreach (RawTransfer transfer in EnumerateDirection(result, "sent", isIncoming: false, isNep11))
            yield return transfer;
    }

    static IEnumerable<RawTransfer> EnumerateDirection(JsonObject result, string key, bool isIncoming, bool isNep11)
    {
        if (result[key] is not JsonArray transfers)
            yield break;

        foreach (JsonNode? node in transfers)
        {
            if (node is not JsonObject transfer)
                continue;

            string? assetHash = ReadString(transfer, "assethash", "assetHash");
            string? txHash = ReadString(transfer, "txhash", "txHash");
            if (string.IsNullOrWhiteSpace(assetHash) || string.IsNullOrWhiteSpace(txHash))
                continue;

            yield return new RawTransfer
            {
                IsIncoming = isIncoming,
                IsNep11 = isNep11,
                AssetHash = UInt160.Parse(assetHash),
                Amount = ReadString(transfer, "amount"),
                TokenId = ReadString(transfer, "tokenid", "tokenId"),
                Counterparty = ReadString(transfer, "transferaddress", "transferAddress") ?? "",
                TransactionHash = txHash,
                Timestamp = ReadInt64(transfer, "timestamp"),
                BlockIndex = ReadUInt32(transfer, "blockindex", "blockIndex")
            };
        }
    }

    async Task<TransactionHistoryItem> CreateItemAsync(RawTransfer transfer)
    {
        return transfer.IsNep11
            ? await CreateNep11ItemAsync(transfer)
            : await CreateNep17ItemAsync(transfer);
    }

    async Task<TransactionHistoryItem> CreateNep17ItemAsync(RawTransfer transfer)
    {
        TokenInfo? token = await GetNep17TokenAsync(transfer.AssetHash);
        string symbol = token?.Symbol ?? ShortHash(transfer.AssetHash.ToString());
        string amount = FormatNep17Amount(transfer.Amount, token?.Decimals ?? 0);
        string sign = transfer.IsIncoming ? "+" : "-";
        return CreateItem(transfer, $"{DirectionVerb(transfer)} {symbol}", $"{sign}{amount} {symbol}");
    }

    async Task<TransactionHistoryItem> CreateNep11ItemAsync(RawTransfer transfer)
    {
        Nep11TokenInfo? token = await GetNep11TokenAsync(transfer.AssetHash);
        string symbol = token?.Symbol ?? ShortHash(transfer.AssetHash.ToString());
        string tokenId = string.IsNullOrWhiteSpace(transfer.TokenId) ? "" : $" #{ShortText(transfer.TokenId)}";
        return CreateItem(transfer, $"{DirectionVerb(transfer)} {symbol}", $"{symbol}{tokenId}");
    }

    TransactionHistoryItem CreateItem(RawTransfer transfer, string title, string amountText)
    {
        string timeText = transfer.Timestamp > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(transfer.Timestamp).LocalDateTime.ToString("g", CultureInfo.CurrentCulture)
            : Strings.Time;
        return new TransactionHistoryItem
        {
            Title = title,
            AmountText = amountText,
            DirectionText = transfer.IsIncoming ? Strings.Receive : Strings.Send,
            CounterpartyText = string.IsNullOrWhiteSpace(transfer.Counterparty) ? Strings.Unavailable : transfer.Counterparty,
            TimeText = timeText,
            BlockText = transfer.BlockIndex is null ? null : $"#{transfer.BlockIndex}",
            TransactionHash = ShortHash(transfer.TransactionHash)
        };
    }

    static TransactionHistoryItem CreateTransactionItem(RawAccountTransaction transaction)
    {
        string timeText = transaction.Timestamp > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(transaction.Timestamp).LocalDateTime.ToString("g", CultureInfo.CurrentCulture)
            : Strings.Time;
        long fee = Math.Max(0, transaction.NetworkFee) + Math.Max(0, transaction.SystemFee);
        string feeText = fee > 0
            ? $"{new BigDecimal(new BigInteger(fee), NativeContract.GAS.Decimals)} {NativeContract.GAS.Symbol}"
            : Strings.Unavailable;
        string stateText = string.Equals(transaction.VmState, "FAULT", StringComparison.OrdinalIgnoreCase)
            ? Strings.Failed
            : Strings.Success;

        return new TransactionHistoryItem
        {
            Title = Strings.TransactionDetails,
            AmountText = feeText,
            DirectionText = stateText,
            CounterpartyText = string.IsNullOrWhiteSpace(transaction.Sender) ? Strings.Unavailable : transaction.Sender,
            TimeText = timeText,
            BlockText = transaction.BlockIndex is null ? null : $"#{transaction.BlockIndex}",
            TransactionHash = ShortHash(transaction.Hash)
        };
    }

    async Task<TokenInfo?> GetNep17TokenAsync(UInt160 assetHash)
    {
        if (nep17TokenCache.TryGetValue(assetHash, out TokenInfo? cached))
            return cached;

        try
        {
            cached = await rpcClient.GetTokenInfo(assetHash);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            cached = null;
        }
        nep17TokenCache[assetHash] = cached;
        return cached;
    }

    async Task<Nep11TokenInfo?> GetNep11TokenAsync(UInt160 assetHash)
    {
        if (nep11TokenCache.TryGetValue(assetHash, out Nep11TokenInfo? cached))
            return cached;

        try
        {
            cached = await rpcClient.GetNep11TokenInfo(assetHash);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            cached = null;
        }
        nep11TokenCache[assetHash] = cached;
        return cached;
    }

    static string FormatNep17Amount(string? value, byte decimals)
    {
        return BigInteger.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger amount)
            ? new BigDecimal(amount, decimals).ToString()
            : value ?? "0";
    }

    static string DirectionVerb(RawTransfer transfer)
    {
        return transfer.IsIncoming ? Strings.Receive : Strings.Send;
    }

    static string ShortHash(string value)
    {
        return value.Length <= 14 ? value : $"{value[..6]}...{value[^6..]}";
    }

    static string ShortText(string value)
    {
        return value.Length <= 12 ? value : $"{value[..6]}...{value[^4..]}";
    }

    static string? ReadString(JsonObject obj, params string[] keys)
    {
        foreach (string key in keys)
        {
            if (obj[key] is JsonValue value)
                return ReadJsonScalar(value);
        }
        return null;
    }

    static long ReadInt64(JsonObject obj, params string[] keys)
    {
        foreach (string key in keys)
        {
            if (obj[key] is JsonValue value && long.TryParse(ReadJsonScalar(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out long result))
                return result;
        }
        return 0;
    }

    static uint? ReadUInt32(JsonObject obj, params string[] keys)
    {
        foreach (string key in keys)
        {
            if (obj[key] is JsonValue value && uint.TryParse(ReadJsonScalar(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint result))
                return result;
        }
        return null;
    }

    static string ReadJsonScalar(JsonValue value)
    {
        return value.TryGetValue(out string? text)
            ? text ?? string.Empty
            : value.ToString();
    }

    sealed class RawTransfer
    {
        public required bool IsIncoming { get; init; }
        public required bool IsNep11 { get; init; }
        public required UInt160 AssetHash { get; init; }
        public string? Amount { get; init; }
        public string? TokenId { get; init; }
        public required string Counterparty { get; init; }
        public required string TransactionHash { get; init; }
        public required long Timestamp { get; init; }
        public uint? BlockIndex { get; init; }
    }

    sealed class RawAccountTransaction
    {
        public required string Hash { get; init; }
        public required long Timestamp { get; init; }
        public uint? BlockIndex { get; init; }
        public required string Sender { get; init; }
        public required long NetworkFee { get; init; }
        public required long SystemFee { get; init; }
        public required string VmState { get; init; }
    }
}
