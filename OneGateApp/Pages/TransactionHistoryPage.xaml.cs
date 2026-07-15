using Neo;
using Neo.SmartContract.Native;
using Neo.Wallets;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
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
    static readonly Uri OneGateExplorerApiUri = new("https://explorer.onegate.space/api");
    static readonly Uri N3IndexAccountsUri = new("https://api.n3index.dev/mainnet/accounts/");

    readonly IWalletProvider walletProvider;
    readonly ProtocolSettings protocolSettings;
    readonly HttpClient httpClient;
    bool hasLoaded;

    public IReadOnlyList<TransactionHistoryItem> Transactions { get; set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsEmpty)); } } = [];
    public bool IsLoading { get; set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsEmpty)); } }
    public bool IsRefreshing { get; set { field = value; OnPropertyChanged(); } }
    public bool HasLoadError { get; set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsEmpty)); } }
    public string LoadErrorMessage { get; set { field = value; OnPropertyChanged(); } } = "";
    public bool IsEmpty => !IsLoading && !HasLoadError && Transactions.Count == 0;
    public ICommand RefreshCommand { get; }

    public TransactionHistoryPage(IWalletProvider walletProvider, ProtocolSettings protocolSettings, HttpClient httpClient)
    {
        this.walletProvider = walletProvider;
        this.protocolSettings = protocolSettings;
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
            Transactions = accountTransactions;
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
        Task<JsonArray?> signedTask = LoadOneGateHistoryAsync("GetRawTransactionByAddress", new JsonObject
        {
            ["Address"] = scriptHash.ToString(),
            ["Limit"] = DisplayLimit,
            ["Skip"] = 0
        });
        Task<JsonArray?> nep17Task = LoadOneGateHistoryAsync("GetNep17TransferByAddress", new JsonObject
        {
            ["Address"] = scriptHash.ToString(),
            ["ExcludeBonusAndBurn"] = true,
            ["Limit"] = DisplayLimit,
            ["Skip"] = 0
        });
        Task<JsonArray?> nep11Task = LoadOneGateHistoryAsync("GetNep11TransferByAddress", new JsonObject
        {
            ["Address"] = scriptHash.ToString(),
            ["Limit"] = DisplayLimit,
            ["Skip"] = 0
        });
        await Task.WhenAll(signedTask, nep17Task, nep11Task);

        JsonArray? signedResult = await signedTask;
        IReadOnlyList<RawAccountTransaction> signedTransactions = signedResult is null
            ? await LoadN3IndexAccountTransactionsAsync(address)
            : EnumerateAccountTransactions(signedResult).ToArray();

        JsonArray? nep17Result = await nep17Task;
        JsonArray? nep11Result = await nep11Task;
        var transferTransactions = EnumerateAccountTransfers(nep17Result, scriptHash, address)
            .Concat(EnumerateAccountTransfers(nep11Result, scriptHash, address))
            .ToList();
        if (nep17Result is null || nep11Result is null)
            transferTransactions.AddRange(await LoadN3IndexAccountTransfersAsync(scriptHash, address));

        var transactions = new Dictionary<string, RawAccountTransaction>(StringComparer.OrdinalIgnoreCase);
        foreach (RawAccountTransaction transaction in signedTransactions.Concat(transferTransactions))
        {
            if (IsZeroTransactionHash(transaction.Hash))
                continue;

            transactions[transaction.Hash] = transactions.TryGetValue(transaction.Hash, out RawAccountTransaction? existing)
                ? MergeTransactions(existing, transaction)
                : transaction;
        }

        return transactions.Values
            .OrderByDescending(p => p.Timestamp)
            .ThenByDescending(p => p.BlockIndex ?? 0)
            .Take(DisplayLimit)
            .Select(CreateTransactionItem)
            .ToArray();
    }

    async Task<JsonArray?> LoadOneGateHistoryAsync(string method, JsonObject parameters)
    {
        try
        {
            var request = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = method,
                ["params"] = parameters
            };
            using var timeout = new CancellationTokenSource(AccountHistoryTimeout);
            using HttpResponseMessage response = await httpClient.PostAsJsonAsync(OneGateExplorerApiUri, request, SharedOptions.JsonSerializerOptions, timeout.Token);
            if (!response.IsSuccessStatusCode)
                return null;

            JsonObject? payload = await response.Content.ReadFromJsonAsync<JsonObject>(SharedOptions.JsonSerializerOptions, timeout.Token);
            if (payload?["error"] is not null)
                return null;

            return payload?["result"]?["result"] as JsonArray;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            Debug.WriteLine(ex);
            return null;
        }
    }

    async Task<IReadOnlyList<RawAccountTransaction>> LoadN3IndexAccountTransactionsAsync(string address)
    {
        try
        {
            Uri uri = new(N3IndexAccountsUri, $"{Uri.EscapeDataString(address)}/transactions?limit={DisplayLimit}&offset=0");
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

    async Task<IReadOnlyList<RawAccountTransaction>> LoadN3IndexAccountTransfersAsync(UInt160 scriptHash, string address)
    {
        try
        {
            Uri uri = new(N3IndexAccountsUri, $"{Uri.EscapeDataString(address)}/transfers?limit={DisplayLimit}&offset=0");
            using var timeout = new CancellationTokenSource(AccountHistoryTimeout);
            JsonObject? payload = await httpClient.GetFromJsonAsync<JsonObject>(uri, SharedOptions.JsonSerializerOptions, timeout.Token);
            return EnumerateAccountTransfers(payload?["data"] as JsonArray, scriptHash, address).ToArray();
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
                Counterparty = ReadString(transaction, "sender", "sender_address") ?? "",
                NetworkFee = ReadInt64(transaction, "netfee", "net_fee"),
                SystemFee = ReadInt64(transaction, "sysfee", "sys_fee"),
                VmState = ReadString(transaction, "vmstate", "vm_state") ?? "",
                HasTransferDetails = false
            };
        }
    }

    IEnumerable<RawAccountTransaction> EnumerateAccountTransfers(JsonArray? transfers, UInt160 scriptHash, string address)
    {
        if (transfers is null)
            yield break;

        foreach (JsonNode? node in transfers)
        {
            if (node is not JsonObject transfer)
                continue;

            string? hash = ReadString(transfer, "txid", "hash");
            if (string.IsNullOrWhiteSpace(hash) || IsZeroTransactionHash(hash))
                continue;

            string from = ReadString(transfer, "from", "from_address") ?? "";
            string to = ReadString(transfer, "to", "to_address") ?? "";
            string counterparty = IsAccountIdentifier(from, scriptHash, address) ? to : from;

            yield return new RawAccountTransaction
            {
                Hash = hash,
                Timestamp = ReadInt64(transfer, "timestamp", "timestamp_ms", "blocktime", "block_time_ms"),
                BlockIndex = ReadUInt32(transfer, "blockIndex", "block_index"),
                Counterparty = NormalizeAddress(counterparty),
                NetworkFee = ReadInt64(transfer, "netfee", "net_fee"),
                SystemFee = ReadInt64(transfer, "sysfee", "sys_fee"),
                VmState = ReadString(transfer, "vmstate", "vm_state") ?? "",
                HasTransferDetails = true
            };
        }
    }

    static RawAccountTransaction MergeTransactions(RawAccountTransaction existing, RawAccountTransaction candidate)
    {
        string counterparty = existing.HasTransferDetails
            ? existing.Counterparty
            : candidate.HasTransferDetails
                ? candidate.Counterparty
                : string.IsNullOrWhiteSpace(existing.Counterparty) ? candidate.Counterparty : existing.Counterparty;

        return new RawAccountTransaction
        {
            Hash = existing.Hash,
            Timestamp = Math.Max(existing.Timestamp, candidate.Timestamp),
            BlockIndex = existing.BlockIndex ?? candidate.BlockIndex,
            Counterparty = counterparty,
            NetworkFee = Math.Max(existing.NetworkFee, candidate.NetworkFee),
            SystemFee = Math.Max(existing.SystemFee, candidate.SystemFee),
            VmState = IsKnownVmState(existing.VmState) ? existing.VmState : candidate.VmState,
            HasTransferDetails = existing.HasTransferDetails || candidate.HasTransferDetails
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
        string stateText = transaction.VmState.ToUpperInvariant() switch
        {
            "HALT" => Strings.Success,
            "FAULT" => Strings.Failed,
            _ => Strings.Unavailable
        };

        return new TransactionHistoryItem
        {
            Title = Strings.TransactionDetails,
            AmountText = feeText,
            DirectionText = stateText,
            CounterpartyText = string.IsNullOrWhiteSpace(transaction.Counterparty) ? Strings.Unavailable : transaction.Counterparty,
            TimeText = timeText,
            BlockText = transaction.BlockIndex is null ? null : $"#{transaction.BlockIndex}",
            TransactionHash = ShortHash(transaction.Hash)
        };
    }

    static string ShortHash(string value)
    {
        return value.Length <= 14 ? value : $"{value[..6]}...{value[^6..]}";
    }

    static bool IsZeroTransactionHash(string value)
    {
        string hash = value.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? value[2..] : value;
        return hash.Length > 0 && hash.All(p => p == '0');
    }

    static bool IsKnownVmState(string value)
    {
        return value.Equals("HALT", StringComparison.OrdinalIgnoreCase)
            || value.Equals("FAULT", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsAccountIdentifier(string value, UInt160 scriptHash, string address)
    {
        return string.Equals(value, address, StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, scriptHash.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    string NormalizeAddress(string value)
    {
        if (UInt160.TryParse(value, out UInt160? hash))
            return hash.ToAddress(protocolSettings.AddressVersion);
        return value;
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

    sealed class RawAccountTransaction
    {
        public required string Hash { get; init; }
        public required long Timestamp { get; init; }
        public uint? BlockIndex { get; init; }
        public required string Counterparty { get; init; }
        public required long NetworkFee { get; init; }
        public required long SystemFee { get; init; }
        public required string VmState { get; init; }
        public required bool HasTransferDetails { get; init; }
    }
}
