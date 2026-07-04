#if ANDROID

using Android.Content.PM;

namespace NeoOrder.OneGate.Controls.Views;

public partial class BridgeWebView
{
    private partial Task LockScreenOrientationAsync(string orientation)
    {
        var activity = Platform.CurrentActivity ?? throw new InvalidOperationException("Activity not available");
        activity.RequestedOrientation = orientation switch
        {
            "any" => ScreenOrientation.FullSensor,
            "natural" => ScreenOrientation.Unspecified,
            "landscape" => ScreenOrientation.SensorLandscape,
            "portrait" => ScreenOrientation.SensorPortrait,
            "portrait-primary" => ScreenOrientation.Portrait,
            "portrait-secondary" => ScreenOrientation.ReversePortrait,
            "landscape-primary" => ScreenOrientation.Landscape,
            "landscape-secondary" => ScreenOrientation.ReverseLandscape,
            _ => throw new InvalidOperationException("Invalid screen orientation lock type")
        };
        return Task.CompletedTask;
    }

    private partial void UnlockScreenOrientation()
    {
        var activity = Platform.CurrentActivity ?? throw new InvalidOperationException("Activity not available");
        activity.RequestedOrientation = ScreenOrientation.Unspecified;
    }
}
#endif
