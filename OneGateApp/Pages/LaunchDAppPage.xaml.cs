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
    readonly IServiceProvider serviceProvider;
    readonly ProtocolSettings protocolSettings;
    readonly IWalletProvider walletProvider;
    readonly WalletAuthorizationService walletAuthorizationService;
    readonly ApplicationDbContext dbContext;
    readonly HttpClient httpClient;
    readonly RpcServer rpcServer;
    readonly RpcClient rpcClient;

    public required DApp DApp { get; set { field = value; OnPropertyChanged(); } }
    public required string Url { get; set { field = value; OnPropertyChanged(); } }
    public bool IsFavorite { get; set { field = value; OnPropertyChanged(); } }

    public LaunchDAppPage(IServiceProvider serviceProvider, ProtocolSettings protocolSettings, IWalletProvider walletProvider, WalletAuthorizationService walletAuthorizationService, ApplicationDbContext dbContext, HttpClient httpClient, RpcClient rpcClient, IHomeShortcutService homeShortcutService)
    {
        this.serviceProvider = serviceProvider;
        this.protocolSettings = protocolSettings;
        this.walletProvider = walletProvider;
        this.walletAuthorizationService = walletAuthorizationService;
        this.dbContext = dbContext;
        this.httpClient = httpClient;
        this.rpcServer = new(this);
        this.rpcClient = rpcClient;
        InitializeComponent();
        if (!homeShortcutService.IsSupported)
            ToolbarItems.Remove(addToHomeScreenButton);
    }

    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        string url;
        Uri? launchUri = GetLaunchUri(query);
        if (query.TryGetValue("dapp", out var value))
        {
            DApp = (DApp)value;
            url = launchUri is null
                ? DApp.Url
                : DAppLaunchUri.ApplyLaunchParameters(DApp.Url, launchUri);
        }
        else
        {
            Uri uri = launchUri ?? throw new ArgumentException("Missing launch URI.");
            if (LaunchDAppAction.TryCreate(uri) is LaunchDAppAction action)
            {
                var response = await httpClient.GetAsync($"/api/dapp/{action.AppId}");
                if (!response.IsSuccessStatusCode)
                {
                    await this.GoBackOrCloseAsync();
                    return;
                }
                DApp = (await response.Content.ReadFromJsonAsync<DApp>())!;
                url = DAppLaunchUri.ApplyLaunchParameters(DApp.Url, action.Uri);
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
                url = uri.AbsoluteUri;
            }
        }
        webView.DocumentStartScript = CreateDapiProviderScript();
        Url = url;
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

    static Uri? GetLaunchUri(IDictionary<string, object> query)
    {
        if (!query.TryGetValue("uri", out var value))
            return null;
        return value as Uri ?? new(WebUtility.UrlDecode((string)value));
    }

    void OnFavoriteClicked(object sender, EventArgs e)
    {
        IsFavorite = !IsFavorite;
    }

    async void OnNavigating(object sender, WebNavigatingEventArgs e)
    {
        if (DApp is null) return;
        Uri uriOld = new(DApp.Url);
        Uri uriNew = new(uriOld, e.Url);
        if (IsCrossDomain(uriOld, uriNew))
        {
            e.Cancel = true;
            await Toast.Show(Strings.RedirectionBlockedText);
        }
    }

    async void OnNavigated(object sender, WebNavigatedEventArgs e)
    {
        if (e.Result != WebNavigationResult.Success) return;
        await InjectDapiProviderAsync((BridgeWebView)sender);
    }

    async Task InjectDapiProviderAsync(BridgeWebView webView)
    {
        await webView.EvaluateJavaScriptAsync(CreateDapiProviderScript());
    }

    string CreateDapiProviderScript()
    {
        string script = $$"""
            (function () {
                function dispatchReady(provider) {
                    window.dispatchEvent(new CustomEvent('Neo.DapiProvider.ready', {
                        detail: {
                            provider: provider
                        }
                    }));
                }

                const pending = new Map();
                if (window.__OneGateDapiInjected && window.OneGateDapiProvider) {
                    dispatchReady(window.OneGateDapiProvider);
                    return;
                }
                window.__OneGateDapiInjected = true;

                function createId() {
                    return 'onegate_' + Date.now() + '_' + Math.random().toString(16).slice(2);
                }

                function rpc(method, params) {
                    return new Promise(function(resolve, reject) {
                        const id = createId();
                        pending.set(id, { resolve, reject });

                        const request = {
                            jsonrpc: "2.0",
                            id: id,
                            method: method,
                            params: params
                        };

                        window.__OneGateBridge.invoke(JSON.stringify(request));
                    });
                }

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
                        
                window.__OneGateDapiCallback = function(response) {
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
                    provider[method] = function () { return rpc(method, [...arguments]); };
            
                window.OneGateDapiProvider = deepFreeze(provider);

                dispatchReady(window.OneGateDapiProvider);

                window.addEventListener('Neo.DapiProvider.request', function() {
                    dispatchReady(window.OneGateDapiProvider);
                });
            })();
            """.ReplaceLineEndings("");
        return script;
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
        await webView.EvaluateJavaScriptAsync($"window.__OneGateDapiCallback({response.ToJsonString()})");
    }

    async Task EmitEventAsync(string eventName, JsonObject? detial)
    {
        detial ??= new();
        await webView.EvaluateJavaScriptAsync($"window.OneGateDapiProvider.__emit('{eventName}', {detial.ToJsonString()})");
    }
}
