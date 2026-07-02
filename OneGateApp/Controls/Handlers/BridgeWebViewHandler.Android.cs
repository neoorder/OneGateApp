#if ANDROID

using Android.Webkit;
using AndroidX.WebKit;
using Java.Interop;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;

namespace NeoOrder.OneGate.Controls.Handlers;

partial class BridgeWebViewHandler
{
    class ScriptHandler(Action<string> onMessage, Func<string, string> onSyncMessage) : Java.Lang.Object
    {
        [JavascriptInterface]
        [Export("invoke")]
        public void Invoke(string payload)
        {
            onMessage(payload);
        }

        [JavascriptInterface]
        [Export("invokeSync")]
        public string InvokeSync(string payload)
        {
            return onSyncMessage(payload);
        }
    }

    class BridgeWebViewClient(BridgeWebViewHandler handler) : MauiWebViewClient(handler)
    {
        public override void OnPageFinished(Android.Webkit.WebView? view, string? url)
        {
            base.OnPageFinished(view, url);
            handler.InjectBridgeScript(view);
        }
    }

    protected override void ConnectHandler(Android.Webkit.WebView platformView)
    {
        base.ConnectHandler(platformView);
        platformView.Settings.DomStorageEnabled = true;
        platformView.Settings.JavaScriptEnabled = true;
        platformView.AddJavascriptInterface(new ScriptHandler(BridgeWebView.OnMessage, BridgeWebView.OnSyncMessage), "__OneGateBridge");
        platformView.SetWebViewClient(new BridgeWebViewClient(this));
        InstallDocumentStartScript(platformView);
    }

    static partial void ConfigureMapper(PropertyMapper<IWebView, IWebViewHandler> mapper)
    {
        mapper[nameof(Views.BridgeWebView.DocumentStartScript)] = MapDocumentStartScript;
    }

    static void MapDocumentStartScript(IWebViewHandler handler, IWebView webView)
    {
        if (handler is BridgeWebViewHandler bridgeHandler)
            bridgeHandler.InstallDocumentStartScript(bridgeHandler.PlatformView);
    }

    void InstallDocumentStartScript(Android.Webkit.WebView? platformView)
    {
        if (platformView is null || !WebViewFeature.IsFeatureSupported(WebViewFeature.DocumentStartScript))
            return;

        string script = Views.BridgeWebView.CreateRpcScript();
        if (!string.IsNullOrWhiteSpace(BridgeWebView.DocumentStartScript))
            script += BridgeWebView.DocumentStartScript;
        WebViewCompat.AddDocumentStartJavaScript(platformView, script, ["*"]);
    }

    void InjectBridgeScript(Android.Webkit.WebView? platformView)
    {
        if (platformView is null)
            return;

        string script = Views.BridgeWebView.CreateRpcScript();
        if (!string.IsNullOrWhiteSpace(BridgeWebView.DocumentStartScript))
            script += BridgeWebView.DocumentStartScript;
        platformView.EvaluateJavascript(script, null);
    }
}
#endif
