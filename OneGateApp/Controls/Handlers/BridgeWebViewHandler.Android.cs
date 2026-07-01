#if ANDROID

using Android.Webkit;
using AndroidX.WebKit;
using Java.Interop;
using Microsoft.Maui.Handlers;

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

    static partial void ConfigureMapper(PropertyMapper<IWebView, IWebViewHandler> mapper)
    {
        mapper.AppendToMapping(nameof(Views.BridgeWebView.DocumentStartScript), MapDocumentStartScript);
    }

    protected override void ConnectHandler(Android.Webkit.WebView platformView)
    {
        base.ConnectHandler(platformView);
        platformView.Settings.DomStorageEnabled = true;
        platformView.Settings.JavaScriptEnabled = true;
        platformView.AddJavascriptInterface(new ScriptHandler(BridgeWebView.OnMessage, BridgeWebView.OnSyncMessage), "__OneGateBridge");
        AddDocumentStartScript(platformView);
    }

    static void MapDocumentStartScript(IWebViewHandler handler, IWebView webView)
    {
        if (handler is BridgeWebViewHandler bridgeHandler && bridgeHandler.PlatformView is { } platformView)
            bridgeHandler.AddDocumentStartScript(platformView);
    }

    void AddDocumentStartScript(Android.Webkit.WebView platformView)
    {
        if (WebViewFeature.IsFeatureSupported(WebViewFeature.DocumentStartScript))
        {
            string script = Views.BridgeWebView.CreateRpcScript();
            if (!string.IsNullOrWhiteSpace(BridgeWebView.DocumentStartScript))
                script += BridgeWebView.DocumentStartScript;
            WebViewCompat.AddDocumentStartJavaScript(platformView, script, ["*"]);
        }
    }
}
#endif
