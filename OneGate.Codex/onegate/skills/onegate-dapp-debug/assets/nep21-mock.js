// Injected into the DApp main world at document start by the OneGate local runtime.
(function bootstrapOneGateMock() {
    "use strict";

    if (window.top !== window)
        return;

    const CONFIG_GLOBAL = "__ONEGATE_MOCK_CONFIG__";
    const BRIDGE_GLOBAL = "__OneGateMockBridge";
    const BRIDGE_RECEIVER_GLOBAL = "__OneGateMockBridgeReceive";
    const TRACE_EVENT = "OneGate.Mock.trace";
    const READY_EVENT = "Neo.DapiProvider.ready";
    const REQUEST_EVENT = "Neo.DapiProvider.request";
    const MAX_TRACE_ENTRIES = 500;

    const ErrorCode = Object.freeze({
        UNKNOWN: 10000,
        UNSUPPORTED: 10001,
        INVALID: 10002,
        NOTFOUND: 10003,
        FAILED: 10004,
        TIMEOUT: 10005,
        CANCELED: 10006,
        INSUFFICIENT_FUNDS: 10007,
        RPC_ERROR: 10008
    });

    const methodNames = Object.freeze([
        "authenticate",
        "getAccounts",
        "pickAddress",
        "getBalance",
        "send",
        "call",
        "invoke",
        "makeTransaction",
        "sign",
        "signMessage",
        "relay",
        "getBlock",
        "getBlockCount",
        "getTransaction",
        "getApplicationLog",
        "getStorage",
        "getTokenInfo"
    ]);

    function clone(value) {
        if (value === undefined)
            return undefined;
        if (typeof structuredClone === "function") {
            try {
                return structuredClone(value);
            } catch (_) {
            }
        }
        return JSON.parse(JSON.stringify(value));
    }

    function deepFreeze(value, seen) {
        if (value === null || (typeof value !== "object" && typeof value !== "function"))
            return value;
        const visited = seen || new WeakSet();
        if (visited.has(value) || Object.isFrozen(value))
            return value;
        visited.add(value);
        for (const key of Reflect.ownKeys(value))
            deepFreeze(value[key], visited);
        return Object.freeze(value);
    }

    function readConfiguration() {
        const envelope = globalThis[CONFIG_GLOBAL];
        if (!envelope || typeof envelope !== "object")
            throw new Error("OneGate Browser Mock configuration is missing.");
        if (!envelope.profile || typeof envelope.profile !== "object")
            throw new Error("OneGate Browser Mock profile is invalid.");
        return {
            sessionId: String(envelope.sessionId || "unknown"),
            profile: envelope.profile
        };
    }

    function dapiError(code, message, data) {
        const error = new Error(message);
        error.code = Number.isInteger(code) ? code : ErrorCode.UNKNOWN;
        if (data !== undefined)
            error.data = clone(data);
        return error;
    }

    if (window.OneGateDapiProvider) {
        console.warn("[OneGate Mock] A provider already exists; mock injection was skipped.");
        return;
    }

    if (typeof globalThis[BRIDGE_GLOBAL] !== "function") {
        console.error("[OneGate Mock] The private dAPI bridge binding is unavailable.");
        return;
    }

    let configuration;
    try {
        configuration = readConfiguration();
    } catch (error) {
        console.error("[OneGate Mock] Injection failed:", error);
        return;
    }

    const profile = deepFreeze(configuration.profile);
    const providerSettings = profile.provider || {};
    const trace = [];
    const pending = new Map();
    const listeners = {
        accountchanged: new Set(),
        networkchanged: new Set()
    };
    let nextRequestId = 1;

    function appendTrace(phase, method, args, value) {
        const entry = deepFreeze({
            sequence: trace.length ? trace[trace.length - 1].sequence + 1 : 1,
            timestamp: new Date().toISOString(),
            phase: phase,
            method: method,
            args: clone(args),
            value: clone(value)
        });
        trace.push(entry);
        if (trace.length > MAX_TRACE_ENTRIES)
            trace.shift();
        window.dispatchEvent(new CustomEvent(TRACE_EVENT, { detail: entry }));
        console.debug("[OneGate Mock trace] " + JSON.stringify(entry));
    }

    function emitProviderEvent(eventName, detail) {
        if (!listeners[eventName])
            return;
        const event = new CustomEvent(eventName, { detail: deepFreeze(clone(detail)) });
        listeners[eventName].forEach(function (listener) {
            try {
                listener(event);
            } catch (error) {
                console.error("[OneGate Mock] Provider event listener failed:", error);
            }
        });
    }

    function rejectPending(id, error) {
        const request = pending.get(id);
        if (!request)
            return;
        pending.delete(id);
        const normalized = error && Number.isInteger(error.code)
            ? error
            : dapiError(ErrorCode.UNKNOWN, error && error.message ? error.message : "Unknown Browser Mock failure.");
        appendTrace("reject", request.method, request.args, {
            code: normalized.code,
            message: normalized.message,
            data: normalized.data
        });
        request.reject(normalized);
    }

    function receiveBridgeResponse(payload) {
        let response;
        try {
            response = typeof payload === "string" ? JSON.parse(payload) : payload;
        } catch (error) {
            console.error("[OneGate Mock] Invalid bridge response:", error);
            return;
        }
        const request = response && pending.get(response.id);
        if (!request)
            return;
        if (response.ok === true) {
            pending.delete(response.id);
            appendTrace("resolve", request.method, request.args, response.result);
            request.resolve(clone(response.result));
            return;
        }
        rejectPending(response.id, dapiError(
            response && response.error ? response.error.code : ErrorCode.UNKNOWN,
            response && response.error && response.error.message
                ? response.error.message
                : "Unknown Browser Mock failure.",
            response && response.error ? response.error.data : undefined
        ));
    }

    Object.defineProperty(globalThis, BRIDGE_RECEIVER_GLOBAL, {
        configurable: false,
        enumerable: false,
        writable: false,
        value: receiveBridgeResponse
    });

    function invokeMock(method, args) {
        appendTrace("request", method, args);
        return new Promise(function (resolve, reject) {
            const id = String(nextRequestId++);
            pending.set(id, { method: method, args: clone(args), resolve: resolve, reject: reject });
            try {
                globalThis[BRIDGE_GLOBAL](JSON.stringify({
                    sessionId: configuration.sessionId,
                    id: id,
                    method: method,
                    args: args
                }));
            } catch (error) {
                rejectPending(id, error);
            }
        });
    }

    const provider = {
        name: providerSettings.name || "OneGate Codex Plugin",
        version: providerSettings.version || "1.0.0",
        dapiVersion: providerSettings.dapiVersion || "1.0",
        compatibility: Array.isArray(providerSettings.compatibility)
            ? clone(providerSettings.compatibility)
            : ["NEP-2", "NEP-6", "NEP-9", "NEP-11", "NEP-17", "NEP-20", "NEP-21", "NEP-33"],
        connected: providerSettings.connected !== false,
        network: Number.isInteger(providerSettings.network) ? providerSettings.network : 860833102,
        supportedNetworks: Array.isArray(providerSettings.supportedNetworks)
            ? clone(providerSettings.supportedNetworks)
            : [860833102],
        icon: providerSettings.icon || "https://onegate.space/images/logo.png",
        website: providerSettings.website || "https://onegate.space",
        extra: {
            mock: true,
            profile: String(profile.id || "default"),
            transactionMode: String(profile.transactionMode || "offline"),
            sessionId: configuration.sessionId
        },
        on: function (eventName, listener) {
            if (listeners[eventName] && typeof listener === "function")
                listeners[eventName].add(listener);
        },
        removeListener: function (eventName, listener) {
            if (listeners[eventName])
                listeners[eventName].delete(listener);
        },
        __emit: emitProviderEvent
    };

    methodNames.forEach(function (method) {
        provider[method] = function () {
            return invokeMock(method, Array.prototype.slice.call(arguments));
        };
    });

    deepFreeze(provider);
    Object.defineProperty(window, "OneGateDapiProvider", {
        configurable: false,
        enumerable: true,
        writable: false,
        value: provider
    });
    Object.defineProperty(window, "__OneGateDapiInjected", {
        configurable: false,
        enumerable: false,
        writable: false,
        value: true
    });

    const control = deepFreeze({
        errorCodes: ErrorCode,
        getTrace: function () {
            return clone(trace);
        },
        clearTrace: function () {
            trace.splice(0, trace.length);
        },
        emitAccountChanged: function (accounts) {
            emitProviderEvent("accountchanged", { accounts: clone(accounts) });
        },
        emitNetworkChanged: function (network) {
            emitProviderEvent("networkchanged", { network: network });
        }
    });
    Object.defineProperty(window, "__OneGateMock", {
        configurable: false,
        enumerable: false,
        writable: false,
        value: control
    });

    function dispatchReady() {
        window.dispatchEvent(new CustomEvent(READY_EVENT, {
            detail: { provider: provider }
        }));
    }

    window.addEventListener(REQUEST_EVENT, function (event) {
        const requestedVersion = event && event.detail ? event.detail.version : undefined;
        if (!requestedVersion || requestedVersion === provider.dapiVersion)
            dispatchReady();
    });

    dispatchReady();
    console.info("[OneGate Mock] Provider ready " + JSON.stringify(provider.extra));
})();
