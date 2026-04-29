using CommunityToolkit.Maui.Alerts;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using System.Diagnostics.CodeAnalysis;
using System.Net;
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
        if (!TryCreateWebUri(url, out Uri? uri))
        {
            await Toast.Show(Strings.UnknownError);
            return;
        }
        await Browser.OpenAsync(uri, BrowserLaunchMode.SystemPreferred);
    });

    public static AsyncCommand<string> LaunchUrl { get; } = new(static async url =>
    {
        if (!TryCreateWebUri(url, out Uri? uri))
        {
            await Toast.Show(Strings.UnknownError);
            return;
        }
        if (!await Launcher.TryOpenAsync(uri))
            await Browser.OpenAsync(uri, BrowserLaunchMode.External);
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
        int appId = parameter switch
        {
            DApp dapp => dapp.Id,
            Uri uri => int.Parse(uri.Segments[2]),
            _ => throw new ArgumentException("Invalid parameter type.")
        };
#if IOS
        bool supportOpenWithDeepLink = false;
#else
        bool supportOpenWithDeepLink = true;
#endif
        if (!supportOpenWithDeepLink || !await Launcher.TryOpenAsync($"https://{SharedOptions.OneGateDomain}/app/{appId}"))
        {
            Dictionary<string, object> parameters = new();
            if (parameter is DApp) parameters["dapp"] = parameter;
            else if (parameter is Uri uri) parameters["uri"] = WebUtility.UrlEncode(uri.ToString());
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

    static bool TryCreateWebUri(string? value, [NotNullWhen(true)] out Uri? uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out uri) && uri.Scheme == Uri.UriSchemeHttps)
            return true;
        uri = null;
        return false;
    }
}

partial class AsyncCommand(Func<object?, Task> execute) : Command(async p => await execute(p))
{
    public Task ExecuteAsync(object? parameter = null) => execute(parameter);
}

partial class AsyncCommand<T>(Func<T, Task> execute) : Command(async p => await execute((T)p))
{
    public Task ExecuteAsync(T parameter) => execute(parameter);
}
