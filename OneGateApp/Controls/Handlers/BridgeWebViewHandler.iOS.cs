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
    const string SyncPrompt = "__OneGateBridgeSync";

    class ScriptHandler(Action<string> onMessage) : NSObject, IWKScriptMessageHandler
    {
        public void DidReceiveScriptMessage(WKUserContentController userContentController, WKScriptMessage message)
        {
            onMessage(message.Body?.ToString()!);
        }
    }

    BridgeWebViewUIDelegate? uiDelegate;

    class BridgeWebViewUIDelegate : MauiWebViewUIDelegate
    {
        readonly IWebViewHandler handler;

        public BridgeWebViewUIDelegate(IWebViewHandler handler) : base(handler)
        {
            this.handler = handler;
        }

        public override void RequestDeviceOrientationAndMotionPermission(
            WKWebView webView,
            WKSecurityOrigin origin,
            WKFrameInfo frame,
            Action<WKPermissionDecision> decisionHandler)
        {
            decisionHandler(frame.MainFrame ? WKPermissionDecision.Grant : WKPermissionDecision.Deny);
        }

#pragma warning disable CS0672
        public override void RunJavaScriptTextInputPanel(
            WKWebView webView,
            string prompt,
            string? defaultText,
            WKFrameInfo frame,
            Action<string> completionHandler)
        {
            if (frame.MainFrame && prompt == SyncPrompt && handler.VirtualView is Views.BridgeWebView bridgeWebView)
            {
                completionHandler(bridgeWebView.OnSyncMessage(defaultText ?? string.Empty));
                return;
            }

#pragma warning disable CS0618
            base.RunJavaScriptTextInputPanel(webView, prompt, defaultText, frame, completionHandler);
#pragma warning restore CS0618
        }
#pragma warning restore CS0672
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
                },
                invokeSync: function(payload) {
                    return window.prompt("__OneGateBridgeSync", payload);
                }
            };
            """;
        controller.AddUserScript(CreateDocumentStartScript(shim + Views.BridgeWebView.CreateRpcScript()));
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
