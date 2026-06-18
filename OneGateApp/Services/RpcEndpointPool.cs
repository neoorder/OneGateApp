using System.Text.Json;

namespace NeoOrder.OneGate.Services;

public sealed class RpcEndpointPool
{
    public static readonly Uri[] DefaultEndpoints =
    [
        new("https://n3seed1.ngd.network:10332"),
        new("https://n3seed2.ngd.network:10332"),
        new("https://n3seed3.ngd.network:10332"),
        new("https://n3seed4.ngd.network:10332"),
        new("https://n3seed5.ngd.network:10332")
    ];

    readonly Uri[] endpoints;
    int preferredIndex;

    public IReadOnlyList<Uri> Endpoints => endpoints;
    public Uri PreferredEndpoint => endpoints[Math.Clamp(Volatile.Read(ref preferredIndex), 0, endpoints.Length - 1)];

    public RpcEndpointPool(IEnumerable<Uri>? endpoints = null)
    {
        this.endpoints = (endpoints ?? DefaultEndpoints).Distinct().ToArray();
        if (this.endpoints.Length == 0)
            throw new ArgumentException("At least one RPC endpoint is required.", nameof(endpoints));
    }

    public async Task<T> SendAsync<T>(Func<Uri, Task<T>> operation)
    {
        int start = Math.Clamp(Volatile.Read(ref preferredIndex), 0, endpoints.Length - 1);
        List<Exception> failures = [];

        for (int attempt = 0; attempt < endpoints.Length; attempt++)
        {
            int index = (start + attempt) % endpoints.Length;
            Uri endpoint = endpoints[index];
            try
            {
                T result = await operation(endpoint);
                if (attempt > 0)
                    Interlocked.Exchange(ref preferredIndex, index);
                return result;
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                failures.Add(new InvalidOperationException($"{endpoint}: {ex.Message}", ex));
            }
        }

        throw new InvalidOperationException("All configured Neo RPC endpoints failed.", new AggregateException(failures));
    }

    static bool IsTransient(Exception ex)
    {
        return ex is HttpRequestException ||
            ex is TaskCanceledException ||
            ex is IOException ||
            ex is JsonException;
    }
}
