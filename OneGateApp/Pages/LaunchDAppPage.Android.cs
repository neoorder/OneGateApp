#if ANDROID
using AndroidX.WebKit;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;

namespace NeoOrder.OneGate.Pages;

partial class LaunchDAppPage
{
    WebViewLaunchSupport? androidWebViewLaunchSupport;

    bool ShowWebViewUpdatePageIfRequired()
    {
        bool documentStartScriptSupported = WebViewFeature.IsFeatureSupported(WebViewFeature.DocumentStartScript);
        string? package = documentStartScriptSupported ? null
            : WebViewCompat.GetCurrentWebViewPackage(global::Android.App.Application.Context)?.PackageName;
        androidWebViewLaunchSupport = new(documentStartScriptSupported, package);
        if (androidWebViewLaunchSupport.CanLaunchDApp) return false;

        // Replace the unconnected view before any dApp URL is assigned. The
        // unavailable page must never construct a native bridge or load a dApp.
        ToolbarItems.Clear();
        Title = Strings.WebViewUpdateRequired;
        Button update = new() { Text = Strings.UpdateWebView };
        update.Clicked += OnUpdateWebViewClicked;
        Button close = new() { Text = Strings.CloseDApp };
        close.Clicked += async (_, _) => await this.GoBackOrCloseAsync();
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(24),
                Spacing = 20,
                VerticalOptions = LayoutOptions.Center,
                Children =
                {
                    new Label
                    {
                        Text = Strings.WebViewUpdateRequired,
                        FontSize = 24,
                        FontAttributes = FontAttributes.Bold,
                        HorizontalTextAlignment = TextAlignment.Center
                    },
                    new Label
                    {
                        Text = Strings.WebViewUpdateRequiredText,
                        HorizontalTextAlignment = TextAlignment.Center
                    },
                    update,
                    close
                }
            }
        };
        return true;
    }

    async void OnUpdateWebViewClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button || androidWebViewLaunchSupport is null) return;
        button.IsEnabled = false;
        bool opened;
        try
        {
            opened = await androidWebViewLaunchSupport.OpenUpdateAsync(uri => Launcher.Default.TryOpenAsync(uri));
        }
        catch (Exception)
        {
            // Some providers have no store/browser handler. Keep the unavailable
            // page visible and offer manual update instructions below.
            opened = false;
        }
        finally
        {
            button.IsEnabled = true;
        }
        if (!opened)
            await DisplayAlertAsync(Strings.WebViewUpdateRequired, Strings.WebViewUpdateOpenFailed, Strings.OK);
    }
}
#endif
