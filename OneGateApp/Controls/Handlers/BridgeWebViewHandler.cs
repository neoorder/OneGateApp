using Microsoft.Maui.Handlers;
using NeoOrder.OneGate.Controls.Views;

namespace NeoOrder.OneGate.Controls.Handlers;

public partial class BridgeWebViewHandler : WebViewHandler
{
    protected BridgeWebView BridgeWebView => (BridgeWebView)VirtualView;

    public BridgeWebViewHandler() : base(CreateMapper(), CommandMapper)
    {
    }

    static IPropertyMapper<IWebView, IWebViewHandler> CreateMapper()
    {
        var mapper = new PropertyMapper<IWebView, IWebViewHandler>(WebViewHandler.Mapper);
        ConfigureMapper(mapper);
        return mapper;
    }

    static partial void ConfigureMapper(PropertyMapper<IWebView, IWebViewHandler> mapper);
}
