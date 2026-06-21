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

    public BridgeWebView()
    {
        RegisterSystemCallHandler("screen.orientation.lock", LockScreenOrientationSystemAsync);
        RegisterSystemCallHandler("screen.orientation.unlock", UnlockScreenOrientationSystemAsync);
    }

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
            (function () {
                if (window.__OneGateScreenOrientationInjected) return;
                window.__OneGateScreenOrientationInjected = true;
                if (!window.screen) return;

                const supportedLockTypes = new Set([
                    "any",
                    "natural",
                    "landscape",
                    "portrait",
                    "portrait-primary",
                    "portrait-secondary",
                    "landscape-primary",
                    "landscape-secondary"
                ]);

                let orientation = window.screen.orientation;
                const eventTarget = typeof EventTarget === "function" ? new EventTarget() : null;
                let onchange = null;
                if (!orientation) {
                    orientation = {};
                    try {
                        Object.defineProperty(window.screen, "orientation", {
                            configurable: true,
                            enumerable: true,
                            value: orientation
                        });
                    } catch (_) {
                        window.screen.orientation = orientation;
                    }
                }

                function define(name, descriptor) {
                    try {
                        Object.defineProperty(orientation, name, descriptor);
                    } catch (_) {
                        if ("value" in descriptor) {
                            try { orientation[name] = descriptor.value; } catch (_) {}
                        }
                    }
                }

                function getType() {
                    return window.innerWidth > window.innerHeight ? "landscape-primary" : "portrait-primary";
                }

                function getAngle() {
                    if (typeof window.orientation === "number")
                        return window.orientation;
                    return window.innerWidth > window.innerHeight ? 90 : 0;
                }

                define("type", {
                    configurable: true,
                    enumerable: true,
                    get: getType
                });

                define("angle", {
                    configurable: true,
                    enumerable: true,
                    get: getAngle
                });

                define("lock", {
                    configurable: true,
                    value: function(lockType) {
                        if (typeof lockType !== "string" || !supportedLockTypes.has(lockType))
                            return Promise.reject(new TypeError("Invalid screen orientation lock type."));
                        return window.{{SystemCallInvokeFunctionName}}("screen.orientation.lock", [lockType]);
                    }
                });

                define("unlock", {
                    configurable: true,
                    value: function() {
                        window.{{SystemCallInvokeFunctionName}}("screen.orientation.unlock", []).catch(function() {});
                    }
                });

                if (eventTarget && typeof orientation.addEventListener !== "function") {
                    define("addEventListener", {
                        configurable: true,
                        value: function() {
                            return eventTarget.addEventListener.apply(eventTarget, arguments);
                        }
                    });

                    define("removeEventListener", {
                        configurable: true,
                        value: function() {
                            return eventTarget.removeEventListener.apply(eventTarget, arguments);
                        }
                    });

                    define("dispatchEvent", {
                        configurable: true,
                        value: function() {
                            return eventTarget.dispatchEvent.apply(eventTarget, arguments);
                        }
                    });
                }

                define("onchange", {
                    configurable: true,
                    enumerable: true,
                    get: function() {
                        return onchange;
                    },
                    set: function(value) {
                        onchange = typeof value === "function" ? value : null;
                    }
                });

                function dispatchChange() {
                    const event = typeof Event === "function"
                        ? new Event("change")
                        : { type: "change", target: orientation };

                    if (eventTarget)
                        eventTarget.dispatchEvent(event);
                    if (typeof onchange === "function")
                        onchange.call(orientation, event);
                }

                window.addEventListener("orientationchange", dispatchChange);
                window.addEventListener("resize", dispatchChange);
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

    async Task<JsonNode?> LockScreenOrientationSystemAsync(JsonArray? args)
    {
        if (args is not { Count: 1 } || args[0]?.GetValueKind() != JsonValueKind.String)
            throw new InvalidOperationException("Invalid screen orientation lock request");

        string orientation = args[0]!.GetValue<string>();
        if (!IsSupportedScreenOrientationLockType(orientation))
            throw new InvalidOperationException("Invalid screen orientation lock type");

        await LockScreenOrientationAsync(orientation);
        return null;
    }

    async Task<JsonNode?> UnlockScreenOrientationSystemAsync(JsonArray? args)
    {
        if (args is not null && args.Count != 0)
            throw new InvalidOperationException("Invalid screen orientation unlock request");

        await UnlockScreenOrientationAsync();
        return null;
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

    static bool IsSupportedScreenOrientationLockType(string orientation)
    {
        return orientation is "any"
            or "natural"
            or "landscape"
            or "portrait"
            or "portrait-primary"
            or "portrait-secondary"
            or "landscape-primary"
            or "landscape-secondary";
    }

    private partial Task LockScreenOrientationAsync(string orientation);

    private partial Task UnlockScreenOrientationAsync();
}
