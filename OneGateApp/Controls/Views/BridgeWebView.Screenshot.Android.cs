#if ANDROID

using Android.Graphics;
using Android.OS;
using Android.Views;

namespace NeoOrder.OneGate.Controls.Views;

public partial class BridgeWebView
{
    sealed class PixelCopyListener(TaskCompletionSource<int> completion) : Java.Lang.Object, PixelCopy.IOnPixelCopyFinishedListener
    {
        public void OnPixelCopyFinished(int copyResult) => completion.TrySetResult(copyResult);
    }

    public async partial Task<byte[]> CaptureViewportAsync()
    {
        if (Handler?.PlatformView is not Android.Webkit.WebView webView || webView.Width <= 0 || webView.Height <= 0)
            throw new InvalidOperationException("The DApp WebView is not ready.");
        Android.App.Activity activity = Platform.CurrentActivity
            ?? throw new InvalidOperationException("The DApp activity is unavailable.");
        int[] location = new int[2];
        webView.GetLocationInWindow(location);
        Android.Graphics.Rect source = new(location[0], location[1], location[0] + webView.Width, location[1] + webView.Height);
        using Bitmap bitmap = Bitmap.CreateBitmap(webView.Width, webView.Height, Bitmap.Config.Argb8888!)!;
        TaskCompletionSource<int> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        PixelCopy.Request(activity.Window!, source, bitmap, new PixelCopyListener(completion), new Handler(Looper.MainLooper!));
        int result = await completion.Task;
        if (result != (int)PixelCopyResult.Success)
            throw new InvalidOperationException($"Android PixelCopy failed: {result}.");
        using MemoryStream stream = new();
        if (!bitmap.Compress(Bitmap.CompressFormat.Png!, 100, stream))
            throw new InvalidOperationException("Unable to encode the DApp screenshot.");
        return stream.ToArray();
    }
}

#endif
