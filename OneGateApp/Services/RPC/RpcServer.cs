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
        catch (OperationCanceledException)
        {
            response["error"] = new JsonObject
            {
                ["code"] = 10006,
                ["message"] = "Operation cancelled"
            };
        }
        catch (DapiException ex)
        {
            response["error"] = new JsonObject
            {
                ["code"] = ex.Code,
                ["message"] = ex.Message,
                ["data"] = JsonSerializer.SerializeToNode(ex.Data, SharedOptions.JsonSerializerOptions)
            };
        }
        catch (Exception ex)
        {
            response["error"] = new JsonObject
            {
                ["code"] = 10000,
                ["message"] = ex.Message
            };
        }
        return response;
    }

    public static JsonObject CreateErrorResponse(JsonObject request, int code, string message, JsonNode? data = null)
    {
        var error = new JsonObject
        {
            ["code"] = code,
            ["message"] = message
        };
        if (data is not null)
            error["data"] = data;

        var response = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["error"] = error
        };
        response["id"] = request["id"]?.AsValue().DeepClone();
        return response;
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
