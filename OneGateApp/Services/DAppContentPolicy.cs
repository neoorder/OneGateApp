using NeoOrder.OneGate.Data;

namespace NeoOrder.OneGate.Services;

static class DAppContentPolicy
{
    public const string AllowRestrictedContentKey = "preference/allow_restricted_content";
    public const int RestrictedContentMinimumAge = 18;

    public static async Task<bool> GetAllowRestrictedContentAsync(ApplicationDbContext dbContext)
    {
        return await dbContext.Settings.GetAsync<bool>(AllowRestrictedContentKey);
    }

    public static bool IsRestricted(DApp dapp)
    {
        return dapp.Warnings != ContentWarnings.None;
    }

    public static bool IsVisible(DApp dapp, bool allowRestrictedContent)
    {
        return allowRestrictedContent || !IsRestricted(dapp);
    }

    public static bool IsOldEnoughForRestrictedContent(DateTime birthDate)
    {
        return birthDate.Date.AddYears(RestrictedContentMinimumAge) <= DateTime.Today;
    }
}
