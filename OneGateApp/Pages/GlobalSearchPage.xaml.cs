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

    bool hasLoaded;
    string query = "";
    IReadOnlyList<AssetInfo> assets = [];
    Contact[] contacts = [];

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
    public bool IsEmpty => !LoadingService.IsLoading && Results.Length == 0;

    public GlobalSearchPage(IServiceProvider serviceProvider, ApplicationDbContext dbContext, TokenManager tokenManager)
    {
        this.dbContext = dbContext;
        this.tokenManager = tokenManager;
        DApps = serviceProvider.GetServiceOrCreateInstance<CachedCollection<DApp>>();
        LoadingService = new(LoadAssetsAsync, LoadContactsAsync, LoadDAppsAsync);
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

    async Task LoadAssetsAsync()
    {
        assets = await tokenManager.LoadAssetsAsync();
    }

    async Task LoadContactsAsync()
    {
        contacts = await dbContext.Contacts.AsNoTracking().ToArrayAsync();
    }

    async Task LoadDAppsAsync()
    {
        await DApps.LoadAsync("/api/dapps", TimeSpan.FromDays(1));
    }

    void OnLoaded(object? sender, EventArgs e)
    {
        hasLoaded = true;
        UpdateResults();
    }

    void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        query = e.NewTextValue ?? "";
        UpdateResults();
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
        Results =
        [
            ..assets
                .Where(p => MatchesAsset(p, filter))
                .Take(MaxResultsPerGroup)
                .Select(GlobalSearchResult.FromAsset),
            ..contacts
                .Where(p => Contains(p.Label, filter) || Contains(p.Address, filter))
                .Take(MaxResultsPerGroup)
                .Select(GlobalSearchResult.FromContact),
            ..DApps
                .Where(p => p.IsRegularApp && MatchesDApp(p, filter))
                .Take(MaxResultsPerGroup)
                .Select(GlobalSearchResult.FromDApp)
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
                await Commands.LaunchDApp.ExecuteAsync(result.DApp!);
                break;
        }
    }

    static bool MatchesAsset(AssetInfo asset, string filter)
    {
        return Contains(asset.Token.Symbol, filter)
            || Contains(asset.Token.Name, filter)
            || Contains(asset.Token.Hash.ToString(), filter);
    }

    static bool MatchesDApp(DApp dapp, string filter)
    {
        return Contains(dapp.NameLocalizer.Localize(), filter)
            || Contains(dapp.DescriptionLocalizer?.Localize(), filter)
            || Contains(dapp.Url, filter)
            || dapp.Tags?.Any(p => Contains(DApp.LocalizeTag(p), filter)) == true;
    }

    static bool Contains(string? value, string filter)
    {
        return value?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true;
    }
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

    public static GlobalSearchResult FromDApp(DApp dapp)
    {
        return new()
        {
            Type = GlobalSearchResultType.DApp,
            Title = dapp.NameLocalizer.Localize() ?? dapp.Url,
            Subtitle = TryGetHost(dapp.Url) ?? dapp.Url,
            IconGlyph = "\ue792",
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
