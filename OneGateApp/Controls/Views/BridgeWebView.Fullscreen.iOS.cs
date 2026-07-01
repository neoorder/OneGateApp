#if IOS

using Microsoft.Maui.Controls.PlatformConfiguration;
using Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific;
using UIKit;

namespace NeoOrder.OneGate.Controls.Views;

public partial class BridgeWebView
{
    ContentPage? fullscreenPage;
    bool isFullscreen;
    StatusBarHiddenMode previousStatusBarHiddenMode;
    bool previousHomeIndicatorAutoHidden;
    bool previousNavBarIsVisible;
    SafeAreaEdges previousSafeAreaEdges;

    private partial Task EnterFullscreenAsync()
    {
        if (isFullscreen)
            return Task.CompletedTask;

        ContentPage page = GetParentContentPage() ?? throw new InvalidOperationException("Page not available");

        isFullscreen = true;
        fullscreenPage = page;
        previousNavBarIsVisible = Shell.GetNavBarIsVisible(page);
        previousSafeAreaEdges = page.SafeAreaEdges;
        previousStatusBarHiddenMode = Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.Page.GetPrefersStatusBarHidden(page);
        previousHomeIndicatorAutoHidden = Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.Page.GetPrefersHomeIndicatorAutoHidden(page);

        Shell.SetNavBarIsVisible(page, false);
        page.SafeAreaEdges = SafeAreaEdges.None;
        SetSystemChromeHidden(page, StatusBarHiddenMode.True, true);
        return Task.CompletedTask;
    }

    private partial Task ExitFullscreenAsync()
    {
        RestoreFullscreenState();
        return Task.CompletedTask;
    }

    private partial void RestoreFullscreenState()
    {
        if (!isFullscreen)
            return;

        ContentPage? page = fullscreenPage ?? GetParentContentPage();
        if (page is not null)
        {
            Shell.SetNavBarIsVisible(page, previousNavBarIsVisible);
            page.SafeAreaEdges = previousSafeAreaEdges;
            SetSystemChromeHidden(page, previousStatusBarHiddenMode, previousHomeIndicatorAutoHidden);
        }
        isFullscreen = false;
        fullscreenPage = null;
    }

    ContentPage? GetParentContentPage()
    {
        Element? element = this;
        while (element is not null)
        {
            if (element is ContentPage page)
                return page;

            element = element.Parent;
        }

        return Window?.Page as ContentPage;
    }

    static void SetSystemChromeHidden(ContentPage page, StatusBarHiddenMode statusBarMode, bool homeIndicatorAutoHidden)
    {
        page.On<iOS>()
            .SetPrefersStatusBarHidden(statusBarMode)
            .SetPrefersHomeIndicatorAutoHidden(homeIndicatorAutoHidden);

        UIViewController viewController = GetCurrentViewController();
        viewController.SetNeedsStatusBarAppearanceUpdate();
        viewController.SetNeedsUpdateOfHomeIndicatorAutoHidden();
    }
}
#endif
