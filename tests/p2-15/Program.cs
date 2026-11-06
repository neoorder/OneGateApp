using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Services;
using NeoOrder.OneGate.Services.RPC;

var failures = new List<string>();
var tests = new List<(string Name, Func<Task> Run)>();

void Error(string name, string method, JsonArray? parameters, int expectedCode, Exception? exception = null)
{
    tests.Add((name, async () =>
    {
        var host = new TestHost { Exception = exception };
        var request = Request(method, parameters);
        JsonObject response = await new RpcServer(host).HandleRequestAsync(request);
        Equal(expectedCode, response["error"]?["code"]?.GetValue<int>(), response.ToJsonString());
        Equal("request-7", response["id"]?.GetValue<string>(), "Request ID must survive errors.");
        Check(!response.ContainsKey("result"), "Error responses must not contain a result.");
    }));
}

Error("invalid JSON parameter type", "Number", ["not-an-int"], 10002);
Error("converter format error", "Converted", ["not-a-number"], 10002);
Error("converter numeric overflow", "Converted", ["999999999999999999999999"], 10002);
Error("converter token mismatch", "Converted", [new JsonObject()], 10002);
Error("unsupported parameter target type", "Unsupported", [1], 10002);
Error("null for non-nullable value type", "Number", [null], 10002);
Error("missing non-nullable value", "Number", [], 10002);
Error("missing parameter list", "Number", null, 10002);
Error("unsupported method", "DoesNotExist", [], 10001);
Error("synchronous business error", "ThrowSync", [], 10003, new DapiException(10003, "Account not found"));
Error("asynchronous business error", "ThrowAsync", [], 10007, new DapiException(10007, "Insufficient funds"));
Error("synchronous node error", "ThrowSync", [], 10008, new RpcException(-100, "Unknown transaction"));
Error("asynchronous node error", "ThrowAsync", [], 10008, new RpcException(-32602, "Invalid node params"));
Error("HTTP transport failure", "ThrowAsync", [], 10008, new HttpRequestException("Service unavailable", null, HttpStatusCode.ServiceUnavailable));
Error("direct timeout", "ThrowSync", [], 10005, new TimeoutException("Timed out"));
Error("asynchronous timeout", "ThrowAsync", [], 10005, new TimeoutException("Timed out"));
Error("HTTP timeout cancellation", "ThrowAsync", [], 10005, new TaskCanceledException("Request canceled", new TimeoutException("Request timed out")));
Error("synchronous user cancellation", "ThrowSync", [], 10006, new OperationCanceledException());
Error("asynchronous user cancellation", "ThrowAsync", [], 10006, new OperationCanceledException());
Error("task cancellation without timeout", "ThrowAsync", [], 10006, new TaskCanceledException());
Error("nested reflection wrappers", "ThrowSync", [], 10008, new TargetInvocationException(new TargetInvocationException(new RpcException(-1, "Node rejected"))));
Error("unknown handler error", "ThrowAsync", [], 10000, new InvalidOperationException("Internal handler failure"));
Error("handler format error is not a binding error", "ThrowSync", [], 10000, new FormatException("Internal formatting failure"));

tests.Add(("node details and JSON ownership survive mapping", async () =>
{
    JsonObject upstream = JsonNode.Parse("""{"error":{"data":{"hash":"0x123","trace":[1,2]}}}""")!.AsObject();
    var exception = new RpcException(-100, "Unknown transaction", upstream["error"]!["data"]);
    JsonObject response = await new RpcServer(new TestHost { Exception = exception }).HandleRequestAsync(Request("ThrowAsync", []));
    JsonNode? data = response["error"]?["data"];
    Equal(10008, response["error"]?["code"]?.GetValue<int>(), response.ToJsonString());
    Equal(-100, data?["code"]?.GetValue<int>(), "Node error code must be preserved.");
    Equal("Unknown transaction", data?["message"]?.GetValue<string>(), "Node error message must be preserved.");
    Check(JsonNode.DeepEquals(upstream["error"]!["data"], data?["data"]), "Node error data must be preserved.");
    data!["data"]!["hash"] = "changed";
    Equal("0x123", upstream["error"]!["data"]!["hash"]?.GetValue<string>(), "Response must not mutate upstream JSON.");
}));

tests.Add(("business error retains invocation details", async () =>
{
    var exception = new DapiException(10004, "Contract execution failed", new InvocationResult("FAULT", "ABORT"));
    JsonObject response = await new RpcServer(new TestHost { Exception = exception }).HandleRequestAsync(Request("ThrowSync", []));
    Equal(10004, response["error"]?["code"]?.GetValue<int>(), response.ToJsonString());
    Equal("FAULT", response["error"]?["data"]?["state"]?.GetValue<string>(), "Invocation data missing.");
    Equal("ABORT", response["error"]?["data"]?["exception"]?.GetValue<string>(), "Invocation error missing.");
}));

tests.Add(("valid synchronous and asynchronous calls", async () =>
{
    var server = new RpcServer(new TestHost());
    foreach (string method in new[] { "Number", "NumberAsync" })
    {
        JsonObject response = await server.HandleRequestAsync(Request(method, [7]));
        Equal(7, response["result"]?.GetValue<int>(), response.ToJsonString());
        Check(!response.ContainsKey("error"), "Successful calls must not contain an error.");
    }
    JsonObject noArgs = await server.HandleRequestAsync(Request("NoArguments", null));
    Equal("ok", noArgs["result"]?.GetValue<string>(), noArgs.ToJsonString());
    JsonObject nullable = await server.HandleRequestAsync(Request("NullableArgument", [null]));
    Equal("default", nullable["result"]?.GetValue<string>(), nullable.ToJsonString());
}));

tests.Add(("malformed request envelope remains INVALID", async () =>
{
    foreach (string json in new[] { "{}", "{\"method\":7}", "{\"method\":\"Number\",\"params\":{}}", "{\"id\":{},\"method\":\"Number\"}" })
    {
        JsonObject response = await new RpcServer(new TestHost()).HandleRequestAsync(JsonNode.Parse(json)!.AsObject());
        Equal(10002, response["error"]?["code"]?.GetValue<int>(), response.ToJsonString());
        Check(response.ContainsKey("id"), "Malformed response must retain an ID field.");
    }
}));

foreach ((string name, Func<Task> run) in tests)
{
    try
    {
        await run();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        failures.Add(name);
        Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
    }
}
Console.WriteLine($"{tests.Count - failures.Count}/{tests.Count} passed");
return failures.Count == 0 ? 0 : 1;

static JsonObject Request(string method, JsonArray? parameters) => new()
{
    ["jsonrpc"] = "2.0",
    ["id"] = "request-7",
    ["method"] = method,
    ["params"] = parameters
};

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}, received {actual}. {message}");
}

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

public sealed class TestHost
{
    public Exception? Exception { get; init; }

    [RpcMethod]
    public int Number(int value) => value;

    [RpcMethod]
    public Task<int> NumberAsync(int value) => Task.FromResult(value);

    [RpcMethod]
    internal int Converted(StrictInteger value) => value.Value;

    [RpcMethod]
    internal int Unsupported(Stream value) => value.Length > 0 ? 1 : 0;

    [RpcMethod]
    public string NoArguments() => "ok";

    [RpcMethod]
    public string NullableArgument(string? value) => value ?? "default";

    [RpcMethod]
    public int ThrowSync() => throw Exception!;

    [RpcMethod]
    public Task<int> ThrowAsync() => Task.FromException<int>(Exception!);
}
