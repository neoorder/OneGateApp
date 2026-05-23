using CommunityToolkit.Maui.Alerts;
using Microsoft.EntityFrameworkCore;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;

namespace NeoOrder.OneGate.Pages;

public partial class AboutPage : ContentPage
{
    readonly ApplicationDbContext dbContext;
    readonly UpdateService updateService;

    public LoadingService LoadingService { get; set { field = value; OnPropertyChanged(); } }
    public IAppInfo AppInfo { get; } = Microsoft.Maui.ApplicationModel.AppInfo.Current;
    public SettingEntry[]? SettingEntries { get; set { field = value; OnPropertyChanged(); } }

    public AboutPage(ApplicationDbContext dbContext, UpdateService updateService)
    {
        this.LoadingService = new(LoadSettingsAsync);
        this.dbContext = dbContext;
        this.updateService = updateService;
        InitializeComponent();
        LoadingService.BeginLoad();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        updateService.UpdateAvailable += OnUpdateAvailable;
    }

    protected override void OnDisappearing()
    {
        updateService.UpdateAvailable -= OnUpdateAvailable;
        base.OnDisappearing();
    }

    void OnUpdateAvailable(object? sender, EventArgs e)
    {
        LoadingService.BeginLoad();
    }

    async void OnKonamiCodeEntered(object sender, EventArgs e)
    {
        await dbContext.Settings.PutAsync("preference/developer_mode_enabled", true);
        GlobalStates.Invalidate<SettingsPage>();
        await Toast.Show(Strings.DeveloperModeEnabled);
    }

    async Task LoadSettingsAsync()
    {
        SettingEntries = await GetAllSettingEntries().ToArrayAsync();
    }

    async IAsyncEnumerable<SettingEntry> GetAllSettingEntries()
    {
        yield return new SettingEntry(Strings.Website)
        {
            CurrentValue = $"https://{SharedOptions.OneGateDomain}",
            Command = Commands.LaunchUrl,
            CommandParameter = $"https://{SharedOptions.OneGateDomain}"
        };
        yield return new SettingEntry("X")
        {
            CurrentValue = "@OneGateSpace",
            Command = Commands.LaunchUrl,
            CommandParameter = "https://x.com/OneGateSpace"
        };
        if (await dbContext.Settings.ExistsAsync("system/updates"))
            yield return new SettingEntry(Strings.UpdateNow)
            {
                CurrentValue = Strings.NewVersionAvailable,
                Command = Commands.UpdateApp
            };
        else
            yield return new SettingEntry(Strings.CheckForUpdates)
            {
                Command = Commands.CheckForUpdates
            };
        yield return new SettingEntry(Strings.TermsOfService)
        {
            Command = Commands.OpenUrl,
            CommandParameter = $"https://{SharedOptions.OneGateDomain}/terms.html"
        };
        yield return new SettingEntry(Strings.PrivacyPolicy)
        {
            Command = Commands.OpenUrl,
            CommandParameter = $"https://{SharedOptions.OneGateDomain}/privacy.html"
        };
    }
}
