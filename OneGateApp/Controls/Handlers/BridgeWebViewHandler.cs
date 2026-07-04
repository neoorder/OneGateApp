using Microsoft.Maui.Handlers;
using NeoOrder.OneGate.Controls.Views;
using NeoOrder.OneGate.Properties;

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

    static Task<bool> ConfirmCameraPermissionAsync(string? originUrl)
    {
        string origin = GetDisplayOrigin(originUrl);
        return Shell.Current.DisplayAlertAsync(
            Strings.DAppCameraPermissionTitle,
            string.Format(Strings.DAppCameraPermissionText, origin),
            Strings.Authorize,
            Strings.Cancel);
    }

    static bool IsSameWebOrigin(string? originUrl, string? pageUrl)
    {
        if (!Uri.TryCreate(originUrl, UriKind.Absolute, out Uri? origin))
            return false;
        if (!Uri.TryCreate(pageUrl, UriKind.Absolute, out Uri? page))
            return false;

        return StringComparer.OrdinalIgnoreCase.Equals(origin.Scheme, page.Scheme)
            && StringComparer.OrdinalIgnoreCase.Equals(origin.Host, page.Host)
            && origin.Port == page.Port;
    }

    static string GetDisplayOrigin(string? originUrl)
    {
        if (Uri.TryCreate(originUrl, UriKind.Absolute, out Uri? origin))
            return origin.Authority;

        return string.IsNullOrWhiteSpace(originUrl) ? "this dApp" : originUrl;
    }
}
