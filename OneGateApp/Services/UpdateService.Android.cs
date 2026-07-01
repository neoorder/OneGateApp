#if ANDROID

using Android.App;
using Android.Content.PM;
using Android.Gms.Extensions;
using AndroidX.Activity.Result;
using AndroidX.Activity.Result.Contract;
using CommunityToolkit.Maui.Alerts;
using Java.Interop;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Pages;
using NeoOrder.OneGate.Platforms.Android;
using NeoOrder.OneGate.Properties;
using Xamarin.Google.Android.Play.Core.AppUpdate;
using Xamarin.Google.Android.Play.Core.AppUpdate.Install;
using Xamarin.Google.Android.Play.Core.AppUpdate.Install.Model;

namespace NeoOrder.OneGate.Services;

partial class UpdateService : Java.Lang.Object, IActivityResultCallback, IInstallStateUpdatedListener
{
    ActivityResultLauncher? launcher;
    IAppUpdateManager? updateManager;

    internal void OnMainActivityCreated(MainActivity activity)
    {
        launcher = activity.RegisterForActivityResult(new ActivityResultContracts.StartIntentSenderForResult(), this);
        updateManager = AppUpdateManagerFactory.Create(activity);
        updateManager.RegisterListener(this);
    }

    void IActivityResultCallback.OnActivityResult(Java.Lang.Object? result)
    {
        var activityResult = result.JavaCast<AndroidX.Activity.Result.ActivityResult>()!;
        if (activityResult.ResultCode != (int)Result.Ok)
            IsUpdating = false;
    }

    void IInstallStateUpdatedListener.OnStateUpdate(InstallState? state)
    {
        switch (state?.InstallStatus())
        {
            case InstallStatus.Downloaded:
                MainThread.BeginInvokeOnMainThread(async () =>
                {
                    try
                    {
                        bool accepted = await Shell.Current.DisplayAlertAsync(Strings.UpdateApp, Strings.UpdateDownloadedRestartToInstall, Strings.Install, Strings.Cancel);
                        if (accepted)
                            await updateManager!.CompleteUpdate();
                        IsUpdating = false;
                    }
                    catch (Exception ex)
                    {
                        IsUpdating = false;
                        await Toast.Show(ex.Message);
                    }
                });
                break;
            case InstallStatus.Canceled:
            case InstallStatus.Failed:
            case InstallStatus.Installed:
                IsUpdating = false;
                break;
        }
    }

    private partial async Task<bool> CheckForUpdatesInternalAsync()
    {
        if (IsGooglePlay())
        {
            if (updateManager is null)
                throw new InvalidOperationException("Update manager is not initialized.");
            var info = await updateManager.GetAppUpdateInfo().AsAsync<AppUpdateInfo>();
            return info.UpdateAvailability() == UpdateAvailability.UpdateAvailable;
        }
        return await CheckForUpdatesFallbackAsync();
    }

    private partial async Task UpdateInternalAsync()
    {
        if (IsGooglePlay())
        {
            if (updateManager is null)
                throw new InvalidOperationException("Update manager is not initialized.");
            var info = await updateManager.GetAppUpdateInfo().AsAsync<AppUpdateInfo>();
            int availability = info.UpdateAvailability();
            if (availability != UpdateAvailability.UpdateAvailable && availability != UpdateAvailability.DeveloperTriggeredUpdateInProgress)
                throw new UpdateUnavailableException();
            bool flexible = info.IsUpdateTypeAllowed(AppUpdateType.Flexible);
            bool immediate = info.IsUpdateTypeAllowed(AppUpdateType.Immediate);
            if (flexible || immediate)
            {
                var options = AppUpdateOptions.DefaultOptions(flexible ? AppUpdateType.Flexible : AppUpdateType.Immediate);
                if (!updateManager.StartUpdateFlowForResult(info, launcher!, options))
                    throw new InvalidOperationException("Failed to start update flow.");
            }
            else
            {
                await Commands.LaunchUrl.ExecuteAsync($"https://play.google.com/store/apps/details?id={AppInfo.PackageName}");
                IsUpdating = false;
            }
        }
        else
        {
            await UpdateFallbackAsync();
        }
    }

    static bool IsGooglePlay()
    {
        PackageManager manager = Platform.AppContext.PackageManager!;
        var info = manager.GetInstallSourceInfo(Platform.AppContext.PackageName!);
        return info.InstallingPackageName == "com.android.vending";
    }
}
#endif
