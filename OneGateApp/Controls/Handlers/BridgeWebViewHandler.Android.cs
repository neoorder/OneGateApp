#if ANDROID

using Android.Webkit;
using AndroidX.WebKit;
using Java.Interop;

namespace NeoOrder.OneGate.Controls.Handlers;

partial class BridgeWebViewHandler
{
    static readonly string[] AllowedOriginRules = ["*"];
    IScriptHandler? documentStartScriptHandler;

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
        platformView.Settings.JavaScriptEnabled = true;
        platformView.AddJavascriptInterface(new ScriptHandler(BridgeWebView.OnMessage), "__OneGateBridge");
        UpdateDocumentStartScriptCore();
    }

    protected override void DisconnectHandler(Android.Webkit.WebView platformView)
    {
        documentStartScriptHandler?.Remove();
        documentStartScriptHandler?.Dispose();
        documentStartScriptHandler = null;
        platformView.RemoveJavascriptInterface("__OneGateBridge");
        base.DisconnectHandler(platformView);
    }

    partial void UpdateDocumentStartScriptCore()
    {
        if (PlatformView is not Android.Webkit.WebView webView)
            return;

        documentStartScriptHandler?.Remove();
        documentStartScriptHandler?.Dispose();
        documentStartScriptHandler = null;

        string? script = BridgeWebView.DocumentStartScript;
        if (string.IsNullOrWhiteSpace(script))
            return;

        if (WebViewFeature.IsFeatureSupported(WebViewFeature.DocumentStartScript))
        {
            documentStartScriptHandler = WebViewCompat.AddDocumentStartJavaScript(webView, script, AllowedOriginRules);
        }

        webView.EvaluateJavascript(script, null);
    }
}
#endif
