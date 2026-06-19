using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Extensions;
using Microsoft.EntityFrameworkCore;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Controls.Popups;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using System.Globalization;

namespace NeoOrder.OneGate.Pages;

public partial class LanguagePage : ContentPage
{
    public static readonly string[] SupportedLanguages = ["", "de", "en", "es", "fr", "id", "it", "ja", "ko", "nl", "pt-BR", "ru", "tr", "vi", "zh-Hans", "zh-Hant"];

    readonly IServiceProvider serviceProvider;
    readonly ApplicationDbContext dbContext;
    readonly IDbContextFactory<CacheDbContext> cacheDbContextFactory;
    readonly HttpClient httpClient;

    public LoadingService LoadingService { get; set { field = value; OnPropertyChanged(); } }
    public string? CurrentLanguage { get; set { field = value; OnPropertyChanged(); } }

    public LanguagePage(IServiceProvider serviceProvider, ApplicationDbContext dbContext, IDbContextFactory<CacheDbContext> cacheDbContextFactory, HttpClient httpClient)
    {
        this.LoadingService = new(LoadSettingsAsync);
        this.serviceProvider = serviceProvider;
        this.dbContext = dbContext;
        this.cacheDbContextFactory = cacheDbContextFactory;
        this.httpClient = httpClient;
        InitializeComponent();
        LoadingService.BeginLoad();
    }

    async Task LoadSettingsAsync()
    {
        CurrentLanguage = await dbContext.Settings.GetAsync("preference/language") ?? "";
    }

    async void OnLanguageTapped(object sender, TappedEventArgs e)
    {
        string lang = (string)e.Parameter!;
        if (lang == CurrentLanguage)
        {
            await Shell.Current.GoToAsync("..");
            return;
        }
        var popup = serviceProvider.GetServiceOrCreateInstance<ConfirmationPopup>();
        popup.Title = Strings.ChangeLanguage;
        popup.Message = Strings.ChangeLanguageConfirmation;
        popup.AcceptText = Strings.Continue;
        var result = await this.ShowPopupAsync<bool>(popup);
        if (!result.Result) return;
        await dbContext.Settings.PutAsync("preference/language", lang);
        await cacheDbContextFactory.DeleteSettingIfExistsAsync("caching/last_update/news");
        CultureInfo culture = string.IsNullOrEmpty(lang)
            ? CultureInfo.InstalledUICulture
            : new CultureInfo(lang);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        httpClient.DefaultRequestHeaders.AcceptLanguage.Clear();
        httpClient.DefaultRequestHeaders.AcceptLanguage.ParseAdd(culture.Name);
        await Toast.Show(Strings.LanguageChanged);
#if ANDROID
        Platform.CurrentActivity!.Recreate();
#else
        Window.Page = serviceProvider.GetServiceOrCreateInstance<AppShell>();
#endif
    }
}
