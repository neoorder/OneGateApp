using Neo;
using Neo.Wallets;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using NeoOrder.OneGate.Services.RPC;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text.Json.Nodes;
using System.Windows.Input;

namespace NeoOrder.OneGate.Pages;

public partial class TransactionHistoryPage : ContentPage
{
    const int DisplayLimit = 50;

    readonly IWalletProvider walletProvider;
    readonly ProtocolSettings protocolSettings;
    readonly RpcClient rpcClient;
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

    public TransactionHistoryPage(IWalletProvider walletProvider, ProtocolSettings protocolSettings, RpcClient rpcClient)
    {
        this.walletProvider = walletProvider;
        this.protocolSettings = protocolSettings;
        this.rpcClient = rpcClient;
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

    async Task<IEnumerable<RawTransfer>> LoadNep17TransfersAsync(string address)
    {
        JsonObject result = await rpcClient.RpcSendAsync<JsonObject>("getnep17transfers", address);
        return EnumerateTransfers(result, isNep11: false);
    }

    async Task<IEnumerable<RawTransfer>> LoadNep11TransfersAsync(string address)
    {
        JsonObject result = await rpcClient.RpcSendAsync<JsonObject>("getnep11transfers", address);
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
            CounterpartyText = string.IsNullOrWhiteSpace(transfer.Counterparty) ? ShortHash(transfer.AssetHash.ToString()) : transfer.Counterparty,
            TimeText = timeText,
            BlockText = transfer.BlockIndex is null ? null : $"#{transfer.BlockIndex}",
            TransactionHash = ShortHash(transfer.TransactionHash)
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
                return value.ToString();
        }
        return null;
    }

    static long ReadInt64(JsonObject obj, string key)
    {
        return obj[key] is JsonValue value && long.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long result)
            ? result
            : 0;
    }

    static uint? ReadUInt32(JsonObject obj, params string[] keys)
    {
        foreach (string key in keys)
        {
            if (obj[key] is JsonValue value && uint.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out uint result))
                return result;
        }
        return null;
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
}
