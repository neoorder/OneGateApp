#if IOS

using UIKit;

namespace NeoOrder.OneGate.Controls.Views;

public partial class BridgeWebView
{
    internal static UIInterfaceOrientationMask SupportedInterfaceOrientations { get; private set; } = GetDefaultSupportedInterfaceOrientations();

    internal static UIInterfaceOrientationMask GetSupportedInterfaceOrientations()
    {
        return SupportedInterfaceOrientations;
    }

    private partial Task LockScreenOrientationAsync(string orientation)
    {
        UIInterfaceOrientationMask mask = GetInterfaceOrientationMask(orientation);
        SupportedInterfaceOrientations = mask;
        return RequestGeometryUpdateAsync(mask);
    }

    private partial Task UnlockScreenOrientationAsync()
    {
        UIInterfaceOrientationMask mask = GetDefaultSupportedInterfaceOrientations();
        SupportedInterfaceOrientations = mask;
        return RequestGeometryUpdateAsync(mask);
    }

    static UIInterfaceOrientationMask GetInterfaceOrientationMask(string orientation)
    {
        return orientation switch
        {
            "any" => GetAllSupportedInterfaceOrientations(),
            "natural" => UIInterfaceOrientationMask.Portrait,
            "landscape" => UIInterfaceOrientationMask.Landscape,
            "portrait" => UIInterfaceOrientationMask.Portrait,
            "portrait-primary" => UIInterfaceOrientationMask.Portrait,
            "portrait-secondary" when DeviceInfo.Idiom != DeviceIdiom.Phone => UIInterfaceOrientationMask.PortraitUpsideDown,
            "portrait-secondary" => throw new NotSupportedException("Portrait secondary orientation is not supported on iPhone"),
            "landscape-primary" => UIInterfaceOrientationMask.LandscapeLeft,
            "landscape-secondary" => UIInterfaceOrientationMask.LandscapeRight,
            _ => throw new InvalidOperationException("Invalid screen orientation lock type")
        };
    }

    static UIInterfaceOrientationMask GetDefaultSupportedInterfaceOrientations()
    {
        return DeviceInfo.Idiom == DeviceIdiom.Phone
            ? UIInterfaceOrientationMask.Portrait
            : UIInterfaceOrientationMask.All;
    }

    static UIInterfaceOrientationMask GetAllSupportedInterfaceOrientations()
    {
        return DeviceInfo.Idiom == DeviceIdiom.Phone
            ? UIInterfaceOrientationMask.Portrait | UIInterfaceOrientationMask.LandscapeLeft | UIInterfaceOrientationMask.LandscapeRight
            : UIInterfaceOrientationMask.All;
    }

    static Task RequestGeometryUpdateAsync(UIInterfaceOrientationMask mask)
    {
        UIWindowScene? windowScene = null;
        foreach (var scene in UIApplication.SharedApplication.ConnectedScenes)
        {
            if (scene is UIWindowScene { ActivationState: UISceneActivationState.ForegroundActive } activeScene)
            {
                windowScene = activeScene;
                break;
            }
        }

        if (windowScene is null)
        {
            UIViewController.AttemptRotationToDeviceOrientation();
            return Task.CompletedTask;
        }

        TaskCompletionSource tcs = new();
        windowScene.RequestGeometryUpdate(new UIWindowSceneGeometryPreferencesIOS(mask), error =>
        {
            if (error is null)
                tcs.TrySetResult();
            else
                tcs.TrySetException(new InvalidOperationException(error.LocalizedDescription));
        });
        UIViewController.AttemptRotationToDeviceOrientation();
        return tcs.Task;
    }
}
#endif
