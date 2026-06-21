#if ANDROID

using Android.Webkit;
using AndroidX.WebKit;
using Java.Interop;

namespace NeoOrder.OneGate.Controls.Handlers;

partial class BridgeWebViewHandler
{
    class ScriptHandler(Action<string> onMessage) : Java.Lang.Object
    {
        [JavascriptInterface]
        [Export("invoke")]
        public void Invoke(string payload)
        {
            onMessage(payload);
        }
    }

    protected override void ConnectHandler(Android.Webkit.WebView platformView)
    {
        base.ConnectHandler(platformView);
        platformView.Settings.DomStorageEnabled = true;
        platformView.Settings.JavaScriptEnabled = true;
        platformView.AddJavascriptInterface(new ScriptHandler(BridgeWebView.OnMessage), "__OneGateBridge");
        if (!string.IsNullOrWhiteSpace(BridgeWebView.DocumentStartScript) && WebViewFeature.IsFeatureSupported(WebViewFeature.DocumentStartScript))
            WebViewCompat.AddDocumentStartJavaScript(platformView, BridgeWebView.DocumentStartScript, ["*"]);
    }
}
#endif
