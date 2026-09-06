using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;
using TabBar = NeoOrder.OneGate.Controls.Views.TabBar;

namespace NeoOrder.OneGate.Pages;

public partial class DAppsPage : ContentPage
{
    readonly ApplicationDbContext dbContext;
    bool allowRestrictedContent;
    bool developerModeEnabled;

    public LoadingService LoadingService { get; }
    public CachedCollection<DApp> DApps { get; }
    public DApp[] DAppsRegular { get; private set { field = value; OnPropertyChanged(); } } = [];
    public string[] DAppCategories { get; private set { field = value; OnPropertyChanged(); } } = [Strings.All];
    public DApp[] DAppsFiltered { get; private set { field = value; OnPropertyChanged(); } } = [];
    public List<int> DAppsIdFavorite { get; private set; } = [];
    public ObservableCollection<DApp> DAppsFavorite { get; private set; } = [];
    public List<int> DAppsIdRecent { get; private set; } = [];
    public ObservableCollection<DApp> DAppsRecent { get; private set; } = [];
    public ObservableCollection<DApp>? DAppsFavoriteOrRecent { get; set { field = value; OnPropertyChanged(); } }
    public bool HasFavoriteOrRecent { get; private set { field = value; OnPropertyChanged(); } }

    public DAppsPage(IServiceProvider serviceProvider, ApplicationDbContext dbContext)
    {
        this.LoadingService = new(LoadCatalogAsync);
        this.dbContext = dbContext;
        this.DApps = serviceProvider.GetServiceOrCreateInstance<CachedCollection<DApp>>();
        InitializeComponent();
#if WINDOWS
        // Disable the search handler on Windows
        // The search handler is not well supported on Windows and can cause issues with the layout
        Shell.SetSearchHandler(this, null);
#endif
        LoadingService.Loaded += OnDataLoaded;
        DApps.CollectionLoaded += OnDataLoaded;
        LoadingService.BeginLoad();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (this.ShouldRefresh())
            LoadingService.BeginLoad();
    }

    void FavoriteOrRecent_SelectedTabChanged(object sender, EventArgs e)
    {
        TabBar tabBar = (TabBar)sender;
        if (tabBar.SelectedTab == tabBar.Tabs![0])
            DAppsFavoriteOrRecent = DAppsRecent;
        else
            DAppsFavoriteOrRecent = DAppsFavorite;
    }

    void OnCategoryChanged(object sender, EventArgs e)
    {
        ApplyCategoryFilter((TabBar)sender);
    }

    void ApplyCategoryFilter(TabBar tabBar)
    {
        if (tabBar.Tabs is not { Count: > 0 } tabs)
        {
            DAppsFiltered = DAppsRegular;
            return;
        }

        if (tabBar.SelectedTab is null)
        {
            tabBar.SelectedTab = tabs[0];
            return;
        }

        if (tabBar.SelectedTab == tabs[0])
            DAppsFiltered = DAppsRegular;
        else
            DAppsFiltered = DAppsRegular.Where(p => p.Tags?.Select(DApp.LocalizeTag).Contains(tabBar.SelectedTab) == true).ToArray();
    }

    async void OnDetailsClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//dapps/details", new Dictionary<string, object>
        {
            ["dapp"] = ((Button)sender).CommandParameter
        });
    }

    async Task LoadCatalogAsync()
    {
        // Do not leave a previous permissive policy visible while settings are
        // loading. A failed read remains restricted rather than reusing old flags.
        allowRestrictedContent = false;
        developerModeEnabled = false;
        OnDataLoaded(this, EventArgs.Empty);
        Exception? settingsFailure = null;
        try { await LoadSettingsAsync(); }
        catch (Exception ex) { settingsFailure = ex; }
        finally
        {
            // An already loaded/shared collection will not send another disk-load
            // event, and an offline refresh will not send a success event either.
            OnDataLoaded(this, EventArgs.Empty);
        }
        await LoadDAppsAsync();
        if (settingsFailure is not null)
            ExceptionDispatchInfo.Capture(settingsFailure).Throw();
    }

    async Task LoadSettingsAsync()
    {
        bool allowRestrictedContent = await DAppCatalogPolicy.GetAllowRestrictedContentAsync(dbContext);
        bool developerModeEnabled = await DAppCatalogPolicy.GetDeveloperModeEnabledAsync(dbContext);
        List<int> favorite = await dbContext.Settings.GetAsync<List<int>>("dapps/favorite") ?? [];
        List<int> recent = await dbContext.Settings.GetAsync<List<int>>("dapps/recent") ?? [];
        this.allowRestrictedContent = allowRestrictedContent;
        this.developerModeEnabled = developerModeEnabled;
        DAppsIdFavorite = favorite;
        DAppsIdRecent = recent;
    }

    async Task LoadDAppsAsync()
    {
        await DApps.LoadAsync("/api/dapps", TimeSpan.FromDays(1));
    }

    void OnDataLoaded(object? sender, EventArgs e)
    {
        DAppsRegular = DApps
            .Where(p => p.IsRegularApp
                && DAppCatalogPolicy.IsVisible(p, allowRestrictedContent, developerModeEnabled))
            .ToArray();
        DAppsFiltered = DAppsRegular;
        DAppCategories = DAppsRegular
            .SelectMany(p => p.Tags ?? [])
            .Distinct()
            .Select(DApp.LocalizeTag)
            .Prepend(Strings.All)
            .ToArray();
        ApplyCategoryFilter(tabbarCategory);

        DAppsFavorite = new(DAppsIdFavorite.Select(id => DAppsRegular.FirstOrDefault(p => p.Id == id)).OfType<DApp>());
        DAppsRecent = new(DAppsIdRecent.Select(id => DAppsRegular.FirstOrDefault(p => p.Id == id)).OfType<DApp>());
        HasFavoriteOrRecent = DAppsFavorite.Count > 0 || DAppsRecent.Count > 0;
        if (tabbarFavoriteOrRecent.SelectedTab == tabbarFavoriteOrRecent.Tabs![0])
            DAppsFavoriteOrRecent = DAppsRecent;
        else
            DAppsFavoriteOrRecent = DAppsFavorite;
    }
}
