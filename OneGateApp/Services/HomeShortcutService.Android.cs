#if ANDROID

using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Java.Lang;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Platforms.Android;
using AndroidIcon = Android.Graphics.Drawables.Icon;

namespace NeoOrder.OneGate.Services;

class HomeShortcutService(HttpClient httpClient) : IHomeShortcutService
{
    public bool IsSupported
    {
        get
        {
            Activity? activity = Platform.CurrentActivity;
            if (activity is null) return false;
            ShortcutManager? shortcutManager = GetShortcutManager(activity);
            return shortcutManager?.IsRequestPinShortcutSupported == true;
        }
    }

    public async Task<bool> AddShortcutAsync(DApp dapp)
    {
        Activity activity = Platform.CurrentActivity!;
        ShortcutManager? shortcutManager = GetShortcutManager(activity);
        if (shortcutManager?.IsRequestPinShortcutSupported != true)
            throw new NotSupportedException();
        string title = dapp.NameLocalizer.Localize()!;
        string deepLink = $"https://{SharedOptions.OneGateDomain}/app/{dapp.Id}";
        var deepLinkUri = Android.Net.Uri.Parse(deepLink);
        var intent = new Intent(Intent.ActionView, deepLinkUri);
        intent.SetClass(activity, typeof(MainActivity));
        intent.SetPackage(activity.PackageName);
        intent.AddFlags(ActivityFlags.ClearTop | ActivityFlags.SingleTop);
        AndroidIcon icon = await CreateShortcutIconAsync(activity, dapp.IconUrl);
        var shortcut = new ShortcutInfo.Builder(activity, $"dapp-{dapp.Id}")
            .SetShortLabel(title)
            .SetLongLabel(title)
            .SetIcon(icon)
            .SetIntent(intent)
            .Build();
        return shortcutManager.RequestPinShortcut(shortcut, null);
    }

    static ShortcutManager? GetShortcutManager(Context context)
    {
        return context.GetSystemService(Class.FromType(typeof(ShortcutManager))) as ShortcutManager;
    }

    async Task<AndroidIcon> CreateShortcutIconAsync(Context context, string? iconUrl)
    {
        if (!string.IsNullOrWhiteSpace(iconUrl) && Uri.TryCreate(iconUrl, UriKind.Absolute, out Uri? uri))
        {
            try
            {
                byte[] bytes = await httpClient.GetByteArrayAsync(uri);
                Bitmap? bitmap = BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length);
                if (bitmap is not null)
                    return AndroidIcon.CreateWithBitmap(bitmap);
            }
            catch
            {
                // Fall back to the app icon when the dApp icon cannot be downloaded or decoded.
            }
        }
        return AndroidIcon.CreateWithResource(context, Resource.Mipmap.appicon);
    }
}

#endif
