using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using System.Globalization;

namespace NeoOrder.OneGate.Pages;

public partial class SettingsPage : ContentPage
{
    readonly ApplicationDbContext dbContext;
    readonly TokenManager tokenManager;
    readonly ActivityLogService activityLogService;

    public LoadingService LoadingService { get; set { field = value; OnPropertyChanged(); } }
    public SettingEntryGroup[]? SettingEntries { get; set { field = value; OnPropertyChanged(); } }

    public SettingsPage(ApplicationDbContext dbContext, TokenManager tokenManager, ActivityLogService activityLogService)
    {
        this.LoadingService = new(LoadSettingsAsync);
        this.dbContext = dbContext;
        this.tokenManager = tokenManager;
        this.activityLogService = activityLogService;
        InitializeComponent();
        LoadingService.BeginLoad();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (this.ShouldRefresh())
            LoadingService.BeginLoad();
    }

    async Task LoadSettingsAsync()
    {
        SettingEntries = await GetAllSettingEntries()
            .GroupBy(p => p.GroupName, p => p.Entry)
            .Select(p => SettingEntryGroup.Create(p.Key, p))
            .ToArrayAsync();
    }

    async IAsyncEnumerable<(string GroupName, SettingEntry Entry)> GetAllSettingEntries()
    {
        yield return (Strings.General, new SettingEntry(Strings.Language)
        {
            CurrentValue = await GetCurrentLanguageNameAsync(),
            Command = Commands.GotoPage,
            CommandParameter = "//home/settings/language"
        });
        yield return (Strings.General, new SettingEntry(Strings.NewsCategories)
        {
            Command = Commands.GotoPage,
            CommandParameter = "//home/settings/news"
        });
        yield return (Strings.General, new SettingEntry(Strings.ContentSettings)
        {
            CurrentValue = await DAppContentPolicy.GetAllowRestrictedContentAsync(dbContext) ? Strings.Enabled : Strings.Disabled,
            Command = Commands.GotoPage,
            CommandParameter = "//home/settings/content"
        });
        yield return (Strings.General, new SettingEntry(Strings.HiddenAssets)
        {
            CurrentValue = (await tokenManager.GetHiddenTokenCountAsync()).ToString(),
            Command = Commands.GotoPage,
            CommandParameter = "//home/settings/assets/hidden"
        });
        yield return (Strings.General, new SettingEntry(Strings.ActivityCenter)
        {
            CurrentValue = await GetActivitySummaryTextAsync(),
            Command = Commands.GotoPage,
            CommandParameter = "//home/settings/activity"
        });
        yield return (Strings.Security, new SettingEntry(Strings.SecurityCenter)
        {
            CurrentValue = Strings.Review,
            Command = Commands.GotoPage,
            CommandParameter = "//home/settings/security"
        });
        yield return (Strings.Security, new SettingEntry(Strings.WalletSettings)
        {
            Command = Commands.GotoPage,
            CommandParameter = "//home/settings/wallet/details"
        });
        if (await DataProtectionService.CheckAvailabilityAsync())
        {
            bool enabled = await dbContext.Settings.ExistsAsync("biometric/credential");
            yield return (Strings.Security, new SettingEntry(Strings.BiometricAuthentication)
            {
                CurrentValue = enabled ? Strings.Enabled : null,
                Command = Commands.GotoPage,
                CommandParameter = enabled ? "//home/settings/biometric/disable" : "//home/settings/biometric/create"
            });
        }
        yield return (Strings.Others, new SettingEntry(Strings.ContactUs)
        {
            CurrentValue = SharedOptions.ContactEmail,
            Command = Commands.LaunchUrl,
            CommandParameter = $"mailto:{SharedOptions.ContactEmail}"
        });
        yield return (Strings.Others, new SettingEntry(Strings.DeveloperTools)
        {
            Command = Commands.GotoPage,
            CommandParameter = "//home/settings/developer"
        });
        yield return (Strings.Others, new SettingEntry(Strings.About)
        {
            Command = Commands.GotoPage,
            CommandParameter = "//home/settings/about"
        });
    }

    async Task<string> GetCurrentLanguageNameAsync()
    {
        string? lang = await dbContext.Settings.GetAsync("preference/language");
        if (string.IsNullOrEmpty(lang)) return Strings.SystemLanguage;
        CultureInfo culture = new(lang);
        return culture.NativeName;
    }

    async Task<string> GetActivitySummaryTextAsync()
    {
        int count = (await activityLogService.GetRecentAsync()).Count;
        return count == 0 ? Strings.NoRecentActivity : string.Format(Strings.ActivityRecordCount, count);
    }
}
