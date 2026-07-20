#if IOS || MACCATALYST

using WebKit;

namespace NeoOrder.OneGate.Controls.Views;

public partial class BridgeWebView
{
    public async partial Task<byte[]> CaptureViewportAsync()
    {
        if (Handler?.PlatformView is not WKWebView webView)
            throw new InvalidOperationException("The DApp WebView is not ready.");
        using WKSnapshotConfiguration configuration = new();
        using UIKit.UIImage? image = await webView.TakeSnapshotAsync(configuration);
        if (image is null)
            throw new InvalidOperationException("The DApp WebView did not produce a snapshot.");
        using Foundation.NSData? data = image.AsPNG();
        if (data is null)
            throw new InvalidOperationException("Unable to encode the DApp screenshot.");
        return data.ToArray();
    }
}

#endif
