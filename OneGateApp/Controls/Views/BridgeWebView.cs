using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NeoOrder.OneGate.Controls.Views;

public partial class BridgeWebView : WebView
{
    public const string BridgeInvokeFunctionName = "__OneGateInvoke";
    public const string BridgeInvokeSyncFunctionName = "__OneGateInvokeSync";
    public const string BridgeCallbackName = "__OneGateBridgeCallback";
    const string SystemCallMethodPrefix = "onegate.system.";
    const string SystemCallInvokeFunctionName = "__OneGateSystemInvoke";
    const string SystemCallInvokeSyncFunctionName = "__OneGateSystemInvokeSync";

    public static readonly BindableProperty DocumentStartScriptProperty = BindableProperty.Create(nameof(DocumentStartScript), typeof(string), typeof(BridgeWebView));
    readonly ConcurrentDictionary<string, Func<JsonArray?, Task<JsonNode?>>> asyncSystemCallHandlers = new(StringComparer.Ordinal);
    readonly ConcurrentDictionary<string, Func<JsonArray?, JsonNode?>> syncSystemCallHandlers = new(StringComparer.Ordinal);

    public event EventHandler<BridgeWebView, JsonObject>? InvokedFromJavaScript;

    public string? DocumentStartScript { get => (string?)GetValue(DocumentStartScriptProperty); set => SetValue(DocumentStartScriptProperty, value); }

    internal static string CreateRpcScript()
    {
        return $$"""
            (function () {
                if (window.__OneGateRpcInjected) return;
                window.__OneGateRpcInjected = true;

                const pending = new Map();

                function createId() {
                    return 'onegate_' + Date.now() + '_' + Math.random().toString(16).slice(2);
                }

                function invoke(method, params) {
                    return new Promise(function(resolve, reject) {
                        const id = createId();
                        pending.set(id, { resolve, reject });

                        const request = {
                            jsonrpc: "2.0",
                            id: id,
                            method: method,
                            params: params
                        };

                        try {
                            window.__OneGateBridge.invoke(JSON.stringify(request));
                        } catch (error) {
                            pending.delete(id);
                            reject(error);
                        }
                    });
                }

                window.{{BridgeInvokeFunctionName}} = invoke;
                window.{{BridgeInvokeSyncFunctionName}} = function(method, params) {
                    if (!window.__OneGateBridge || typeof window.__OneGateBridge.invokeSync !== 'function')
                        throw new Error('Synchronous bridge is not available.');

                    const request = {
                        jsonrpc: "2.0",
                        id: createId(),
                        method: method,
                        params: params
                    };
                    let response = window.__OneGateBridge.invokeSync(JSON.stringify(request));
                    if (typeof response === 'string')
                        response = JSON.parse(response);

                    if (!response || response.jsonrpc !== "2.0" || response.id !== request.id)
                        throw new Error('Invalid synchronous bridge response.');

                    if (response.error) {
                        const error = new Error(response.error.message || 'Synchronous bridge invocation failed.');
                        error.code = response.error.code;
                        error.data = response.error.data;
                        throw error;
                    }

                    return response.result;
                };
                window.{{SystemCallInvokeFunctionName}} = function(method, params) {
                    if (method.indexOf('{{SystemCallMethodPrefix}}') === 0)
                        return invoke(method, params);
                    return invoke('{{SystemCallMethodPrefix}}' + method, params);
                };
                window.{{SystemCallInvokeSyncFunctionName}} = function(method, params) {
                    if (method.indexOf('{{SystemCallMethodPrefix}}') === 0)
                        return window.{{BridgeInvokeSyncFunctionName}}(method, params);
                    return window.{{BridgeInvokeSyncFunctionName}}('{{SystemCallMethodPrefix}}' + method, params);
                };

                window.{{BridgeCallbackName}} = function(response) {
                    if (typeof response === 'string')
                        response = JSON.parse(response);

                    const item = pending.get(response.id);
                    if (!item) return;
                    pending.delete(response.id);

                    if (response.error)
                        item.reject(response.error);
                    else
                        item.resolve(response.result);
                };
            })();
            """.ReplaceLineEndings("");
    }

    public Task SendRpcRepsonseAsync(JsonObject response)
    {
        ArgumentNullException.ThrowIfNull(response);
        return EvaluateJavaScriptAsync($"window.{BridgeCallbackName}({response.ToJsonString()})");
    }

    protected void RegisterSystemCallHandler(string method, Func<JsonArray?, JsonNode?> handler)
    {
        syncSystemCallHandlers[method] = handler;
    }

    protected void RegisterSystemCallHandler(string method, Func<JsonArray?, Task<JsonNode?>> handler)
    {
        asyncSystemCallHandlers[method] = handler;
    }

    internal void OnMessage(string payload)
    {
        JsonObject? request;
        try
        {
            request = ParseRpcRequest(payload);
        }
        catch
        {
            return;
        }
        string method = request["method"]!.GetValue<string>();
        if (TryGetSystemCallMethod(method, out string? systemMethod))
            DispatchSystemCall(request, systemMethod);
        else
            DispatchJavaScriptInvocation(request);
    }

    void DispatchJavaScriptInvocation(JsonObject request)
    {
        if (MainThread.IsMainThread)
            InvokedFromJavaScript?.Invoke(this, request);
        else
            MainThread.BeginInvokeOnMainThread(() => InvokedFromJavaScript?.Invoke(this, request));
    }

