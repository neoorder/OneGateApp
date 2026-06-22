#if IOS

using UIKit;

namespace NeoOrder.OneGate.Controls.Views;

public partial class BridgeWebView
{
    internal static UIInterfaceOrientationMask SupportedInterfaceOrientations { get; private set; } = GetAllSupportedInterfaceOrientations();

    private partial Task LockScreenOrientationAsync(string orientation)
    {
        SupportedInterfaceOrientations = GetInterfaceOrientationMask(orientation);
        RequestGeometryUpdate(SupportedInterfaceOrientations);
        return Task.CompletedTask;
    }

    private partial void UnlockScreenOrientation()
    {
        SupportedInterfaceOrientations = GetAllSupportedInterfaceOrientations();
        RequestGeometryUpdate(SupportedInterfaceOrientations);
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

    static UIInterfaceOrientationMask GetAllSupportedInterfaceOrientations()
    {
        return DeviceInfo.Idiom == DeviceIdiom.Phone
            ? UIInterfaceOrientationMask.Portrait
            : UIInterfaceOrientationMask.All;
    }

    static void RequestGeometryUpdate(UIInterfaceOrientationMask mask)
    {
        var windowScene = GetActiveWindowScene();
        if (windowScene is null)
            return;

        GetCurrentViewController(windowScene)?.SetNeedsUpdateOfSupportedInterfaceOrientations();
        windowScene.RequestGeometryUpdate(new UIWindowSceneGeometryPreferencesIOS(mask), null);
    }

    static UIViewController? GetCurrentViewController(UIWindowScene windowScene)
    {
        UIViewController? viewController = windowScene.Windows.FirstOrDefault(w => w.IsKeyWindow)?.RootViewController
            ?? windowScene.Windows.FirstOrDefault()?.RootViewController;

        while (viewController?.PresentedViewController is not null)
            viewController = viewController.PresentedViewController;

        return viewController switch
        {
            UINavigationController navigationController => navigationController.VisibleViewController,
            UITabBarController tabBarController => tabBarController.SelectedViewController,
            _ => viewController
        };
    }

    static UIWindowScene? GetActiveWindowScene()
    {
        return UIApplication.SharedApplication.ConnectedScenes
                .OfType<UIWindowScene>()
                .FirstOrDefault(s => s.ActivationState == UISceneActivationState.ForegroundActive);
    }
}
#endif
