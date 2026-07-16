#if ANDROID

using Android.Webkit;
using AndroidX.WebKit;
using Java.Interop;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
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

    class BridgeWebChromeClient(BridgeWebViewHandler handler) : MauiWebChromeClient(handler)
    {
        static readonly string[] VideoCaptureResources = [PermissionRequest.ResourceVideoCapture];

        public override void OnPermissionRequest(PermissionRequest? request)
        {
            if (request is null)
                return;

            if (!IsCameraPermissionRequest(request) || !IsSameWebOrigin(request.Origin?.ToString(), handler.PlatformView.Url))
            {
                request.Deny();
                return;
            }

            _ = MainThread.InvokeOnMainThreadAsync(() => HandlePermissionRequestAsync(request));
        }

        static bool IsCameraPermissionRequest(PermissionRequest request)
        {
            string[]? resources = request.GetResources();
            return resources is { Length: > 0 }
                && resources.Contains(PermissionRequest.ResourceVideoCapture)
                && resources.All(resource => resource == PermissionRequest.ResourceVideoCapture);
        }

        static async Task HandlePermissionRequestAsync(PermissionRequest request)
        {
            try
            {
                if (!await ConfirmCameraPermissionAsync(request.Origin?.ToString()))
                {
                    request.Deny();
                    return;
                }

                PermissionStatus status = await Permissions.CheckStatusAsync<Permissions.Camera>();
                if (status != PermissionStatus.Granted)
                    status = await Permissions.RequestAsync<Permissions.Camera>();

                if (status == PermissionStatus.Granted)
                    request.Grant(VideoCaptureResources);
                else
                    request.Deny();
            }
            catch
            {
                request.Deny();
            }
        }
    }

    static partial void ConfigureMapper(PropertyMapper<IWebView, IWebViewHandler> mapper)
    {
        mapper[nameof(WebChromeClient)] = MapBridgeWebChromeClient;
    }

    static void MapBridgeWebChromeClient(IWebViewHandler handler, IWebView webView)
    {
        if (handler is BridgeWebViewHandler bridgeHandler)
            bridgeHandler.PlatformView.SetWebChromeClient(new BridgeWebChromeClient(bridgeHandler));
    }

    protected override void ConnectHandler(Android.Webkit.WebView platformView)
    {
        base.ConnectHandler(platformView);
        platformView.Settings.DomStorageEnabled = true;
        platformView.Settings.JavaScriptEnabled = true;
        platformView.Settings.MediaPlaybackRequiresUserGesture = false;
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
