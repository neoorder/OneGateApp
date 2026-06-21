#if ANDROID

using Android.Webkit;
using AndroidX.WebKit;
using Java.Interop;

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

    protected override void ConnectHandler(Android.Webkit.WebView platformView)
    {
        base.ConnectHandler(platformView);
        platformView.Settings.DomStorageEnabled = true;
        platformView.Settings.JavaScriptEnabled = true;
        platformView.AddJavascriptInterface(new ScriptHandler(BridgeWebView.OnMessage, BridgeWebView.OnSyncMessage), "__OneGateBridge");
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
