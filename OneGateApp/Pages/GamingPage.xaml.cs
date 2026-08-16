using CommunityToolkit.Maui.Alerts;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using System.Collections.ObjectModel;
using TabBar = NeoOrder.OneGate.Controls.Views.TabBar;

namespace NeoOrder.OneGate.Pages;

public partial class GamingPage : ContentPage
{
    const string LayoutPreferenceKey = "gaming/layout";
    const string GalleryLayout = "gallery";
    const string ListLayout = "list";
    const double GameItemMinWidth = 520;
    const double GalleryItemMinWidth = 112;
    const double HorizontalPageMargin = 40;
    const int MaxGameColumns = 3;
    const int MaxGalleryColumns = 4;

    readonly ApplicationDbContext dbContext;
    bool allowRestrictedContent;
    bool developerModeEnabled;

    public LoadingService LoadingService { get; }
    public CachedCollection<DApp> DApps { get; }
    public List<int> GamesIdRecent { get; private set; } = [];
    public ObservableCollection<DApp> GamesRecent { get; private set { field = value; OnPropertyChanged(); } } = [];
    public bool HasRecentGames { get; private set { field = value; OnPropertyChanged(); } }
    public DApp[] Games { get; private set { field = value; OnPropertyChanged(); } } = [];
    public DApp[] GamesFiltered
    {
        get;
        private set
        {
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowGalleryLayout));
            OnPropertyChanged(nameof(ShowListLayout));
            OnPropertyChanged(nameof(ShowEmptyState));
        }
    } = [];
    public DApp[] GamesQuickPicks { get; private set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasQuickPicks)); } } = [];
    public bool HasQuickPicks => GamesQuickPicks.Length > 0;
    public DApp? SpotlightGame { get; private set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSpotlightGame)); } }
    public bool HasSpotlightGame => SpotlightGame is not null;
    public string[] GameTypes { get; private set { field = value; OnPropertyChanged(); } } = [Strings.All];
    public bool HasGameTypeFilters { get; private set { field = value; OnPropertyChanged(); } }
    public bool IsGalleryLayout
    {
        get;
        private set
        {
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsListLayout));
            OnPropertyChanged(nameof(LayoutToggleDescription));
            OnPropertyChanged(nameof(ShowGalleryLayout));
            OnPropertyChanged(nameof(ShowListLayout));
        }
    } = true;
    public bool IsListLayout => !IsGalleryLayout;
    public string LayoutToggleDescription => IsGalleryLayout
        ? Strings.GamingSwitchToListLayout
        : Strings.GamingSwitchToGalleryLayout;
    public bool ShowGalleryLayout => IsGalleryLayout && GamesFiltered.Length > 0;
    public bool ShowListLayout => IsListLayout && GamesFiltered.Length > 0;
    public bool ShowEmptyState => GamesFiltered.Length == 0;

    bool layoutPreferenceLoaded;

    public GamingPage(IServiceProvider serviceProvider, ApplicationDbContext dbContext)
    {
        this.LoadingService = new(LoadSettingsAsync, LoadDAppsAsync);
        this.dbContext = dbContext;
        this.DApps = serviceProvider.GetServiceOrCreateInstance<CachedCollection<DApp>>();
        InitializeComponent();
#if WINDOWS
        // Disable the search handler on Windows because it can cause layout issues there.
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
        else
            LoadRecentGames();
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        UpdateGamesItemsLayout(width);
        UpdateGalleryItemsLayout(width);
    }

    async Task LoadSettingsAsync()
    {
        allowRestrictedContent = await DAppCatalogPolicy.GetAllowRestrictedContentAsync(dbContext);
        developerModeEnabled = await DAppCatalogPolicy.GetDeveloperModeEnabledAsync(dbContext);
        GamesIdRecent = await dbContext.Settings.GetAsync<List<int>>("dapps/recent") ?? [];
        IsGalleryLayout = !string.Equals(await dbContext.Settings.GetAsync(LayoutPreferenceKey), ListLayout, StringComparison.OrdinalIgnoreCase);
        layoutPreferenceLoaded = true;
    }

    async Task LoadDAppsAsync()
    {
        await DApps.LoadAsync("/api/dapps", TimeSpan.FromDays(1));
    }

    void OnGameTypeChanged(object sender, EventArgs e)
    {
        ApplyGameTypeFilter((TabBar)sender);
    }

    void UpdateGamesItemsLayout(double pageWidth)
    {
        if (pageWidth <= 0) return;
        var contentWidth = Math.Max(0, pageWidth - HorizontalPageMargin);
        var span = Math.Clamp((int)(contentWidth / GameItemMinWidth), 1, MaxGameColumns);
        if (GamesItemsLayout.Span != span)
            GamesItemsLayout.Span = span;
    }

    void UpdateGalleryItemsLayout(double pageWidth)
    {
        if (pageWidth <= 0) return;
        var contentWidth = Math.Max(0, pageWidth - HorizontalPageMargin);
        var span = Math.Clamp((int)(contentWidth / GalleryItemMinWidth), 2, MaxGalleryColumns);
        if (GalleryItemsLayout.Span != span)
            GalleryItemsLayout.Span = span;
    }

    void OnDataLoaded(object? sender, EventArgs e)
    {
        Games = DApps
            .Where(p => p.IsGamingApp
                && DAppCatalogPolicy.IsVisible(p, allowRestrictedContent, developerModeEnabled))
            .ToArray();
        GameTypes = Games
            .Select(p => p.GameType)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(DApp.LocalizeGameType)
            .OfType<string>()
            .Order()
            .Prepend(Strings.All)
            .ToArray();
        HasGameTypeFilters = GameTypes.Length > 2;
        ApplyGameTypeFilter(gameTypeTabBar);
        LoadRecentGames();
    }

    void LoadRecentGames()
    {
        GamesRecent = new(GamesIdRecent
            .Select(id => Games.FirstOrDefault(p => p.Id == id))
            .OfType<DApp>());
        HasRecentGames = GamesRecent.Count > 0;
    }

    void UpdateGallerySections()
    {
        SpotlightGame = GamesFiltered
            .FirstOrDefault(p => p.Previews?.Any(url => !string.IsNullOrWhiteSpace(url)) == true)
            ?? GamesFiltered.FirstOrDefault();
        GamesQuickPicks = GamesFiltered
            .Where(p => !ReferenceEquals(p, SpotlightGame))
            .Take(6)
            .ToArray();
    }

    void ApplyGameTypeFilter(TabBar tabBar)
    {
        if (tabBar.Tabs is not { Count: > 0 } tabs)
        {
            GamesFiltered = Games;
            UpdateGallerySections();
            return;
        }

        if (tabBar.SelectedTab is null)
        {
            tabBar.SelectedTab = tabs[0];
            return;
        }

        if (tabBar.SelectedTab == tabs[0])
        {
            GamesFiltered = Games;
            UpdateGallerySections();
            return;
        }

        GamesFiltered = Games
            .Where(p => string.Equals(p.GameTypeDisplayName, tabBar.SelectedTab, StringComparison.CurrentCulture))
            .ToArray();
        UpdateGallerySections();
    }

    async void OnLayoutToggleClicked(object sender, EventArgs e)
    {
        if (!layoutPreferenceLoaded)
            return;

        bool previousLayout = IsGalleryLayout;
        IsGalleryLayout = !previousLayout;
        try
        {
            await dbContext.Settings.PutAsync(LayoutPreferenceKey, IsGalleryLayout ? GalleryLayout : ListLayout);
        }
        catch (Exception ex)
        {
            IsGalleryLayout = previousLayout;
            await Toast.Show(ex.Message);
        }
    }

    async void OnDetailsClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//gaming/details", new Dictionary<string, object>
        {
            ["dapp"] = ((Button)sender).CommandParameter
        });
    }
}
