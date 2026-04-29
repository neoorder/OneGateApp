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
        try
        {
            response["id"] = GetRequestId(request["id"]);
            string method = GetRequiredString(request, "method");
            JsonArray? args = GetParams(request["params"]);
            response["result"] = await HandleRequestAsync(method, args);
        }
        catch (OperationCanceledException)
        {
            response.TryAdd("id", null);
            response["error"] = new JsonObject
            {
                ["code"] = 10006,
                ["message"] = "Operation cancelled"
            };
        }
        catch (DapiException ex)
        {
            response.TryAdd("id", null);
            response["error"] = new JsonObject
            {
                ["code"] = ex.Code,
                ["message"] = ex.Message,
                ["data"] = JsonSerializer.SerializeToNode(ex.Data, SharedOptions.JsonSerializerOptions)
            };
        }
        catch (Exception ex)
        {
            response.TryAdd("id", null);
            response["error"] = new JsonObject
            {
                ["code"] = 10000,
                ["message"] = ex.Message
            };
        }
        return response;
    }

    static JsonNode? GetRequestId(JsonNode? id)
    {
        if (id is null) return null;
        if (id is not JsonValue value)
            throw new DapiException(10002, "Invalid request id");
        if (value.TryGetValue<string>(out _) ||
            value.TryGetValue<int>(out _) ||
            value.TryGetValue<long>(out _) ||
            value.TryGetValue<ulong>(out _) ||
            value.TryGetValue<decimal>(out _))
        {
            return id.DeepClone();
        }
        throw new DapiException(10002, "Invalid request id");
    }

    static string GetRequiredString(JsonObject request, string name)
    {
        try
        {
            return request[name]?.GetValue<string>()
                ?? throw new DapiException(10002, $"Missing {name}");
        }
        catch (InvalidOperationException)
        {
            throw new DapiException(10002, $"Invalid {name}");
        }
    }

    static JsonArray? GetParams(JsonNode? node)
    {
        return node switch
        {
            null => null,
            JsonArray array => array,
            _ => throw new DapiException(10002, "Invalid params")
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
                    arguments.Add(null);
                }
                else
                {
                    JsonNode? node = args[parameter.Position];
                    object? argument = node?.Deserialize(parameter.ParameterType, SharedOptions.JsonSerializerOptions);
                    arguments.Add(argument);
                }
            }
        object result = handler.Invoke(host, arguments.ToArray())!;
        if (result is Task task)
        {
            await task;
            result = ((dynamic)task).Result;
        }
        return JsonSerializer.SerializeToNode(result, result.GetType(), SharedOptions.JsonSerializerOptions)!;
    }
}
