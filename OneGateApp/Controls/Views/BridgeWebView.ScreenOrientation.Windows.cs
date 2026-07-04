#if WINDOWS

namespace NeoOrder.OneGate.Controls.Views;

public partial class BridgeWebView
{
    private partial Task LockScreenOrientationAsync(string orientation)
    {
        throw new NotSupportedException("Screen orientation lock is not supported on Windows");
    }

    private partial void UnlockScreenOrientation()
    {
    }
}
#endif
