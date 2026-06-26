#if WINDOWS

using NeoOrder.OneGate.Properties;
using Windows.Services.Store;

namespace NeoOrder.OneGate.Services;

partial class UpdateService
{
    readonly StoreContext context = StoreContext.GetDefault();

    private partial async Task<bool> CheckForUpdatesInternalAsync()
    {
        if (await IsMicrosoftStore())
        {
            var updates = await context.GetAppAndOptionalStorePackageUpdatesAsync();
            return updates.Count > 0;
        }
        return await CheckForUpdatesFallbackAsync();
    }

    private partial async Task UpdateInternalAsync()
    {
        if (await IsMicrosoftStore())
        {
            var updates = await context.GetAppAndOptionalStorePackageUpdatesAsync();
            if (updates.Count == 0)
                throw new UpdateUnavailableException();
            var result = await context.RequestDownloadAndInstallStorePackageUpdatesAsync(updates);
            switch (result.OverallState)
            {
                case StorePackageUpdateState.Completed:
                    bool accepted = await Shell.Current.DisplayAlertAsync(Strings.UpdateApp, Strings.UpdateInstalledRestartNow, Strings.Restart, Strings.Cancel);
                    if (accepted) Application.Current!.Quit();
                    IsUpdating = false;
                    break;
                case StorePackageUpdateState.Canceled:
                    IsUpdating = false;
                    break;
                default:
                    throw new InvalidOperationException($"Update failed with state: {result.OverallState}");
            }
        }
        else
        {
            await UpdateFallbackAsync();
        }
    }

    async Task<bool> IsMicrosoftStore()
    {
        var license = await context.GetAppLicenseAsync();
        if (string.IsNullOrEmpty(license.SkuStoreId)) return false;
        return true;
    }
}
#endif
