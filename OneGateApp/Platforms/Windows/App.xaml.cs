using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.PushNotifications;
using NeoOrder.OneGate.Services.Notifications;

namespace NeoOrder.OneGate.WinUI;

public partial class App : MauiWinUIApplication
{
    public App()
    {
        InitializeComponent();
    }

    Microsoft.UI.Xaml.LaunchActivatedEventArgs? launchArguments;
    AppActivationArguments? activation;
    bool uiCreated;

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        launchArguments = args;
        var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        WindowsPushNotifications.Initialize(values => dispatcher.TryEnqueue(() => OpenNotification(values)));
        activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        if (activation.Kind == ExtendedActivationKind.Push && activation.Data is PushNotificationReceivedEventArgs push)
        {
            var deferral = push.GetDeferral();
            try { WindowsPushNotifications.Show(push.Payload); }
            finally { deferral.Complete(); }
            // Raw delivery must not create a wallet window or establish a Relay connection.
            if (!uiCreated) Exit();
            return;
        }
        if (activation.Kind == ExtendedActivationKind.AppNotification && activation.Data is AppNotificationActivatedEventArgs notification)
        {
            OpenNotification(new Dictionary<string, string>(notification.Arguments));
            return;
        }
        // On COM launches the invocation event may follow OnLaunched.
        if (args.Arguments.Contains("----AppNotificationActivated:", StringComparison.Ordinal)
            && WindowsPushNotifications.RegistrationError is null) return;
        uiCreated = true;
        base.OnLaunched(args);
    }

    void CreateUi()
    {
        if (uiCreated || launchArguments is null) return;
        uiCreated = true;
        base.OnLaunched(launchArguments);
    }

    void OpenNotification(IReadOnlyDictionary<string, string> values)
    {
        CreateUi();
        if (!uiCreated) return;
        var services = IPlatformApplication.Current!.Services;
        services.GetRequiredService<PushNavigationService>().Receive(values);
        var application = (OneGate.App)services.GetRequiredService<IApplication>();
        var window = application.Windows.FirstOrDefault(w => w.Page is AppShell) ?? application.Windows.FirstOrDefault();
        if (window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window native)
        {
            if (native.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter) presenter.Restore();
            native.Activate();
        }
    }

    protected override MauiApp CreateMauiApp()
    {
        MauiApp app = MauiProgram.CreateMauiApp();
        Workarounds.FixPickerHandler();
        var arguments = activation ?? AppInstance.GetCurrent().GetActivatedEventArgs();
        if (arguments.Kind == ExtendedActivationKind.Protocol && arguments.Data is Windows.ApplicationModel.Activation.ProtocolActivatedEventArgs protocol)
        {
            var application = (OneGate.App)app.Services.GetRequiredService<IApplication>();
            if (!application.ProcessAppLinkUri(protocol.Uri))
                application.Quit();
        }
        return app;
    }
}
