using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace NeoOrder.OneGate.Controls.Views;

public partial class BridgeWebView : WebView
{
    public const string BridgeInvokeFunctionName = "__OneGateInvoke";
    public const string BridgeCallbackName = "__OneGateBridgeCallback";
    const string SystemCallMethodPrefix = "onegate.system.";
    const string SystemCallInvokeFunctionName = "__OneGateSystemInvoke";

    readonly ConcurrentDictionary<string, Func<JsonArray?, Task<JsonNode?>>> systemCallHandlers = new(StringComparer.Ordinal);

    public event EventHandler<BridgeWebView, JsonObject>? InvokedFromJavaScript;

    public static readonly BindableProperty DocumentStartScriptProperty = BindableProperty.Create(nameof(DocumentStartScript), typeof(string), typeof(BridgeWebView));

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
                window.{{SystemCallInvokeFunctionName}} = function(method, params) {
                    if (method.indexOf('{{SystemCallMethodPrefix}}') === 0)
                        return invoke(method, params);
                    return invoke('{{SystemCallMethodPrefix}}' + method, params);
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
        RegisterSystemCallHandler(method, args => Task.FromResult(handler(args)));
    }

    protected void RegisterSystemCallHandler(string method, Func<JsonArray?, Task<JsonNode?>> handler)
    {
        systemCallHandlers[method] = handler;
    }

    internal void OnMessage(string payload)
    {
        JsonObject? request;
        try
        {
            if (string.IsNullOrWhiteSpace(payload)) return;
            request = JsonNode.Parse(payload) as JsonObject;
            if (request is null) return;
            if (request["jsonrpc"] is not JsonValue jsonrpc || jsonrpc.GetValueKind() != JsonValueKind.String || jsonrpc.GetValue<string>() != "2.0") return;
            if (request["method"]?.GetValueKind() != JsonValueKind.String) return;
            if (request["params"] is not null && request["params"] is not JsonArray) return;
            if (request["id"] is not JsonValue id) return;
            if (id.GetValueKind() != JsonValueKind.String && id.GetValueKind() != JsonValueKind.Number) return;
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

    void DispatchSystemCall(JsonObject request, string method)
    {
        if (MainThread.IsMainThread)
            _ = HandleSystemCallAsync(request, method);
        else
            MainThread.BeginInvokeOnMainThread(() => _ = HandleSystemCallAsync(request, method));
    }

    async Task HandleSystemCallAsync(JsonObject request, string method)
    {
        JsonObject response = new()
        {
            ["jsonrpc"] = "2.0",
            ["id"] = request["id"]?.DeepClone()
        };
        try
        {
            systemCallHandlers.TryGetValue(method, out Func<JsonArray?, Task<JsonNode?>>? handler);

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
