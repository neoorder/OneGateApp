#if WINDOWS

using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Storage.Streams;

namespace NeoOrder.OneGate.Controls.Views;

public partial class BridgeWebView
{
    public async partial Task<byte[]> CaptureViewportAsync()
    {
        if (Handler?.PlatformView is not WebView2 webView || webView.CoreWebView2 is null)
            throw new InvalidOperationException("The DApp WebView is not ready.");
        using InMemoryRandomAccessStream randomAccessStream = new();
        await webView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, randomAccessStream);
        randomAccessStream.Seek(0);
        using Stream input = randomAccessStream.AsStreamForRead();
        using MemoryStream output = new();
        await input.CopyToAsync(output);
        return output.ToArray();
    }
}

#endif
