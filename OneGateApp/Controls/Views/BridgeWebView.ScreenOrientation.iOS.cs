#if IOS

using UIKit;

namespace NeoOrder.OneGate.Controls.Views;

public partial class BridgeWebView
{
    internal static UIInterfaceOrientationMask SupportedInterfaceOrientations
    {
        get;
        private set
        {
            field = value;
            GetCurrentViewController().SetNeedsUpdateOfSupportedInterfaceOrientations();
        }
    } = GetAllSupportedInterfaceOrientations();

    private partial Task LockScreenOrientationAsync(string orientation)
    {
        SupportedInterfaceOrientations = GetInterfaceOrientationMask(orientation);
        return Task.CompletedTask;
    }

    private partial void UnlockScreenOrientation()
    {
        SupportedInterfaceOrientations = GetAllSupportedInterfaceOrientations();
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
            "landscape-primary" => UIInterfaceOrientationMask.LandscapeRight,
            "landscape-secondary" => UIInterfaceOrientationMask.LandscapeLeft,
            _ => throw new InvalidOperationException("Invalid screen orientation lock type")
        };
    }

    static UIInterfaceOrientationMask GetAllSupportedInterfaceOrientations()
    {
        return DeviceInfo.Idiom == DeviceIdiom.Phone
            ? UIInterfaceOrientationMask.Portrait
            : UIInterfaceOrientationMask.All;
    }

    static UIViewController GetCurrentViewController()
    {
        UIWindowScene windowScene = UIApplication.SharedApplication.ConnectedScenes
            .OfType<UIWindowScene>()
            .First(s => s.ActivationState == UISceneActivationState.ForegroundActive);

        UIViewController viewController = windowScene.Windows
            .FirstOrDefault(w => w.IsKeyWindow)?.RootViewController
            ?? windowScene.Windows.First().RootViewController!;

        while (viewController.PresentedViewController is not null)
            viewController = viewController.PresentedViewController;

        return viewController switch
        {
            UINavigationController navigationController => navigationController.VisibleViewController,
            UITabBarController tabBarController => tabBarController.SelectedViewController!,
            _ => viewController
        };
    }
}
#endif
