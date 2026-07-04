using NeoOrder.OneGate.Data;

namespace NeoOrder.OneGate.Services;

public partial class UpdateService(HttpClient httpClient, ApplicationDbContext dbContext)
{
    public event EventHandler? UpdateStatusChanged;

    public bool IsUpdating { get; private set; }

    public async Task<bool> CheckForUpdatesAsync()
    {
        if (await dbContext.Settings.GetAsync<bool>("system/updates"))
            return true;

        bool available = await CheckForUpdatesInternalAsync();
        if (available) await SetUpdateAvailableAsync(true);
        return available;
    }

    async Task SetUpdateAvailableAsync(bool available)
    {
        if (available)
            await dbContext.Settings.PutAsync("system/updates", true);
        else
            await dbContext.Settings.DeleteAsync("system/updates");
        UpdateStatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private partial Task<bool> CheckForUpdatesInternalAsync();

    async Task<bool> CheckForUpdatesFallbackAsync()
    {
        Version latest = new(await httpClient.GetStringAsync("/api/app/version"));
        return latest > AppInfo.Version;
    }

    public async Task UpdateAsync()
    {
        if (IsUpdating) return;
        IsUpdating = true;
        try
        {
            await UpdateInternalAsync();
        }
        catch (UpdateUnavailableException)
        {
            IsUpdating = false;
            await SetUpdateAvailableAsync(false);
            throw;
        }
        catch
        {
            IsUpdating = false;
            throw;
        }
    }

    private partial Task UpdateInternalAsync();

    async Task UpdateFallbackAsync()
    {
        await Browser.OpenAsync($"https://{SharedOptions.OneGateDomain}/download", BrowserLaunchMode.External);
        IsUpdating = false;
    }
}

class UpdateUnavailableException : InvalidOperationException
{
    public UpdateUnavailableException() : base("No update available.")
    {
    }
}
