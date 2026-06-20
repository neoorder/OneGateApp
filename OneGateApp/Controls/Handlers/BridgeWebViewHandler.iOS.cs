#if IOS || MACCATALYST

using CoreGraphics;
using Foundation;
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

    class BridgeWebViewUIDelegate : WKUIDelegate
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

    static readonly BridgeWebViewUIDelegate uiDelegate = new();
    const string BridgeShim = """
        window.__OneGateBridge = {
            invoke: function(payload) {
                window.webkit.messageHandlers.__OneGateBridge.postMessage(payload);
            }
        };
        """;

    protected override WKWebView CreatePlatformView()
    {
        var config = new WKWebViewConfiguration();
        var controller = new WKUserContentController();
        controller.AddScriptMessageHandler(new ScriptHandler(BridgeWebView.OnMessage), "__OneGateBridge");
        InstallUserScripts(controller);
        config.UserContentController = controller;
        return new MauiWKWebView(CGRect.Empty, this, config)
        {
            UIDelegate = uiDelegate
        };
    }

    partial void UpdateDocumentStartScriptCore()
    {
        if (PlatformView is WKWebView webView)
            InstallUserScripts(webView.Configuration.UserContentController);
    }

    void InstallUserScripts(WKUserContentController controller)
    {
        controller.RemoveAllUserScripts();
        controller.AddUserScript(CreateDocumentStartScript(BridgeShim));
        if (!string.IsNullOrWhiteSpace(BridgeWebView.DocumentStartScript))
            controller.AddUserScript(CreateDocumentStartScript(BridgeWebView.DocumentStartScript));
    }

    static WKUserScript CreateDocumentStartScript(string source)
    {
        return new WKUserScript(new NSString(source), WKUserScriptInjectionTime.AtDocumentStart, true);
    }
}
#endif
