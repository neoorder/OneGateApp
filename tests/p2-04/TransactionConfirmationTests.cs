using System.Text.Json;
using System.Text.Json.Nodes;
using NeoOrder.OneGate.Services.RPC;
using Xunit;

public class TransactionConfirmationTests
{
    static Task NoDelay(TimeSpan _, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task NetworkAndPayloadErrorsRecoverWithoutBroadcasting()
    {
        int attempts = 0;
        var methods = new List<string>();
        Task<JsonObject> Read(string method, CancellationToken _)
        {
            methods.Add(method);
            if (method == "getapplicationlog")
                return Task.FromResult(JsonNode.Parse("{\"executions\":[{\"vmstate\":\"HALT\"}]}")!.AsObject());
            return ++attempts switch
            {
                1 => Task.FromException<JsonObject>(new HttpRequestException("offline")),
                2 => Task.FromException<JsonObject>(new HttpRequestException("502")),
                3 => Task.FromException<JsonObject>(new JsonException("invalid JSON")),
                4 => Task.FromException<JsonObject>(new TaskCanceledException("HTTP timeout")),
                5 => Task.FromResult(JsonNode.Parse("{\"blocktime\":{}}")!.AsObject()),
                _ => Task.FromResult(JsonNode.Parse("{\"blocktime\":42}")!.AsObject())
            };
        }
        var result = await new TransactionConfirmation(Read, NoDelay).PollAsync(CancellationToken.None);
        Assert.True(result.Succeeded);
        Assert.Equal(42UL, result.BlockTime);
        Assert.Equal(6, attempts);
        Assert.DoesNotContain("sendrawtransaction", methods);
    }

    [Theory]
    [InlineData("{}", null)]
    [InlineData("{\"executions\":[]}", null)]
    [InlineData("{\"executions\":[{\"vmstate\":\"BREAK\"}]}", null)]
    [InlineData("{\"executions\":[{\"vmstate\":\"FAULT\"}]}", false)]
    [InlineData("{\"executions\":[{\"vmstate\":\"HALT\"}]}", true)]
    public async Task UnknownExecutionIsPending(string log, bool? expected)
    {
        var poller = new TransactionConfirmation((method, _) => Task.FromResult(JsonNode.Parse(
            method == "getrawtransaction" ? "{\"blocktime\":42}" : log)!.AsObject()), NoDelay);
        var result = await poller.PollAsync(CancellationToken.None);
        Assert.Equal(expected, result.Succeeded);
    }

    [Fact]
    public async Task NullResultRemainsPending()
    {
        var poller = new TransactionConfirmation((_, _) => Task.FromResult<JsonObject>(null!), NoDelay);
        Assert.Null((await poller.PollAsync(CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task CancelStopsInFlightReadAndAnotherPollCanStart()
    {
        var blocked = new TaskCompletionSource<JsonObject>();
        CancellationToken observedToken = default;
        using var cancellation = new CancellationTokenSource();
        var first = new TransactionConfirmation((_, token) =>
        {
            observedToken = token;
            token.Register(() => blocked.TrySetCanceled(token));
            return blocked.Task;
        }, NoDelay).PollAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        Assert.True(observedToken.CanBeCanceled);
        var next = new TransactionConfirmation((_, _) => Task.FromException<JsonObject>(new RpcException(-100, "not found")), NoDelay);
        Assert.Null((await next.PollAsync(CancellationToken.None)).Succeeded);
    }
}