    internal string OnSyncMessage(string payload)
    {
        if (MainThread.IsMainThread)
            return HandleSyncMessage(payload);

        return MainThread.InvokeOnMainThreadAsync(() => HandleSyncMessage(payload)).GetAwaiter().GetResult();
    }

    string HandleSyncMessage(string payload)
    {
        JsonObject? request = null;
        try
        {
            request = ParseRpcRequest(payload);
            string method = request["method"]!.GetValue<string>();
            if (TryGetSystemCallMethod(method, out string? systemMethod))
                return HandleSyncSystemCall(request, systemMethod).ToJsonString();
            return InvokeSynchronouslyFromJavaScript(request).ToJsonString();
        }
        catch (OperationCanceledException)
        {
            return CreateRpcErrorResponse(request, 10006, "Operation cancelled").ToJsonString();
        }
        catch (JsonException)
        {
            return CreateRpcErrorResponse(request, 10002, "Invalid request").ToJsonString();
        }
        catch (ArgumentException)
        {
            return CreateRpcErrorResponse(request, 10002, "Invalid request").ToJsonString();
        }
        catch (InvalidOperationException ex)
        {
            return CreateRpcErrorResponse(request, 10001, ex.Message).ToJsonString();
        }
        catch (Exception ex)
        {
            return CreateRpcErrorResponse(request, 10000, ex.Message).ToJsonString();
        }
    }

    protected virtual JsonObject InvokeSynchronouslyFromJavaScript(JsonObject request)
    {
        return CreateRpcErrorResponse(request, -32601, "Method not found");
    }

    JsonObject HandleSyncSystemCall(JsonObject request, string method)
    {
        JsonObject response = CreateRpcResponse(request);
        try
        {
            syncSystemCallHandlers.TryGetValue(method, out Func<JsonArray?, JsonNode?>? handler);

            if (handler is null)
                throw new InvalidOperationException("System method not found");

            response["result"] = handler(request["params"] as JsonArray)?.DeepClone();
        }
        catch (OperationCanceledException)
        {
            response["error"] = CreateSystemCallError(10006, "Operation cancelled");
        }
        catch (InvalidOperationException ex)
        {
            response["error"] = CreateSystemCallError(10001, ex.Message);
        }
        catch (Exception ex)
        {
            response["error"] = CreateSystemCallError(10000, ex.Message);
        }

        return response;
    }

    static JsonObject ParseRpcRequest(string payload)
    {
        if (string.IsNullOrEmpty(payload))
            throw new ArgumentException("Invalid request", nameof(payload));

        JsonObject request = JsonNode.Parse(payload) as JsonObject
            ?? throw new ArgumentException("Invalid request", nameof(payload));

        if (request["jsonrpc"] is not JsonValue jsonrpc || jsonrpc.GetValueKind() != JsonValueKind.String || jsonrpc.GetValue<string>() != "2.0")
            throw new ArgumentException("Invalid request", nameof(payload));
        if (request["method"]?.GetValueKind() != JsonValueKind.String)
            throw new ArgumentException("Invalid request", nameof(payload));
        if (request["params"] is not null && request["params"] is not JsonArray)
            throw new ArgumentException("Invalid request", nameof(payload));
        if (request["id"] is not JsonValue id)
            throw new ArgumentException("Invalid request", nameof(payload));
        if (id.GetValueKind() != JsonValueKind.String && id.GetValueKind() != JsonValueKind.Number)
            throw new ArgumentException("Invalid request", nameof(payload));

        return request;
    }

    static JsonObject CreateRpcErrorResponse(JsonObject? request, int code, string message)
    {
        return new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = request?["id"]?.DeepClone(),
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };
    }

    static JsonObject CreateRpcResponse(JsonObject request)
    {
        return new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = request["id"]?.DeepClone()
        };
    }

    void DispatchSystemCall(JsonObject request, string method)
    {
        if (MainThread.IsMainThread)
            _ = HandleSystemCallAsync(request, method);
        else
            MainThread.BeginInvokeOnMainThread(() => _ = HandleSystemCallAsync(request, method));
    }

    async Task HandleSystemCallAsync(JsonObject request, string method)
    {
        JsonObject response = CreateRpcResponse(request);
        try
        {
            asyncSystemCallHandlers.TryGetValue(method, out Func<JsonArray?, Task<JsonNode?>>? handler);

            if (handler is null)
                throw new InvalidOperationException("System method not found");

            response["result"] = (await handler(request["params"] as JsonArray))?.DeepClone();
        }
        catch (OperationCanceledException)
        {
            response["error"] = CreateSystemCallError(10006, "Operation cancelled");
        }
        catch (InvalidOperationException ex)
        {
            response["error"] = CreateSystemCallError(10001, ex.Message);
        }
        catch (Exception ex)
        {
            response["error"] = CreateSystemCallError(10000, ex.Message);
        }

        await SendRpcRepsonseAsync(response);
    }

    static JsonObject CreateSystemCallError(int code, string message)
    {
        return new()
        {
            ["code"] = code,
            ["message"] = message
        };
    }

    static bool TryGetSystemCallMethod(string method, [NotNullWhen(true)] out string? systemMethod)
    {
        if (method.StartsWith(SystemCallMethodPrefix, StringComparison.Ordinal))
        {
            systemMethod = method[SystemCallMethodPrefix.Length..];
            return true;
        }
        systemMethod = null;
        return false;
    }
}
