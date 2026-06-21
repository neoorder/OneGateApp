using CommunityToolkit.Maui.Alerts;
using Neo;
using Neo.Wallets;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Controls.Views;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Models.AppLinks;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using NeoOrder.OneGate.Services.RPC;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace NeoOrder.OneGate.Pages;

public partial class LaunchDAppPage : ContentPage, IQueryAttributable
{
    const string DeveloperModeKey = "preference/developer_mode_enabled";

    readonly IServiceProvider serviceProvider;
    readonly ProtocolSettings protocolSettings;
    readonly IWalletProvider walletProvider;
    readonly WalletAuthorizationService walletAuthorizationService;
    readonly ConnectedDAppService connectedDAppService;
    readonly ApplicationDbContext dbContext;
    readonly HttpClient httpClient;
    readonly RpcServer rpcServer;
    readonly RpcClient rpcClient;

    public required DApp DApp { get; set { field = value; OnPropertyChanged(); } }
    public required string Url { get; set { field = value; OnPropertyChanged(); } }
    public bool IsFavorite { get; set { field = value; OnPropertyChanged(); } }
    public bool IsDeveloperToolsEnabled { get; set { field = value; OnPropertyChanged(); } }

    public LaunchDAppPage(IServiceProvider serviceProvider, ProtocolSettings protocolSettings, IWalletProvider walletProvider, WalletAuthorizationService walletAuthorizationService, ConnectedDAppService connectedDAppService, ApplicationDbContext dbContext, HttpClient httpClient, RpcClient rpcClient, IHomeShortcutService homeShortcutService)
    {
        this.serviceProvider = serviceProvider;
        this.protocolSettings = protocolSettings;
        this.walletProvider = walletProvider;
        this.walletAuthorizationService = walletAuthorizationService;
        this.connectedDAppService = connectedDAppService;
        this.dbContext = dbContext;
        this.httpClient = httpClient;
        this.rpcServer = new(this);
        this.rpcClient = rpcClient;
        IsDeveloperToolsEnabled = dbContext.Settings.Get<bool>(DeveloperModeKey);
        InitializeComponent();
        webView.DocumentStartScript = CreateDocumentStartScript();
        if (!homeShortcutService.IsSupported)
            ToolbarItems.Remove(addToHomeScreenButton);
        if (!IsDeveloperToolsEnabled)
            ToolbarItems.Remove(developerToolsButton);
    }

    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("dapp", out var value))
        {
            DApp = (DApp)value;
            Url = DApp.Url;
        }
        else
        {
            Uri uri = query["uri"] as Uri ?? new(WebUtility.UrlDecode((string)query["uri"]));
            if (LaunchDAppAction.TryCreate(uri) is LaunchDAppAction action)
            {
                var response = await httpClient.GetAsync($"/api/dapp/{action.AppId}");
                if (!response.IsSuccessStatusCode)
                {
                    await this.GoBackOrCloseAsync();
                    return;
                }
                DApp = (await response.Content.ReadFromJsonAsync<DApp>())!;
                if (string.IsNullOrEmpty(uri.Query))
                    Url = DApp.Url;
                else
                    Url = DApp.Url + uri.Query;
            }
            else
            {
                DApp = new DApp
                {
                    Id = 0,
                    IsActive = false,
                    Name = $"{{\"en\":\"{uri.Host}\"}}",
                    Url = uri.AbsoluteUri,
                    Languages = ["en"]
                };
                Url = uri.AbsoluteUri;
            }
        }
        if (DApp.Id > 0)
        {
            List<int>? favorites = await dbContext.Settings.GetAsync<List<int>>("dapps/favorite");
            IsFavorite = favorites?.Contains(DApp.Id) ?? false;
            List<int> recents = await dbContext.Settings.GetAsync<List<int>>("dapps/recent") ?? [];
            recents.Remove(DApp.Id);
            recents.Insert(0, DApp.Id);
            while (recents.Count > 10)
                recents.RemoveAt(recents.Count - 1);
            await dbContext.Settings.PutAsync("dapps/recent", recents);
            if (DApp.IsRegularApp) GlobalStates.Invalidate<DAppsPage>();
        }
    }

    void OnFavoriteClicked(object sender, EventArgs e)
    {
        IsFavorite = !IsFavorite;
    }

    async void OnDeveloperToolsClicked(object sender, EventArgs e)
    {
        if (!IsDeveloperToolsEnabled) return;
        try
        {
            await webView.EvaluateJavaScriptAsync("window.__OneGateDevTools.toggle();");
        }
        catch
        {
        }
    }

    async void OnNavigating(object sender, WebNavigatingEventArgs e)
    {
        if (DApp is null) return;
        Uri uriOld = new(DApp.Url);
        Uri uriNew = new(uriOld, e.Url);
        if (uriNew.Scheme == "about" && uriNew.AbsoluteUri == "about:blank")
            return;
        if (IsCrossDomain(uriOld, uriNew))
        {
            e.Cancel = true;
            await Toast.Show(Strings.RedirectionBlockedText);
        }
    }

    string CreateDocumentStartScript()
    {
        string script = CreateDapiInjectionScript();
        if (IsDeveloperToolsEnabled)
            script += CreateDeveloperToolsInjectionScript();
        return script;
    }

    string CreateDapiInjectionScript()
    {
        return $$"""
            (function () {
                if (window.__OneGateDapiInjected) return;
                window.__OneGateDapiInjected = true;

                function deepFreeze(obj, seen = new WeakSet()) {
                    if (obj === null || typeof obj !== "object")
                        return obj;
                    if (seen.has(obj) || Object.isFrozen(obj))
                        return obj;
                    seen.add(obj);
                    for (const key of Reflect.ownKeys(obj)) {
                        const value = obj[key];
                        if (value && (typeof value === "object" || typeof value === "function"))
                            deepFreeze(value, seen);
                    }
                    return Object.freeze(obj);
                }
            
                const listeners = {
                    accountchanged: new Set(),
                    networkchanged: new Set()
                };

                const methods = ["authenticate", "getAccounts", "pickAddress", "getBalance", "send", "call", "invoke", "makeTransaction", "sign", "signMessage", "relay", "getBlock", "getBlockCount", "getTransaction", "getApplicationLog", "getStorage", "getTokenInfo"];

                const provider = {
                    name: '{{AppInfo.Name}}',
                    version: '{{AppInfo.VersionString}}',
                    dapiVersion: '1.0',
                    compatibility: ['NEP-2', 'NEP-6', 'NEP-9', 'NEP-11', 'NEP-17', 'NEP-20', 'NEP-21', 'NEP-33'],
                    connected: true,
                    network: {{protocolSettings.Network}},
                    supportedNetworks: [{{protocolSettings.Network}}],
                    icon: 'https://{{SharedOptions.OneGateDomain}}/images/logo.png',
                    website: 'https://{{SharedOptions.OneGateDomain}}',
                    extra: {},

                    on: function(event, listener) {
                        if (listeners[event]) listeners[event].add(listener);
                    },

                    removeListener: function(event, listener) {
                        if (listeners[event]) listeners[event].delete(listener);
                    },

                    __emit: function(event, detail) {
                        const e = new CustomEvent(event, { detail: deepFreeze(detail) });
                        listeners[event].forEach(function(fn) {
                            try { fn(e); } catch (_) {}
                        });
                    }
                };
            
                for (let method of methods)
                    provider[method] = function () { return window.{{BridgeWebView.BridgeInvokeFunctionName}}(method, [...arguments]); };
            
                window.OneGateDapiProvider = deepFreeze(provider);

                function dispatchReady() {
                    window.dispatchEvent(new CustomEvent('Neo.DapiProvider.ready', {
                        detail: {
                            provider: window.OneGateDapiProvider
                        }
                    }));
                }

                dispatchReady();

                window.addEventListener('Neo.DapiProvider.request', dispatchReady);
            })();
            """.ReplaceLineEndings("");
    }

    static string CreateDeveloperToolsInjectionScript()
    {
        return """
            (function () {
                if (window.top !== window) return;
                if (window.__OneGateDevTools) return;

                const maxEntries = 300;
                const entries = [];
                let panel;
                let logList;

                function format(value) {
                    if (value instanceof Error)
                        return value.stack || value.message;
                    if (typeof value === "string")
                        return value;
                    if (typeof value === "undefined")
                        return "undefined";
                    if (typeof value === "function")
                        return value.toString();
                    try {
                        const seen = new WeakSet();
                        return JSON.stringify(value, function(key, item) {
                            if (typeof item === "bigint")
                                return item.toString() + "n";
                            if (item && typeof item === "object") {
                                if (seen.has(item))
                                    return "[Circular]";
                                seen.add(item);
                            }
                            return item;
                        });
                    } catch (error) {
                        return String(value);
                    }
                }

                function push(level, args) {
                    entries.push({
                        level: level,
                        time: new Date().toLocaleTimeString(),
                        message: Array.prototype.slice.call(args).map(format).join(" ")
                    });
                    if (entries.length > maxEntries)
                        entries.splice(0, entries.length - maxEntries);
                    render();
                }

                function render() {
                    if (!logList)
                        return;

                    const atBottom = logList.scrollHeight - logList.scrollTop - logList.clientHeight < 24;
                    logList.replaceChildren();
                    for (const entry of entries) {
                        const row = document.createElement("div");
                        row.className = "onegate-devtools-entry onegate-devtools-" + entry.level;
                        row.textContent = "[" + entry.time + "] " + entry.level + "  " + entry.message;
                        logList.appendChild(row);
                    }
                    if (atBottom)
                        logList.scrollTop = logList.scrollHeight;
                }

                function ensureStyles() {
                    if (document.getElementById("onegate-devtools-style"))
                        return;

                    const style = document.createElement("style");
                    style.id = "onegate-devtools-style";
                    style.textContent =
                        "#onegate-devtools-panel{position:fixed;left:0;right:0;bottom:0;height:min(52vh,420px);z-index:2147483646;display:none;flex-direction:column;background:rgba(17,19,24,.82);color:#f3f5f7;border-top:1px solid rgba(48,52,60,.8);box-shadow:0 -8px 24px rgba(0,0,0,.28);font:12px ui-monospace,SFMono-Regular,Menlo,Consolas,monospace;text-align:left;backdrop-filter:blur(8px);-webkit-backdrop-filter:blur(8px)}" +
                        "#onegate-devtools-header{display:flex;align-items:center;justify-content:space-between;gap:8px;padding:8px 10px;background:rgba(25,29,36,.72);border-bottom:1px solid rgba(48,52,60,.8)}" +
                        "#onegate-devtools-title{font-weight:700;letter-spacing:0;color:#ffffff}" +
                        "#onegate-devtools-actions{display:flex;align-items:center;gap:6px}" +
                        "#onegate-devtools-actions button{border:1px solid rgba(61,68,80,.85);border-radius:6px;background:rgba(37,43,52,.82);color:#f3f5f7;padding:5px 8px;font:12px system-ui,-apple-system,BlinkMacSystemFont,Segoe UI,sans-serif}" +
                        "#onegate-devtools-log{flex:1;overflow:auto;padding:8px 10px;white-space:pre-wrap;word-break:break-word}" +
                        ".onegate-devtools-entry{padding:3px 0;border-bottom:1px solid rgba(255,255,255,.05)}" +
                        ".onegate-devtools-error{color:#ff817a}" +
                        ".onegate-devtools-warn{color:#f2cc60}" +
                        ".onegate-devtools-info{color:#78a8ff}" +
                        ".onegate-devtools-debug{color:#a9b1bd}";
                    (document.head || document.documentElement).appendChild(style);
                }

                function createButton(text, onClick) {
                    const button = document.createElement("button");
                    button.type = "button";
                    button.textContent = text;
                    button.addEventListener("click", function(event) {
                        event.preventDefault();
                        event.stopPropagation();
                        onClick();
                    });
                    return button;
                }

                function ensurePanel() {
                    if (panel)
                        return;

                    ensureStyles();
                    panel = document.createElement("section");
                    panel.id = "onegate-devtools-panel";
                    panel.setAttribute("aria-label", "OneGate DevTools");

                    const header = document.createElement("div");
                    header.id = "onegate-devtools-header";

                    const title = document.createElement("div");
                    title.id = "onegate-devtools-title";
                    title.textContent = "OneGate DevTools";

                    const actions = document.createElement("div");
                    actions.id = "onegate-devtools-actions";
                    actions.appendChild(createButton("Clear", function() {
                        entries.length = 0;
                        render();
                    }));
                    actions.appendChild(createButton("Close", function() {
                        api.hide();
                    }));

                    header.appendChild(title);
                    header.appendChild(actions);

                    logList = document.createElement("div");
                    logList.id = "onegate-devtools-log";

                    panel.appendChild(header);
                    panel.appendChild(logList);
                    (document.body || document.documentElement).appendChild(panel);
                    render();
                }

                const api = {
                    show: function() {
                        ensurePanel();
                        panel.style.display = "flex";
                        render();
                    },
                    hide: function() {
                        if (panel)
                            panel.style.display = "none";
                    },
                    toggle: function() {
                        ensurePanel();
                        if (panel.style.display === "none" || panel.style.display === "")
                            api.show();
                        else
                            api.hide();
                    },
                    clear: function() {
                        entries.length = 0;
                        render();
                    },
                    entries: entries
                };

                window.__OneGateDevTools = api;

                ["log", "info", "warn", "error", "debug"].forEach(function(level) {
                    const original = console[level];
                    console[level] = function() {
                        push(level, arguments);
                        if (original)
                            return original.apply(console, arguments);
                    };
                });

                window.addEventListener("error", function(event) {
                    push("error", [event.message + " @ " + event.filename + ":" + event.lineno + ":" + event.colno]);
                });
                window.addEventListener("unhandledrejection", function(event) {
                    push("error", ["Unhandled rejection", event.reason]);
                });

                push("info", ["OneGate developer tools ready"]);
            })();
            """.ReplaceLineEndings("");
    }

    static bool IsCrossDomain(Uri uriOld, Uri uriNew)
    {
        if (uriOld.Scheme != uriNew.Scheme) return true;
        if (uriOld.Authority == uriNew.Authority) return false;
        if (uriNew.Authority.EndsWith("." + uriOld.Authority)) return false;
        return true;
    }

    async void OnInvokedFromJavaScript(BridgeWebView webView, JsonObject request)
    {
        var response = await rpcServer.HandleRequestAsync(request);
        await webView.SendRpcRepsonseAsync(response);
    }

    async Task EmitEventAsync(string eventName, JsonObject? detial)
    {
        detial ??= new();
        await webView.EvaluateJavaScriptAsync($"window.OneGateDapiProvider.__emit('{eventName}', {detial.ToJsonString()})");
    }
}
