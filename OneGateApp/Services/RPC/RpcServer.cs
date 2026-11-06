using NeoOrder.OneGate.Models;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NeoOrder.OneGate.Services.RPC;

class RpcServer(object host)
{
    readonly Dictionary<string, MethodInfo> handlers = host.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
        .Select(p => (Method: p, Attribute: p.GetCustomAttribute<RpcMethodAttribute>()))
        .Where(p => p.Attribute != null)
        .Select(p => (p.Method, Name: p.Attribute!.Name ?? p.Method.Name))
        .ToDictionary(p => p.Name, p => p.Method, StringComparer.OrdinalIgnoreCase);

    public async Task<JsonObject> HandleRequestAsync(JsonObject request)
    {
        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0"
        };
        string method;
        JsonArray? args;
        try
        {
            response["id"] = request["id"]?.AsValue().DeepClone();
            method = request["method"]!.GetValue<string>();
            args = request["params"]?.AsArray();
        }
        catch
        {
            response.TryAdd("id", null);
            response["error"] = new JsonObject
            {
                ["code"] = 10002,
                ["message"] = "Invalid request"
            };
            return response;
        }
        try
        {
            response["result"] = await HandleRequestAsync(method, args);
        }
        catch (Exception ex)
        {
            response["error"] = CreateError(ex);
        }
        return response;
    }

    static JsonObject CreateError(Exception exception)
    {
        // Synchronous handlers are wrapped by reflection; awaited handlers are not.
        while (exception is TargetInvocationException { InnerException: not null } invocation)
            exception = invocation.InnerException!;

        return exception switch
        {
            DapiException ex => new JsonObject
            {
                ["code"] = ex.Code,
                ["message"] = ex.Message,
                ["data"] = JsonSerializer.SerializeToNode(ex.Data, SharedOptions.JsonSerializerOptions)
            },
            RpcException ex => new JsonObject
            {
                ["code"] = 10008,
                ["message"] = ex.Message,
                ["data"] = new JsonObject
                {
                    ["code"] = ex.Code,
                    ["message"] = ex.Message,
                    ["data"] = JsonSerializer.SerializeToNode(ex.Data, SharedOptions.JsonSerializerOptions)
                }
            },
            // HttpClient identifies its own timeout with this inner exception.
            TimeoutException or OperationCanceledException { InnerException: TimeoutException } => new JsonObject
            {
                ["code"] = 10005,
                ["message"] = "Operation timed out"
            },
            OperationCanceledException => new JsonObject
            {
                ["code"] = 10006,
                ["message"] = "Operation cancelled"
            },
            HttpRequestException ex => new JsonObject
            {
                ["code"] = 10008,
                ["message"] = ex.Message
            },
            _ => new JsonObject
            {
                ["code"] = 10000,
                ["message"] = exception.Message
            }
        };
    }

    private async Task<JsonNode?> HandleRequestAsync(string method, JsonArray? args)
    {
        if (!handlers.TryGetValue(method, out var handler))
            throw new DapiException(10001, "Method not found");
        List<object?> arguments = [];
        if (args != null)
            foreach (var parameter in handler.GetParameters())
            {
                if (parameter.Position >= args.Count)
                {
                    if (parameter.ParameterType.IsValueType && Nullable.GetUnderlyingType(parameter.ParameterType) is null)
                        throw new DapiException(10002, $"Invalid parameter: {parameter.Name}");
                    arguments.Add(null);
                }
                else
                {
                    JsonNode? node = args[parameter.Position];
                    if (node is null && parameter.ParameterType.IsValueType && Nullable.GetUnderlyingType(parameter.ParameterType) is null)
                        throw new DapiException(10002, $"Invalid parameter: {parameter.Name}");
                    object? argument;
                    try
                    {
                        argument = node?.Deserialize(parameter.ParameterType, SharedOptions.JsonSerializerOptions);
                    }
                    catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException or OverflowException or NotSupportedException)
                    {
                        throw new DapiException(10002, $"Invalid parameter: {parameter.Name}");
                    }
                    arguments.Add(argument);
                }
            }
        object result;
        try
        {
            result = handler.Invoke(host, arguments.ToArray())!;
        }
        catch (TargetParameterCountException)
        {
            throw new DapiException(10002, "Invalid parameter count");
        }
        catch (ArgumentException)
        {
            throw new DapiException(10002, "Invalid parameter");
        }
        if (result is Task task)
        {
            await task;
            result = ((dynamic)task).Result;
        }
        return JsonSerializer.SerializeToNode(result, result.GetType(), SharedOptions.JsonSerializerOptions)!;
    }
}
