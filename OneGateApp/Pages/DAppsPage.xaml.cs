using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using System.Collections.ObjectModel;
using TabBar = NeoOrder.OneGate.Controls.Views.TabBar;

namespace NeoOrder.OneGate.Pages;

public partial class DAppsPage : ContentPage
{
    readonly ApplicationDbContext dbContext;

    public LoadingService LoadingService { get; }
    public CachedCollection<DApp> DApps { get; }
    public string[] DAppCategories { get; private set { field = value; OnPropertyChanged(); } } = [Strings.All];
    public DApp[] DAppsFiltered { get; private set { field = value; OnPropertyChanged(); } } = [];
    public List<int> DAppsIdFavorite { get; private set; } = [];
    public ObservableCollection<DApp> DAppsFavorite { get; private set; } = [];
    public List<int> DAppsIdRecent { get; private set; } = [];
    public ObservableCollection<DApp> DAppsRecent { get; private set; } = [];
    public ObservableCollection<DApp>? DAppsFavoriteOrRecent { get; set { field = value; OnPropertyChanged(); } }

    public DAppsPage(IServiceProvider serviceProvider, ApplicationDbContext dbContext)
    {
        this.LoadingService = new(LoadSettingsAsync, LoadDAppsAsync);
        this.dbContext = dbContext;
        this.DApps = serviceProvider.GetServiceOrCreateInstance<CachedCollection<DApp>>();
        this.DApps.CollectionLoaded += OnDAppsLoaded;
        InitializeComponent();
#if WINDOWS
        // Disable the search handler on Windows
        // The search handler is not well supported on Windows and can cause issues with the layout
        Shell.SetSearchHandler(this, null);
#endif
        LoadingService.Loaded += OnDataLoaded;
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
        TabBar tabBar = (TabBar)sender;
        if (tabBar.SelectedTab == tabBar.Tabs![0])
            DAppsFiltered = DApps.ToArray();
        else
            DAppsFiltered = DApps.Where(p => p.Tags?.Select(t => Strings.ResourceManager.GetString(t) ?? t).Contains(tabBar.SelectedTab) == true).ToArray();
    }

    async void OnDetailsClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//dapps/details", new Dictionary<string, object>
        {
            ["dapp"] = ((Button)sender).CommandParameter
        });
    }

    async Task LoadSettingsAsync()
    {
        DAppsIdFavorite = await dbContext.Settings.GetAsync<List<int>>("dapps/favorite") ?? [];
        DAppsIdRecent = await dbContext.Settings.GetAsync<List<int>>("dapps/recent") ?? [];
    }

    async Task LoadDAppsAsync()
    {
        await DApps.LoadAsync("/api/dapps", TimeSpan.FromDays(1));
    }

    void OnDAppsLoaded(object? sender, EventArgs e)
    {
        DAppsFiltered = DApps.ToArray();
        DAppCategories = DApps
            .SelectMany(p => p.Tags ?? [])
            .Distinct()
            .Select(p => Strings.ResourceManager.GetString(p) ?? p)
            .Prepend(Strings.All)
            .ToArray();
    }

    void OnDataLoaded(object? sender, EventArgs e)
    {
        DAppsFavorite = new(DAppsIdFavorite.Select(id => DApps.FirstOrDefault(p => p.Id == id)).OfType<DApp>());
        DAppsRecent = new(DAppsIdRecent.Select(id => DApps.FirstOrDefault(p => p.Id == id)).OfType<DApp>());
        if (tabbarFavoriteOrRecent.SelectedTab == tabbarFavoriteOrRecent.Tabs![0])
            DAppsFavoriteOrRecent = DAppsRecent;
        else
            DAppsFavoriteOrRecent = DAppsFavorite;
    }
}
