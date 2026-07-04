#if ANDROID

using Android.App;
using Android.Content;
using Android.Webkit;
using AndroidX.WebKit;
using Java.Interop;
using NeoOrder.OneGate.Platforms.Android;

namespace NeoOrder.OneGate.Controls.Handlers;

partial class BridgeWebViewHandler
{
    static int nextFileChooserRequestCode = 8300;

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

    class BridgeWebChromeClient : WebChromeClient
    {
        readonly int fileChooserRequestCode = Interlocked.Increment(ref nextFileChooserRequestCode);
        IValueCallback? pendingFilePathCallback;

        public BridgeWebChromeClient()
        {
            OneGateActivity.ActivityResultReceived += OnActivityResultReceived;
        }

        public override bool OnShowFileChooser(Android.Webkit.WebView? webView, IValueCallback? filePathCallback, FileChooserParams? fileChooserParams)
        {
            CancelPendingFileChooser();
            if (filePathCallback is null)
                return false;
            if (fileChooserParams is null)
            {
                filePathCallback.OnReceiveValue(null);
                return true;
            }

            Activity? activity = Platform.CurrentActivity;
            if (activity is null)
            {
                filePathCallback.OnReceiveValue(null);
                return true;
            }

            pendingFilePathCallback = filePathCallback;
            try
            {
                Intent? intent = fileChooserParams.CreateIntent();
                if (intent is null)
                {
                    CompleteFileChooser(null);
                    return true;
                }
                intent.AddFlags(ActivityFlags.GrantReadUriPermission);
                activity.StartActivityForResult(intent, fileChooserRequestCode);
            }
            catch
            {
                CompleteFileChooser(null);
            }
            return true;
        }

        public void Disconnect()
        {
            OneGateActivity.ActivityResultReceived -= OnActivityResultReceived;
            CancelPendingFileChooser();
        }

        void OnActivityResultReceived(object? sender, OneGateActivity.ActivityResultReceivedEventArgs e)
        {
            if (e.RequestCode != fileChooserRequestCode || pendingFilePathCallback is null)
                return;

            Android.Net.Uri[]? uris = FileChooserParams.ParseResult((int)e.ResultCode, e.Data);
            CompleteFileChooser(uris);
        }

        void CancelPendingFileChooser()
        {
            CompleteFileChooser(null);
        }

        void CompleteFileChooser(Android.Net.Uri[]? uris)
        {
            IValueCallback? callback = pendingFilePathCallback;
            pendingFilePathCallback = null;
            callback?.OnReceiveValue(uris is null ? null : Java.Lang.Object.FromArray(uris));
        }
    }

    BridgeWebChromeClient? webChromeClient;

    protected override void ConnectHandler(Android.Webkit.WebView platformView)
    {
        base.ConnectHandler(platformView);
        platformView.Settings.DomStorageEnabled = true;
        platformView.Settings.JavaScriptEnabled = true;
        platformView.SetWebChromeClient(webChromeClient = new BridgeWebChromeClient());
        platformView.AddJavascriptInterface(new ScriptHandler(BridgeWebView.OnMessage, BridgeWebView.OnSyncMessage), "__OneGateBridge");
        if (WebViewFeature.IsFeatureSupported(WebViewFeature.DocumentStartScript))
        {
            string script = Views.BridgeWebView.CreateRpcScript();
            if (!string.IsNullOrWhiteSpace(BridgeWebView.DocumentStartScript))
                script += BridgeWebView.DocumentStartScript;
            WebViewCompat.AddDocumentStartJavaScript(platformView, script, ["*"]);
        }
    }

    protected override void DisconnectHandler(Android.Webkit.WebView platformView)
    {
        webChromeClient?.Disconnect();
        webChromeClient = null;
        platformView.StopLoading();
        platformView.RemoveJavascriptInterface("__OneGateBridge");
        platformView.SetWebChromeClient(null);
        base.DisconnectHandler(platformView);
    }
}
#endif
