namespace NeoOrder.OneGate.Services;

sealed class WebViewLaunchSupport(bool documentStartScriptSupported, string? providerPackage)
{
    const string DefaultProviderPackage = "com.google.android.webview";

    public bool CanLaunchDApp => documentStartScriptSupported;

    public async Task<bool> OpenUpdateAsync(Func<Uri, Task<bool>> tryOpen)
    {
        string package = Uri.EscapeDataString(string.IsNullOrWhiteSpace(providerPackage)
            ? DefaultProviderPackage
            : providerPackage);
        if (await tryOpen(new Uri($"market://details?id={package}"))) return true;
        return await tryOpen(new Uri($"https://play.google.com/store/apps/details?id={package}"));
    }
}
