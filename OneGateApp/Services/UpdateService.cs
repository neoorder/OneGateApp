using NeoOrder.OneGate.Data;
using Microsoft.EntityFrameworkCore;

namespace NeoOrder.OneGate.Services;

public partial class UpdateService(HttpClient httpClient, IServiceScopeFactory scopeFactory, TimeProvider timeProvider)
{
    const int StateId = 1;
    readonly string appVersion = AppInfo.Version.ToString();
    readonly SemaphoreSlim stateLock = new(1, 1);
    readonly SemaphoreSlim checkLock = new(1, 1);
    Task<bool>? currentCheck;

    public event EventHandler? UpdateStatusChanged;

    public bool IsUpdating { get; private set; }

    public Task<AppUpdateState> GetStateAsync() => UpdateStateAsync();

    async Task<AppUpdateState> UpdateStateAsync(Action<AppUpdateState>? update = null)
    {
        await stateLock.WaitAsync();
        try
        {
            using var scope = scopeFactory.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var state = await database.AppUpdateStates.SingleOrDefaultAsync(p => p.Id == StateId);
            if (state is null)
            {
                state = new AppUpdateState { Id = StateId, AppVersion = appVersion };
                database.AppUpdateStates.Add(state);
            }
            else if (state.AppVersion != appVersion)
            {
                state.AppVersion = appVersion;
                state.UpdateAvailable = false;
                state.NextCheckAt = null;
            }
            update?.Invoke(state);
            await database.SaveChangesAsync();
            return state;
        }
        finally { stateLock.Release(); }
    }

    public async Task<bool> CheckForUpdatesAsync(bool automatic = false)
    {
        Task<bool> check;
        await checkLock.WaitAsync();
        try
        {
            if (currentCheck is { IsCompleted: false })
            {
                check = currentCheck;
            }
            else
            {
                var state = await GetStateAsync();
                DateTime now = timeProvider.GetUtcNow().UtcDateTime;
                if (automatic && state.NextCheckAt is { } next && DateTime.SpecifyKind(next, DateTimeKind.Utc) > now)
                    return state.UpdateAvailable;

                // Persist the cooldown before contacting the store, including for interrupted checks.
                await UpdateStateAsync(p => p.NextCheckAt = now.AddDays(1));
                check = currentCheck = CheckForUpdatesCoreAsync();
            }
        }
        finally { checkLock.Release(); }
        return await check;
    }

    async Task<bool> CheckForUpdatesCoreAsync()
    {
        bool available = await CheckForUpdatesInternalAsync();
        await UpdateStateAsync(p =>
        {
            p.UpdateAvailable = available;
            p.NextCheckAt = timeProvider.GetUtcNow().UtcDateTime.AddDays(7);
        });
        NotifyUpdateStatusChanged();
        return available;
    }

    async Task ClearUpdateAvailableAsync()
    {
        await checkLock.WaitAsync();
        try
        {
            // Finish any older query before applying the store's newer "update unavailable" result.
            if (currentCheck is { IsCompleted: false })
            {
                try { await currentCheck; }
                catch (Exception) { }
            }
            await UpdateStateAsync(p => p.UpdateAvailable = false);
        }
        finally { checkLock.Release(); }
        NotifyUpdateStatusChanged();
    }

    void NotifyUpdateStatusChanged() => MainThread.BeginInvokeOnMainThread(() => UpdateStatusChanged?.Invoke(this, EventArgs.Empty));

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
            await ClearUpdateAvailableAsync();
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
