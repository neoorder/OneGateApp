using System.Text.Json;
using System.Text.Json.Nodes;

namespace NeoOrder.OneGate.Services.RPC;

internal sealed class TransactionConfirmation(
    Func<string, CancellationToken, Task<JsonObject>> read,
    Func<TimeSpan, CancellationToken, Task>? delay = null)
{
    public async Task<ConfirmationResult> PollAsync(CancellationToken cancellationToken)
    {
        ulong? blockTime = null;
        for (int attempt = 0; attempt < 10; attempt++)
        {
            await (delay ?? Task.Delay)(TimeSpan.FromSeconds(15), cancellationToken);
            try
            {
                JsonObject? tx = await read("getrawtransaction", cancellationToken);
                if (tx is null) continue;
                blockTime = tx["blocktime"]?.GetValue<ulong>();
                if (!blockTime.HasValue) continue;
                JsonObject? log = await read("getapplicationlog", cancellationToken);
                JsonNode? execution = log?["executions"] is JsonArray executions && executions.Count > 0 ? executions[0] : null;
                bool? succeeded = execution?["vmstate"]?.GetValue<string>() switch
                {
                    "HALT" => true,
                    "FAULT" => false,
                    _ => null
                };
                if (succeeded.HasValue) return new(blockTime, succeeded);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (ex is RpcException or HttpRequestException or JsonException
                or OperationCanceledException or InvalidOperationException or FormatException or OverflowException)
            {
                // A failed status read says nothing about a transaction already broadcast.
                // Keep it pending and only retry the read, never the broadcast.
            }
        }
        return new(blockTime, null);
    }
}

internal readonly record struct ConfirmationResult(ulong? BlockTime, bool? Succeeded);
