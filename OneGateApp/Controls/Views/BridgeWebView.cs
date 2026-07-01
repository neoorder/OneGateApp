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

    public BridgeWebView()
    {
        RegisterSystemCallHandler("screen.orientation.lock", LockScreenOrientationSystemAsync);
        RegisterSystemCallHandler("screen.orientation.unlock", UnlockScreenOrientationSystem);
#if IOS
        RegisterSystemCallHandler("fullscreen.enter", EnterFullscreenSystemAsync);
        RegisterSystemCallHandler("fullscreen.exit", ExitFullscreenSystemAsync);
#endif
        Unloaded += (_, _) => RestoreHostState();
        HandlerChanging += (_, e) =>
        {
            if (e.OldHandler is not null)
                RestoreHostState();
        };
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
                function define(target, name, descriptor) {
                    if (!target)
                        return;

                    try {
                        Object.defineProperty(target, name, descriptor);
                    } catch (_) {
                        if ("value" in descriptor) {
                            try { target[name] = descriptor.value; } catch (_) {}
                        }
                    }
                }

                function install(target) {
                    define(target, "lock", {
                        configurable: true,
                        value: function(lockType) {
                            if (typeof lockType !== "string" || !supportedLockTypes.has(lockType))
                                return Promise.reject(new TypeError("Invalid screen orientation lock type."));
                            return window.{{SystemCallInvokeFunctionName}}("screen.orientation.lock", [lockType]);
                        }
                    });

                    define(target, "unlock", {
                        configurable: true,
                        value: function() {
                            window.{{SystemCallInvokeSyncFunctionName}}("screen.orientation.unlock", []);
                        }
                    });
                }

                const orientationPrototype = Object.getPrototypeOf(orientation);
                install(orientationPrototype);
            })();
            (function () {
                if (window.__OneGateFullscreenInjected) return;
                window.__OneGateFullscreenInjected = true;

                const isIOSWebKit = /iP(hone|ad|od)/.test(navigator.platform)
                    || (navigator.platform === "MacIntel" && navigator.maxTouchPoints > 1);
                if (!isIOSWebKit || typeof Document !== "function" || typeof Element !== "function")
                    return;

                let fullscreenElement = null;
                let fullscreenExitButton = null;
                let fullscreenExitRequest = null;
                const fullscreenElementClassName = "onegate-fullscreen-element";
                const fullscreenExitButtonClassName = "onegate-fullscreen-exit";
                const fullscreenRootClassName = "onegate-fullscreen-active";
                const fullscreenStyleId = "onegate-fullscreen-style";

                function define(target, name, descriptor) {
                    if (!target)
                        return;

                    try {
                        Object.defineProperty(target, name, descriptor);
                    } catch (_) {
                        if ("value" in descriptor) {
                            try { target[name] = descriptor.value; } catch (_) {}
                        }
                    }
                }

                function ensureFullscreenStyle() {
                    if (document.getElementById(fullscreenStyleId))
                        return;

                    const style = document.createElement("style");
                    style.id = fullscreenStyleId;
                    style.textContent =
                        "." + fullscreenRootClassName + "," +
                        "." + fullscreenRootClassName + " body{width:100%;height:100%;overflow:hidden!important;background:#000!important}" +
                        "." + fullscreenRootClassName + " body{margin:0!important}" +
                        "." + fullscreenElementClassName + ":not(:root){" +
                        "position:fixed!important;inset:0!important;margin:0!important;" +
                        "box-sizing:border-box!important;min-width:0!important;max-width:none!important;" +
                        "min-height:0!important;max-height:none!important;width:100vw!important;height:100vh!important;" +
                        "transform:none!important;z-index:2147483646!important;background:#000;object-fit:contain" +
                        "}" +
                        "." + fullscreenExitButtonClassName + "{" +
                        "position:fixed!important;left:max(12px,env(safe-area-inset-left))!important;" +
                        "top:max(12px,env(safe-area-inset-top))!important;width:36px!important;height:36px!important;" +
                        "z-index:2147483647!important;border:0!important;border-radius:18px!important;" +
                        "background:rgba(0,0,0,.58)!important;color:#fff!important;display:flex!important;" +
                        "align-items:center!important;justify-content:center!important;padding:0!important;margin:0!important;" +
                        "font:600 20px/1 system-ui,-apple-system,BlinkMacSystemFont,Segoe UI,sans-serif!important;" +
                        "box-shadow:0 2px 10px rgba(0,0,0,.28)!important;-webkit-tap-highlight-color:transparent!important;" +
                        "touch-action:manipulation!important;cursor:pointer!important" +
                        "}";
                    (document.head || document.documentElement).appendChild(style);
                }

                function createEvent(name) {
                    if (typeof Event === "function")
                        return new Event(name, { bubbles: true, composed: true });

                    const event = document.createEvent("Event");
                    event.initEvent(name, true, false);
                    return event;
                }

                function getEventTarget(element) {
                    if (element && element.isConnected && element.ownerDocument === document)
                        return element;

                    return document;
                }

                function dispatchFullscreenEvent(element, standardName, webkitName) {
                    const target = getEventTarget(element);
                    target.dispatchEvent(createEvent(standardName));
                    target.dispatchEvent(createEvent(webkitName));
                }

                function applyFullscreenStyle(element) {
                    ensureFullscreenStyle();
                    document.documentElement.classList.add(fullscreenRootClassName);
                    element.classList.add(fullscreenElementClassName);
                    ensureExitButton();
                }

                function clearFullscreenStyle(element) {
                    removeExitButton();
                    document.documentElement.classList.remove(fullscreenRootClassName);
                    if (element && element.classList)
                        element.classList.remove(fullscreenElementClassName);
                }

                function ensureExitButton() {
                    if (fullscreenExitButton && fullscreenExitButton.isConnected)
                        return;

                    fullscreenExitButton = document.createElement("button");
                    fullscreenExitButton.type = "button";
                    fullscreenExitButton.className = fullscreenExitButtonClassName;
                    fullscreenExitButton.setAttribute("aria-label", "Exit fullscreen");
                    fullscreenExitButton.textContent = "X";
                    fullscreenExitButton.addEventListener("click", requestExitFromButton);
                    fullscreenExitButton.addEventListener("pointerdown", requestExitFromButton);
                    fullscreenExitButton.addEventListener("touchstart", requestExitFromButton);
                    (document.body || document.documentElement).appendChild(fullscreenExitButton);
                }

                function removeExitButton() {
                    if (fullscreenExitButton)
                        fullscreenExitButton.remove();
                    fullscreenExitButton = null;
                    fullscreenExitRequest = null;
                }

                function eventTargetsExitButton(event) {
                    if (!fullscreenExitButton)
                        return false;

                    if (typeof event.composedPath === "function")
                        return event.composedPath().indexOf(fullscreenExitButton) >= 0;

                    return event.target === fullscreenExitButton
                        || (event.target && typeof fullscreenExitButton.contains === "function" && fullscreenExitButton.contains(event.target));
                }

                function requestExitFromButton(event) {
                    if (!eventTargetsExitButton(event))
                        return;

                    event.preventDefault();
                    event.stopPropagation();
                    if (typeof event.stopImmediatePropagation === "function")
                        event.stopImmediatePropagation();

                    if (!fullscreenElement || fullscreenExitRequest)
                        return;

                    fullscreenExitRequest = exitFullscreen();
                    fullscreenExitRequest.then(
                        function() { fullscreenExitRequest = null; },
                        function() { fullscreenExitRequest = null; });
                }

                ["pointerdown", "touchstart", "mousedown", "click"].forEach(function(type) {
                    window.addEventListener(type, requestExitFromButton, true);
                });

                function dispatchFullscreenChange(element) {
                    dispatchFullscreenEvent(element, "fullscreenchange", "webkitfullscreenchange");
                }

                function dispatchFullscreenError(element) {
                    dispatchFullscreenEvent(element, "fullscreenerror", "webkitfullscreenerror");
                }

                function enterFullscreen(element) {
                    if (!element || element.ownerDocument !== document) {
                        dispatchFullscreenError(element);
                        return Promise.reject(new TypeError("Fullscreen element must belong to this document."));
                    }

                    return window.{{SystemCallInvokeFunctionName}}("fullscreen.enter", []).then(function() {
                        if (fullscreenElement && fullscreenElement !== element)
                            clearFullscreenStyle(fullscreenElement);

                        fullscreenElement = element;
                        applyFullscreenStyle(element);
                        dispatchFullscreenChange(element);
                    }).catch(function(error) {
                        dispatchFullscreenError(element);
                        throw error;
                    });
                }

                function exitFullscreen() {
                    if (!fullscreenElement)
                        return Promise.resolve();

                    const element = fullscreenElement;
                    return window.{{SystemCallInvokeFunctionName}}("fullscreen.exit", []).then(function() {
                        clearFullscreenStyle(element);
                        fullscreenElement = null;
                        dispatchFullscreenChange(element);
                    }).catch(function(error) {
                        dispatchFullscreenError(element);
                        throw error;
                    });
                }

                const fullscreenElementDescriptor = {
                    configurable: true,
                    enumerable: true,
                    get: function() {
                        return fullscreenElement;
                    }
                };

                const fullscreenEnabledDescriptor = {
                    configurable: true,
                    enumerable: true,
                    get: function() {
                        return true;
                    }
                };

                define(Element.prototype, "requestFullscreen", {
                    configurable: true,
                    value: function() {
                        return enterFullscreen(this);
                    }
                });

                define(Element.prototype, "webkitRequestFullscreen", {
                    configurable: true,
                    value: function() {
                        return enterFullscreen(this);
                    }
                });

                define(Document.prototype, "exitFullscreen", {
                    configurable: true,
                    value: exitFullscreen
                });
                define(document, "exitFullscreen", {
                    configurable: true,
                    value: exitFullscreen
                });

                define(Document.prototype, "webkitExitFullscreen", {
                    configurable: true,
                    value: exitFullscreen
                });
                define(document, "webkitExitFullscreen", {
                    configurable: true,
                    value: exitFullscreen
                });

                define(Document.prototype, "fullscreenElement", fullscreenElementDescriptor);
                define(document, "fullscreenElement", fullscreenElementDescriptor);
                define(Document.prototype, "webkitFullscreenElement", fullscreenElementDescriptor);
                define(document, "webkitFullscreenElement", fullscreenElementDescriptor);

                define(Document.prototype, "fullscreenEnabled", fullscreenEnabledDescriptor);
                define(document, "fullscreenEnabled", fullscreenEnabledDescriptor);
                define(Document.prototype, "webkitFullscreenEnabled", fullscreenEnabledDescriptor);
                define(document, "webkitFullscreenEnabled", fullscreenEnabledDescriptor);
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

    JsonNode? UnlockScreenOrientationSystem(JsonArray? args)
    {
        if (args is not null && args.Count != 0)
            throw new InvalidOperationException("Invalid screen orientation unlock request");

        UnlockScreenOrientation();
        return null;
    }

#if IOS
    async Task<JsonNode?> EnterFullscreenSystemAsync(JsonArray? args)
    {
        if (args is not null && args.Count != 0)
            throw new InvalidOperationException("Invalid fullscreen enter request");

        await EnterFullscreenAsync();
        return null;
    }

    async Task<JsonNode?> ExitFullscreenSystemAsync(JsonArray? args)
    {
        if (args is not null && args.Count != 0)
            throw new InvalidOperationException("Invalid fullscreen exit request");

        await ExitFullscreenAsync();
        return null;
    }
#endif

    void RestoreHostState()
    {
#if IOS
        RestoreFullscreenState();
#endif
        try
        {
            UnlockScreenOrientation();
        }
        catch
        {
            // The platform context may already be gone while the WebView is unloading.
        }
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

    private partial void UnlockScreenOrientation();

#if IOS
    private partial Task EnterFullscreenAsync();

    private partial Task ExitFullscreenAsync();

    private partial void RestoreFullscreenState();
#endif
}
