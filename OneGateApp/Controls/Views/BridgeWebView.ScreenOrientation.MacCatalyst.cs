#if MACCATALYST

namespace NeoOrder.OneGate.Controls.Views;

public partial class BridgeWebView
{
    private partial Task LockScreenOrientationAsync(string orientation)
    {
        throw new NotSupportedException("Screen orientation lock is not supported on Mac Catalyst");
    }

    private partial Task UnlockScreenOrientationAsync()
    {
        return Task.CompletedTask;
    }
}
#endif
