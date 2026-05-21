using CommunityToolkit.Maui.Alerts;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using ZXing.Net.Maui;

namespace NeoOrder.OneGate.Pages;

static class Commands
{
    public static AsyncCommand Copy { get; } = new(static async obj =>
    {
        await Clipboard.SetTextAsync(obj?.ToString());
#if !ANDROID
        await Toast.Show(Strings.CopiedToClipboard);
#endif
    });

    public static AsyncCommand<string> OpenUrl { get; } = new(static async url =>
    {
        await Browser.OpenAsync(url, BrowserLaunchMode.SystemPreferred);
    });

    public static AsyncCommand<string> LaunchUrl { get; } = new(static async url =>
    {
        if (!await Launcher.TryOpenAsync(url))
            await Browser.OpenAsync(url, BrowserLaunchMode.External);
    });

    public static AsyncCommand<IShareable> Share { get; } = new(static async shareable =>
    {
        await Microsoft.Maui.ApplicationModel.DataTransfer.Share.RequestAsync(new ShareTextRequest
        {
            Text = shareable.Text,
            Uri = shareable.Uri
        });
    });

    public static AsyncCommand<string> GotoPage { get; } = new(static async route =>
    {
        await Shell.Current.GoToAsync(route);
    });

    public static AsyncCommand<string> ScanQRCode { get; } = new(static async query =>
    {
        if (BarcodeScanning.IsSupported)
            await Shell.Current.GoToAsync("scan" + query);
        else
            await Toast.Show(Strings.ErrorMessageCameraFailure);
    });

    public static AsyncCommand<DApp> AddToFavorites { get; } = new(static async dapp =>
    {
        var dbContext = Application.Current!.Handler.GetRequiredService<ApplicationDbContext>();
        List<int> favorites = await dbContext.Settings.GetAsync<List<int>>("dapps/favorite") ?? [];
        if (!favorites.Remove(dapp.Id)) favorites.Insert(0, dapp.Id);
        await dbContext.Settings.PutAsync("dapps/favorite", favorites);
        GlobalStates.Invalidate<DAppsPage>();
    });

    public static AsyncCommand<DApp> AddToHomeScreen { get; } = new(static async dapp =>
    {
        IHomeShortcutService service = Application.Current!.Handler.GetRequiredService<IHomeShortcutService>();
        await service.AddShortcutAsync(dapp);
    });

    public static AsyncCommand LaunchDApp { get; } = new(static async parameter =>
    {
        DApp? dapp = parameter as DApp;
        Uri? uri = parameter as Uri;
        if (dapp is null && uri is null) throw new ArgumentException("Invalid parameter type.");
        uri ??= new($"https://{SharedOptions.OneGateDomain}/app/{dapp!.Id}");
        int appId = dapp?.Id ?? int.Parse(uri.Segments[2]);
#if ANDROID
        var activity = Platform.CurrentActivity!;
        string canonicalUri = $"https://{SharedOptions.OneGateDomain}/app/{appId}";
        var intent = new Android.Content.Intent(activity, typeof(Platforms.Android.MainActivity));
        intent.SetAction(Android.Content.Intent.ActionView);
        intent.SetData(Android.Net.Uri.Parse(canonicalUri));
        intent.AddFlags(Android.Content.ActivityFlags.NewDocument);
        if (!string.IsNullOrEmpty(uri.Query))
            intent.PutExtra("org.neoorder.onegate.ORIGINAL_URI", uri.AbsoluteUri);
        activity.StartActivity(intent);
#else
#if IOS || MACCATALYST
        bool supportOpenWithDeepLink = false;
#else
        bool supportOpenWithDeepLink = true;
#endif
        if (!supportOpenWithDeepLink || !await Launcher.TryOpenAsync(uri))
        {
            Dictionary<string, object> parameters = new();
            if (dapp != null) parameters["dapp"] = dapp;
            parameters["uri"] = System.Net.WebUtility.UrlEncode(uri.ToString());
#if IOS
            bool supportMultiWindow = DeviceInfo.Idiom == DeviceIdiom.Desktop || DeviceInfo.Idiom == DeviceIdiom.Tablet;
#else
            bool supportMultiWindow = true;
#endif
            if (supportMultiWindow)
            {
                LaunchDAppPage page = Application.Current!.Handler.GetServiceProvider().GetServiceOrCreateInstance<LaunchDAppPage>();
                page.ApplyQueryAttributes(parameters);
                Application.Current!.OpenWindow(new Window(new NavigationPage(page)));
            }
            else
            {
                await Shell.Current.GoToAsync("//dapps/launch", parameters);
            }
        }
#endif
    });

    public static AsyncCommand CheckForUpdates { get; } = new(static async parameter =>
    {
        bool silent = parameter is true;
        var dbContext = Application.Current!.Handler.GetRequiredService<ApplicationDbContext>();
        if (await dbContext.Settings.GetAsync<bool>("system/updates"))
        {
            if (!silent) await Toast.Show(Strings.NewVersionAvailable);
            return;
        }
        if (!silent) await Toast.Show(Strings.CheckingForUpdates + "…");
        UpdateService service = Application.Current!.Handler.GetRequiredService<UpdateService>();
        bool updateAvailable;
        try
        {
            updateAvailable = await service.CheckForUpdatesAsync();
        }
        catch
        {
            if (!silent) await Toast.Show(Strings.NoUpdatesAvailable);
            return;
        }
        if (updateAvailable)
        {
            await dbContext.Settings.PutAsync("system/updates", true);
            if (!silent) await Toast.Show(Strings.NewVersionAvailable);
        }
        else
        {
            if (!silent) await Toast.Show(Strings.NoUpdatesAvailable);
        }
    });

    public static AsyncCommand UpdateApp { get; } = new(static async _ =>
    {
        UpdateService service = Application.Current!.Handler.GetRequiredService<UpdateService>();
        await service.UpdateAsync();
    });
}

partial class AsyncCommand(Func<object?, Task> execute) : Command(async p => await execute(p))
{
    public Task ExecuteAsync(object? parameter = null) => execute(parameter);
}

partial class AsyncCommand<T>(Func<T, Task> execute) : Command(async p => await execute((T)p))
{
    public Task ExecuteAsync(T parameter) => execute(parameter);
}
