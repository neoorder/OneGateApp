using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NeoOrder.OneGate.Services;

public sealed class TransactionHistoryDataSource(HttpClient httpClient)
{
    const int DisplayLimit = 50;
    static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);
    static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    static readonly Uri OneGateExplorerApiUri = new("https://explorer.onegate.space/api");
    static readonly Uri N3IndexAccountsUri = new("https://api.n3index.dev/mainnet/accounts/");

    readonly Dictionary<string, CacheEntry> cache = new(StringComparer.OrdinalIgnoreCase);
    readonly object cacheLock = new();

    public RequestSet GetRequests(string scriptHash, string address, bool forceRefresh = false)
    {
        lock (cacheLock)
        {
            if (!forceRefresh
                && cache.TryGetValue(address, out CacheEntry? entry)
                && DateTimeOffset.UtcNow - entry.CreatedAt < CacheDuration)
                return entry.Requests;

            RequestSet requests = CreateRequests(scriptHash, address);
            cache[address] = new(DateTimeOffset.UtcNow, requests);
            return requests;
        }
    }

    public void PrefetchTransfers(string scriptHash, string address)
    {
        RequestSet requests = GetRequests(scriptHash, address);
        _ = requests.OneGateNep17;
        _ = requests.N3IndexTransfers;
    }

    RequestSet CreateRequests(string scriptHash, string address)
    {
        return new(
            () => LoadOneGateHistoryAsync("GetRawTransactionByAddress", new JsonObject
            {
                ["Address"] = scriptHash,
                ["Limit"] = DisplayLimit,
                ["Skip"] = 0
            }),
            () => LoadOneGateHistoryAsync("GetNep17TransferByAddress", new JsonObject
            {
                ["Address"] = scriptHash,
                ["ExcludeBonusAndBurn"] = true,
                ["Limit"] = DisplayLimit,
                ["Skip"] = 0
            }),
            () => LoadOneGateHistoryAsync("GetNep11TransferByAddress", new JsonObject
            {
                ["Address"] = scriptHash,
                ["Limit"] = DisplayLimit,
                ["Skip"] = 0
            }),
            () => LoadN3IndexHistoryAsync(address, "transactions"),
            () => LoadN3IndexHistoryAsync(address, "transfers"));
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
            using var timeout = new CancellationTokenSource(RequestTimeout);
            using HttpResponseMessage response = await httpClient.PostAsJsonAsync(OneGateExplorerApiUri, request, SharedOptions.JsonSerializerOptions, timeout.Token);
            if (!response.IsSuccessStatusCode)
                return null;

            JsonObject? payload = await response.Content.ReadFromJsonAsync<JsonObject>(SharedOptions.JsonSerializerOptions, timeout.Token);
            if (payload?["error"] is not null)
                return null;

            return payload?["result"]?["result"] as JsonArray;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            Debug.WriteLine(ex);
            return null;
        }
    }

    async Task<JsonArray?> LoadN3IndexHistoryAsync(string address, string resource)
    {
        try
        {
            Uri uri = new(N3IndexAccountsUri, $"{Uri.EscapeDataString(address)}/{resource}?limit={DisplayLimit}&offset=0");
            using var timeout = new CancellationTokenSource(RequestTimeout);
            JsonObject? payload = await httpClient.GetFromJsonAsync<JsonObject>(uri, SharedOptions.JsonSerializerOptions, timeout.Token);
            return payload?["data"] as JsonArray;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException)
        {
            Debug.WriteLine(ex);
            return null;
        }
    }

    public sealed class RequestSet
    {
        readonly Lazy<Task<JsonArray?>> oneGateSigned;
        readonly Lazy<Task<JsonArray?>> oneGateNep17;
        readonly Lazy<Task<JsonArray?>> oneGateNep11;
        readonly Lazy<Task<JsonArray?>> n3IndexTransactions;
        readonly Lazy<Task<JsonArray?>> n3IndexTransfers;

        internal RequestSet(
            Func<Task<JsonArray?>> oneGateSigned,
            Func<Task<JsonArray?>> oneGateNep17,
            Func<Task<JsonArray?>> oneGateNep11,
            Func<Task<JsonArray?>> n3IndexTransactions,
            Func<Task<JsonArray?>> n3IndexTransfers)
        {
            this.oneGateSigned = new(oneGateSigned);
            this.oneGateNep17 = new(oneGateNep17);
            this.oneGateNep11 = new(oneGateNep11);
            this.n3IndexTransactions = new(n3IndexTransactions);
            this.n3IndexTransfers = new(n3IndexTransfers);
        }

        public Task<JsonArray?> OneGateSigned => oneGateSigned.Value;
        public Task<JsonArray?> OneGateNep17 => oneGateNep17.Value;
        public Task<JsonArray?> OneGateNep11 => oneGateNep11.Value;
        public Task<JsonArray?> N3IndexTransactions => n3IndexTransactions.Value;
        public Task<JsonArray?> N3IndexTransfers => n3IndexTransfers.Value;
    }

    sealed record CacheEntry(DateTimeOffset CreatedAt, RequestSet Requests);
}
