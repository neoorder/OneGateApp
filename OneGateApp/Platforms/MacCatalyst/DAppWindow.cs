using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using UIKit;

namespace NeoOrder.OneGate.Platforms.MacCatalyst;

sealed class DAppWindow(Page page) : Window(page)
{
    const string ContentMappingKey = nameof(IWindow.Content);
    static int handlerConfigured;

    public static DAppWindow Create(Page page) => new(new NavigationPage(page));

    public static void ConfigureHandler()
    {
        if (Interlocked.Exchange(ref handlerConfigured, 1) != 0)
            return;

        WindowHandler.Mapper.ModifyMapping(
            ContentMappingKey,
            static (handler, window, mapContent) =>
            {
                if (window is not DAppWindow)
                {
                    (mapContent ?? throw new InvalidOperationException("The default MAUI window content mapping is not available."))
                        .Invoke(handler, window);
                    return;
                }

                IMauiContext mauiContext = handler.MauiContext
                    ?? throw new InvalidOperationException("The MAUI window context is not available.");

                // A navigation controller used directly as the root of a scaled secondary
                // Catalyst window keeps the provisional scene bounds until a manual resize.
                // Use a plain root and contain the NavigationPage only after that root appears
                // with the effective window bounds.
                handler.PlatformView.RootViewController = new NavigationHostViewController(
                    () => window.Content.ToUIViewController(mauiContext));
            });
    }

    sealed class NavigationHostViewController(
        Func<UIViewController> createNavigationController) : UIViewController
    {
        UIViewController? navigationController;

        public override void LoadView()
        {
            View = new UIView { BackgroundColor = UIColor.SystemBackground };
        }

        public override void ViewDidAppear(bool animated)
        {
            base.ViewDidAppear(animated);
            if (navigationController is not null)
                return;

            UIViewController navigation = createNavigationController();
            if (navigation is not UINavigationController)
                throw new InvalidOperationException("The NavigationPage did not create a UINavigationController.");

            AddChildViewController(navigation);
            navigation.View!.Frame = View!.Bounds;
            navigation.View.AutoresizingMask = UIViewAutoresizing.FlexibleWidth | UIViewAutoresizing.FlexibleHeight;
            View.AddSubview(navigation.View);
            navigation.DidMoveToParentViewController(this);
            navigationController = navigation;
        }
    }
}
