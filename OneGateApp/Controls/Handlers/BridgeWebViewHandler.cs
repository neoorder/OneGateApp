using Microsoft.Maui.Handlers;
using NeoOrder.OneGate.Controls.Views;

namespace NeoOrder.OneGate.Controls.Handlers;

public partial class BridgeWebViewHandler : WebViewHandler
{
    protected BridgeWebView BridgeWebView => (BridgeWebView)VirtualView;

    public void UpdateDocumentStartScript()
    {
        UpdateDocumentStartScriptCore();
    }

    partial void UpdateDocumentStartScriptCore();
}
