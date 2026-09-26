using Microsoft.EntityFrameworkCore;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using Contact = NeoOrder.OneGate.Data.Contact;

namespace NeoOrder.OneGate.Pages;

public partial class GlobalSearchPage : ContentPage
{
    const int MaxResultsPerGroup = 5;

    readonly ApplicationDbContext dbContext;
    readonly TokenManager tokenManager;
    readonly object searchErrorLock = new();

    bool hasLoaded;
    int searchVersion;
    string query = "";
    IReadOnlyList<GlobalSearchIndex<AssetInfo>> assetIndex = [];
    IReadOnlyList<GlobalSearchIndex<Contact>> contactIndex = [];
    IReadOnlyList<GlobalSearchIndex<DApp>> dappIndex = [];

    public LoadingService LoadingService { get; }
    public CachedCollection<DApp> DApps { get; }
    public GlobalSearchResult[] Results
    {
        get;
        private set
        {
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasResults));
            OnPropertyChanged(nameof(IsEmpty));
        }
    } = [];
    public bool HasResults => Results.Length > 0;
    public bool HasQuery => !string.IsNullOrWhiteSpace(query);
    public bool IsEmpty => HasQuery && !LoadingService.IsLoading && !HasSearchErrors && Results.Length == 0;
    public bool HasSearchErrors => !string.IsNullOrEmpty(SearchErrorText);
    public string SearchErrorText
    {
        get;
        private set
        {
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSearchErrors));
            OnPropertyChanged(nameof(IsEmpty));
        }
    } = "";

    public GlobalSearchPage(IServiceProvider serviceProvider, ApplicationDbContext dbContext, TokenManager tokenManager)
    {
        this.dbContext = dbContext;
        this.tokenManager = tokenManager;
        DApps = serviceProvider.GetServiceOrCreateInstance<CachedCollection<DApp>>();
        LoadingService = new(LoadSearchDataAsync);
        LoadingService.Loaded += OnLoaded;
        LoadingService.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LoadingService.IsLoading))
                OnPropertyChanged(nameof(IsEmpty));
        };
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (!hasLoaded || this.ShouldRefresh())
            LoadingService.BeginLoad();
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(250), () => searchBar.Focus());
    }

    async Task LoadSearchDataAsync()
    {
        SearchErrorText = "";
        // Settings are part of the discovery boundary. A prior developer-mode
        // index cannot remain usable while settings are unavailable or pending.
        dappIndex = [];
        UpdateResults();
        await LoadSearchGroupAsync(async () =>
        {
            contactIndex = (await dbContext.Contacts.AsNoTracking().ToArrayAsync())
                .Select(p => new GlobalSearchIndex<Contact>(p, p.Label, p.Address))
                .ToArray();
        }, Strings.AddressBook);

        // Read settings before starting network work: TokenManager also uses the
        // application DbContext, which must not run concurrent database queries.
        List<int> recentDAppIds = [];
        bool developerModeEnabled = false;
        bool catalogSettingsLoaded = false;
        await LoadSearchGroupAsync(async () =>
        {
            recentDAppIds = await dbContext.Settings.GetAsync<List<int>>("dapps/recent") ?? [];
            developerModeEnabled = await DAppCatalogPolicy.GetDeveloperModeEnabledAsync(dbContext);
            catalogSettingsLoaded = true;
        }, Strings.Apps);

        await Task.WhenAll(
            LoadSearchGroupAsync(LoadSearchAssetsAsync, Strings.Asset),
            catalogSettingsLoaded
                ? LoadSearchGroupAsync(() => LoadSearchDAppsAsync(recentDAppIds, developerModeEnabled), Strings.Apps)
                : Task.CompletedTask);
    }

    async Task LoadSearchGroupAsync(Func<Task> load, string group)
    {
        try
        {
            await load();
        }
        catch (Exception)
        {
            lock (searchErrorLock)
            {
                string message = $"{group}: {Strings.Unavailable}";
                SearchErrorText = string.IsNullOrEmpty(SearchErrorText)
                    ? message
                    : $"{SearchErrorText}{Environment.NewLine}{message}";
            }
        }
        finally
        {
            UpdateResults();
        }
    }

    async Task LoadSearchAssetsAsync()
    {
        IReadOnlyList<AssetInfo> assets = await tokenManager.LoadAssetsAsync();
        assetIndex = assets
            .Select(p => new GlobalSearchIndex<AssetInfo>(p, p.Token.Symbol, p.Token.Name, p.Token.Hash.ToString()))
            .ToArray();
    }

    async Task LoadSearchDAppsAsync(List<int> recentDAppIds, bool developerModeEnabled)
    {
        Dictionary<int, int> recentDAppRanks = [];
        for (int i = 0; i < recentDAppIds.Count; i++)
            recentDAppRanks.TryAdd(recentDAppIds[i], i);
        try
        {
            await DApps.LoadAsync("/api/dapps", TimeSpan.FromDays(1));
        }
        finally
        {
            // CachedCollection has already loaded disk data even when refreshing
            // the network fails. Keep those usable results alongside the error.
            dappIndex = DApps
                .Where(p => p.IsRegularApp && DAppCatalogPolicy.IsDiscoverable(p, developerModeEnabled))
                .Select(p => new GlobalSearchIndex<DApp>(
                p,
                    recentDAppRanks.GetValueOrDefault(p.Id, -1),
                    p.NameLocalizer.Localize(),
                    p.DescriptionLocalizer?.Localize(),
                    p.Url,
                    p.Tags is null ? null : string.Join(' ', p.Tags.Select(DApp.LocalizeTag))))
                .ToArray();
        }
    }

    void OnLoaded(object? sender, EventArgs e)
    {
        hasLoaded = true;
        UpdateResults();
    }

    void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        query = e.NewTextValue ?? "";
        OnPropertyChanged(nameof(HasQuery));
        OnPropertyChanged(nameof(IsEmpty));
        if (string.IsNullOrWhiteSpace(query))
        {
            searchVersion++;
            Results = [];
            return;
        }
        int version = ++searchVersion;
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(200), () =>
        {
            if (version == searchVersion)
                UpdateResults();
        });
    }

    async void OnSearchButtonPressed(object sender, EventArgs e)
    {
        if (Results.Length == 1)
            await OpenResultAsync(Results[0]);
    }

    async void OnResultTapped(object sender, TappedEventArgs e)
    {
        await OpenResultAsync((GlobalSearchResult)e.Parameter!);
    }

    void UpdateResults()
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            Results = [];
            return;
        }

        string filter = query.Trim();
        GlobalSearchIndex<DApp>[] matchingDApps = dappIndex
            .Where(p => p.Matches(filter))
            .OrderBy(p => p.RecentRank < 0 ? int.MaxValue : p.RecentRank)
            .ThenBy(p => p.Item.NameLocalizer.Localize(), StringComparer.CurrentCultureIgnoreCase)
            .Take(MaxResultsPerGroup)
            .ToArray();
        Results =
        [
            ..matchingDApps
                .Where(p => p.IsRecent)
                .Select(p => GlobalSearchResult.FromDApp(p.Item, p.IsRecent)),
            ..assetIndex
                .Where(p => p.Matches(filter))
                .Take(MaxResultsPerGroup)
                .Select(p => GlobalSearchResult.FromAsset(p.Item)),
            ..contactIndex
                .Where(p => p.Matches(filter))
                .Take(MaxResultsPerGroup)
                .Select(p => GlobalSearchResult.FromContact(p.Item)),
            ..matchingDApps
                .Where(p => !p.IsRecent)
                .Select(p => GlobalSearchResult.FromDApp(p.Item, p.IsRecent))
        ];
    }

    async Task OpenResultAsync(GlobalSearchResult result)
    {
        searchBar.Unfocus();
        switch (result.Type)
        {
            case GlobalSearchResultType.Asset:
                await Shell.Current.GoToAsync("//wallet/asset/details", new Dictionary<string, object>
                {
                    ["asset"] = result.Asset!
                });
                break;
            case GlobalSearchResultType.Contact:
                await Shell.Current.GoToAsync("//home/contacts/edit", new Dictionary<string, object>
                {
                    ["contact"] = result.Contact!
                });
                break;
            case GlobalSearchResultType.DApp:
                if (!dappIndex.Any(p => ReferenceEquals(p.Item, result.DApp))) return;
                await Commands.LaunchDApp.ExecuteAsync(result.DApp!);
                break;
        }
    }

    internal static bool Contains(string? value, string filter)
    {
        return value?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true;
    }
}

