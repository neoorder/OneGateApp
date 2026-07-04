#if WINDOWS

using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;

namespace NeoOrder.OneGate.Controls.Handlers;

partial class BridgeWebViewHandler
{
    const string SyncPrompt = "__OneGateBridgeSync";

    protected override void ConnectHandler(WebView2 platformView)
    {
        base.ConnectHandler(platformView);
        platformView.CoreWebView2Initialized += PlatformView_CoreWebView2Initialized;
    }

    protected override void DisconnectHandler(WebView2 platformView)
    {
        platformView.CoreWebView2Initialized -= PlatformView_CoreWebView2Initialized;
        platformView.CoreWebView2?.WebMessageReceived -= CoreWebView2_WebMessageReceived;
        platformView.CoreWebView2?.ScriptDialogOpening -= CoreWebView2_ScriptDialogOpening;
        base.DisconnectHandler(platformView);
    }

    async void PlatformView_CoreWebView2Initialized(WebView2 sender, CoreWebView2InitializedEventArgs args)
    {
        sender.CoreWebView2.Settings.IsWebMessageEnabled = true;
        sender.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
        sender.CoreWebView2.ScriptDialogOpening += CoreWebView2_ScriptDialogOpening;
        string shim = """
            window.__OneGateBridge = {
                invoke: function(payload) {
                    window.chrome.webview.postMessage(payload);
                },
                invokeSync: function(payload) {
                    return window.prompt("__OneGateBridgeSync", payload);
                }
            };
            """;
        string script = shim + Views.BridgeWebView.CreateRpcScript();
        if (!string.IsNullOrWhiteSpace(BridgeWebView.DocumentStartScript))
            script += BridgeWebView.DocumentStartScript;
        await sender.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script);
    }

    void CoreWebView2_WebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        BridgeWebView.OnMessage(args.TryGetWebMessageAsString());
    }

    void CoreWebView2_ScriptDialogOpening(CoreWebView2 sender, CoreWebView2ScriptDialogOpeningEventArgs args)
    {
        if (args.Kind != CoreWebView2ScriptDialogKind.Prompt || args.Message != SyncPrompt)
            return;

        args.ResultText = BridgeWebView.OnSyncMessage(args.DefaultText ?? string.Empty);
        args.Accept();
    }
}
#endif
