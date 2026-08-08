using NeoOrder.OneGate.Data;

namespace NeoOrder.OneGate.Services;

static class DAppCatalogPolicy
{
    public const string AllowRestrictedContentKey = "preference/allow_restricted_content";
    public const string DeveloperModeKey = "preference/developer_mode_enabled";
    public const int RestrictedContentMinimumAge = 18;

    public static async Task<bool> GetAllowRestrictedContentAsync(ApplicationDbContext dbContext)
    {
        return await dbContext.Settings.GetAsync<bool>(AllowRestrictedContentKey);
    }

    public static async Task<bool> GetDeveloperModeEnabledAsync(ApplicationDbContext dbContext)
    {
        return await dbContext.Settings.GetAsync<bool>(DeveloperModeKey);
    }

    public static bool IsVisible(DApp dapp, bool allowRestrictedContent, bool developerModeEnabled)
    {
        return IsDiscoverable(dapp, developerModeEnabled)
            && IsContentAllowed(dapp, allowRestrictedContent);
    }

    public static bool IsDiscoverable(DApp dapp, bool developerModeEnabled)
    {
        return IsSupportedOnCurrentPlatform(dapp)
            && !dapp.IsHiddenFromCatalog
            && (!dapp.IsInDevelopment || developerModeEnabled);
    }

    public static bool IsSupportedOnCurrentPlatform(DApp dapp)
    {
        return (dapp.SupportedPlatforms & GetCurrentPlatform()) != 0;
    }

    static DAppPlatforms GetCurrentPlatform()
    {
#if ANDROID
        return DAppPlatforms.Android;
#elif IOS
        return DAppPlatforms.iOS;
#elif MACCATALYST
        return DAppPlatforms.MacCatalyst;
#elif WINDOWS
        return DAppPlatforms.Windows;
#else
        throw new PlatformNotSupportedException();
#endif
    }

    public static bool IsContentAllowed(DApp dapp, bool allowRestrictedContent)
    {
        return allowRestrictedContent || !IsRestricted(dapp);
    }

    public static bool IsRestricted(DApp dapp)
    {
        return dapp.Warnings != ContentWarnings.None;
    }

    public static bool IsOldEnoughForRestrictedContent(DateTime birthDate)
    {
        return birthDate.Date.AddYears(RestrictedContentMinimumAge) <= DateTime.Today;
    }
}