public sealed class GlobalSearchIndex<T>
{
    readonly string[] values;

    public GlobalSearchIndex(T item, params string?[] values)
        : this(item, -1, values)
    {
    }

    public GlobalSearchIndex(T item, int recentRank, params string?[] values)
    {
        Item = item;
        RecentRank = recentRank;
        this.values = values
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!)
            .ToArray();
    }

    public T Item { get; }
    public int RecentRank { get; }
    public bool IsRecent => RecentRank >= 0;
    public bool Matches(string filter) => values.Any(p => GlobalSearchPage.Contains(p, filter));
}

public enum GlobalSearchResultType
{
    Asset,
    Contact,
    DApp
}

public sealed class GlobalSearchResult
{
    public required GlobalSearchResultType Type { get; init; }
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public required string IconGlyph { get; init; }
    public string? IconImageSource { get; init; }
    public bool HasIconImage => !string.IsNullOrWhiteSpace(IconImageSource);
    public bool HasIconGlyph => !HasIconImage;
    public bool IsRecentDApp { get; init; }
    public AssetInfo? Asset { get; init; }
    public Contact? Contact { get; init; }
    public DApp? DApp { get; init; }
    public string TypeText => Type switch
    {
        GlobalSearchResultType.Asset => Strings.Asset,
        GlobalSearchResultType.Contact => Strings.AddressBook,
        GlobalSearchResultType.DApp => Strings.Apps,
        _ => ""
    };

    public static GlobalSearchResult FromAsset(AssetInfo asset)
    {
        return new()
        {
            Type = GlobalSearchResultType.Asset,
            Title = asset.Token.Symbol,
            Subtitle = $"{asset.Token.Name} - {asset.DisplayBalance}",
            IconGlyph = "\ue852",
            IconImageSource = asset.Token.Icon ?? "tokens_icon_default.png",
            Asset = asset
        };
    }

    public static GlobalSearchResult FromContact(Contact contact)
    {
        return new()
        {
            Type = GlobalSearchResultType.Contact,
            Title = contact.Label,
            Subtitle = contact.Address,
            IconGlyph = "\ue60d",
            Contact = contact
        };
    }

    public static GlobalSearchResult FromDApp(DApp dapp, bool isRecent)
    {
        return new()
        {
            Type = GlobalSearchResultType.DApp,
            Title = dapp.NameLocalizer.Localize() ?? dapp.Url,
            Subtitle = TryGetHost(dapp.Url) ?? dapp.Url,
            IconGlyph = "\ue792",
            IconImageSource = dapp.IconUrl,
            IsRecentDApp = isRecent,
            DApp = dapp
        };
    }

    static string? TryGetHost(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            return uri.Host;
        return null;
    }
}
