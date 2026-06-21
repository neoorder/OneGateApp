using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using System.Collections.ObjectModel;
using TabBar = NeoOrder.OneGate.Controls.Views.TabBar;

namespace NeoOrder.OneGate.Pages;

public partial class GamingPage : ContentPage
{
    const double GameItemMinWidth = 520;
    const double HorizontalPageMargin = 40;
    const int MaxGameColumns = 3;

    readonly ApplicationDbContext dbContext;

    public LoadingService LoadingService { get; }
    public CachedCollection<DApp> DApps { get; }
    public List<int> GamesIdRecent { get; private set; } = [];
    public ObservableCollection<DApp> GamesRecent { get; private set { field = value; OnPropertyChanged(); } } = [];
    public bool HasRecentGames { get; private set { field = value; OnPropertyChanged(); } }
    public DApp[] Games { get; private set { field = value; OnPropertyChanged(); } } = [];
    public DApp[] GamesFiltered { get; private set { field = value; OnPropertyChanged(); } } = [];
    public string[] GameTypes { get; private set { field = value; OnPropertyChanged(); } } = [Strings.All];
    public bool HasGameTypeFilters { get; private set { field = value; OnPropertyChanged(); } }

    public GamingPage(IServiceProvider serviceProvider, ApplicationDbContext dbContext)
    {
        this.dbContext = dbContext;
        LoadingService = new(LoadSettingsAsync, LoadDAppsAsync);
        DApps = serviceProvider.GetServiceOrCreateInstance<CachedCollection<DApp>>();
        DApps.CollectionLoaded += OnDAppsLoaded;
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
    }

    async Task LoadDAppsAsync()
    {
        await DApps.LoadAsync("/api/dapps", TimeSpan.FromDays(1));
    }

    async Task LoadSettingsAsync()
    {
        GamesIdRecent = await dbContext.Settings.GetAsync<List<int>>("dapps/recent") ?? [];
    }

    void OnGameTypeChanged(object sender, EventArgs e)
    {
        ApplyGameTypeFilter(((TabBar)sender).SelectedTab);
    }

    void UpdateGamesItemsLayout(double pageWidth)
    {
        if (pageWidth <= 0) return;
        var contentWidth = Math.Max(0, pageWidth - HorizontalPageMargin);
        var span = Math.Clamp((int)(contentWidth / GameItemMinWidth), 1, MaxGameColumns);
        if (GamesItemsLayout.Span != span)
            GamesItemsLayout.Span = span;
    }

    void OnDAppsLoaded(object? sender, EventArgs e)
    {
        Games = DApps.Where(p => p.IsGamingApp).ToArray();
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
        ApplyGameTypeFilter(gameTypeTabBar.SelectedTab);
    }

    void OnDataLoaded(object? sender, EventArgs e)
    {
        LoadRecentGames();
    }

    void LoadRecentGames()
    {
        GamesRecent = new(GamesIdRecent
            .Select(id => Games.FirstOrDefault(p => p.Id == id))
            .OfType<DApp>());
        HasRecentGames = GamesRecent.Count > 0;
    }

    void ApplyGameTypeFilter(string? selectedGameType)
    {
        if (string.IsNullOrEmpty(selectedGameType) || selectedGameType == Strings.All)
        {
            GamesFiltered = Games;
            return;
        }

        GamesFiltered = Games
            .Where(p => string.Equals(p.GameTypeDisplayName, selectedGameType, StringComparison.CurrentCulture))
            .ToArray();
    }

    async void OnDetailsClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//gaming/details", new Dictionary<string, object>
        {
            ["dapp"] = ((Button)sender).CommandParameter
        });
    }
}
