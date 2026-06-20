#if IOS || MACCATALYST

using CoreGraphics;
using Foundation;
using Microsoft.Maui;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using WebKit;

namespace NeoOrder.OneGate.Controls.Handlers;

partial class BridgeWebViewHandler
{
    class ScriptHandler(Action<string> onMessage) : NSObject, IWKScriptMessageHandler
    {
        public void DidReceiveScriptMessage(WKUserContentController userContentController, WKScriptMessage message)
        {
            onMessage(message.Body?.ToString()!);
        }
    }

    BridgeWebViewUIDelegate? uiDelegate;

    class BridgeWebViewUIDelegate(IWebViewHandler handler) : MauiWebViewUIDelegate(handler)
    {
        public override void RequestDeviceOrientationAndMotionPermission(
            WKWebView webView,
            WKSecurityOrigin origin,
            WKFrameInfo frame,
            Action<WKPermissionDecision> decisionHandler)
        {
            decisionHandler(frame.MainFrame ? WKPermissionDecision.Grant : WKPermissionDecision.Deny);
        }
    }

    static partial void ConfigureMapper(PropertyMapper<IWebView, IWebViewHandler> mapper)
    {
        mapper[nameof(WKUIDelegate)] = MapBridgeWKUIDelegate;
    }

    static void MapBridgeWKUIDelegate(IWebViewHandler handler, IWebView webView)
    {
        if (handler is not BridgeWebViewHandler bridgeHandler)
            return;

        bridgeHandler.PlatformView.UIDelegate = bridgeHandler.uiDelegate ??= new BridgeWebViewUIDelegate(bridgeHandler);
    }

    protected override WKWebView CreatePlatformView()
    {
        var config = MauiWKWebView.CreateConfiguration();
        var controller = new WKUserContentController();
        string shim = """
            window.__OneGateBridge = {
                invoke: function(payload) {
                    window.webkit.messageHandlers.__OneGateBridge.postMessage(payload);
                }
            };
            """;
        controller.AddUserScript(CreateDocumentStartScript(shim));
        if (!string.IsNullOrWhiteSpace(BridgeWebView.DocumentStartScript))
            controller.AddUserScript(CreateDocumentStartScript(BridgeWebView.DocumentStartScript));
        controller.AddScriptMessageHandler(new ScriptHandler(BridgeWebView.OnMessage), "__OneGateBridge");
        config.Preferences.ElementFullscreenEnabled = true;
        config.UserContentController = controller;
        return new MauiWKWebView(CGRect.Empty, this, config);
    }

    static WKUserScript CreateDocumentStartScript(string script)
    {
        return new WKUserScript(new NSString(script), WKUserScriptInjectionTime.AtDocumentStart, true);
    }
}
#endif
