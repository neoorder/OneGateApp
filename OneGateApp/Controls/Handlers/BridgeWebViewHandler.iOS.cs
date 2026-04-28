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

    protected override WKWebView CreatePlatformView()
    {
        var config = new WKWebViewConfiguration();
        var controller = new WKUserContentController();
        string shim = """
            window.__OneGateBridge = {
                invoke: function(payload) {
                    window.webkit.messageHandlers.__OneGateBridge.postMessage(payload);
                }
            };
            """;
        var script = new WKUserScript(new NSString(shim), WKUserScriptInjectionTime.AtDocumentStart, true);
        controller.AddUserScript(script);
        controller.AddScriptMessageHandler(new ScriptHandler(BridgeWebView.OnMessage), "__OneGateBridge");
        config.UserContentController = controller;
        return new MauiWKWebView(CGRect.Empty, this, config);
    }
}
#endif
