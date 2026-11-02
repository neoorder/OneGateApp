using Microsoft.EntityFrameworkCore;
using Neo.Wallets;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using NeoOrder.OneGate.Services.Messaging;
using Contact = NeoOrder.OneGate.Data.Contact;

namespace NeoOrder.OneGate.Pages;

public partial class GlobalSearchPage : ContentPage
{
    const int MaxResultsPerGroup = 5;

    readonly ApplicationDbContext dbContext;
    readonly TokenManager tokenManager;

    bool hasLoaded;
    bool refreshAll = true;
    bool reloadPending;
    bool openingResult;
    int searchVersion;
    string query = "";
    IReadOnlyList<GlobalSearchIndex<AssetInfo>> assetIndex = [];
    IReadOnlyList<GlobalSearchIndex<GlobalSearchResult>> contactIndex = [];
    IReadOnlyList<GlobalSearchIndex<DApp>> dappIndex = [];

    public LoadingService LoadingService { get; }
    public CachedCollection<DApp> DApps { get; }
    public string? ErrorText { get; private set { field = value; OnPropertyChanged(); } }
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
    public bool ShowLoading => LoadingService.IsLoading && !hasLoaded;
    public bool IsEmpty => !string.IsNullOrWhiteSpace(query) && !ShowLoading && Results.Length == 0;

    public GlobalSearchPage(IServiceProvider serviceProvider, ApplicationDbContext dbContext, TokenManager tokenManager,
        MessagingService messagingService, IWalletProvider walletProvider)
    {
        this.dbContext = dbContext;
        this.tokenManager = tokenManager;
        this.messagingService = messagingService;
        this.walletProvider = walletProvider;
        DApps = serviceProvider.GetServiceOrCreateInstance<CachedCollection<DApp>>();
        LoadingService = new(LoadSearchDataAsync);
        LoadingService.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LoadingService.IsLoading))
            {
                OnPropertyChanged(nameof(ShowLoading));
                OnPropertyChanged(nameof(IsEmpty));
            }
        };
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        StartChatContactSearch();
        refreshAll |= this.ShouldRefresh();
        reloadPending = true;
        LoadingService.BeginLoad();
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(250), () =>
        {
            if (ReferenceEquals(Shell.Current.CurrentPage, this) && !openingResult) searchBar.Focus();
        });
    }

    async Task LoadSearchDataAsync()
    {
        do
        {
            reloadPending = false;
            await ReadSearchDataAsync();
            hasLoaded = true;
            OnPropertyChanged(nameof(ShowLoading));
            UpdateResults();
        } while (reloadPending && ReferenceEquals(Shell.Current.CurrentPage, this));
    }

    async Task ReadSearchDataAsync()
    {
        bool loadAll = refreshAll || !hasLoaded;
        refreshAll = false;
        try
        {
            if (loadAll)
            {
                IReadOnlyList<AssetInfo> assets = await tokenManager.LoadAssetsAsync();
                assetIndex = assets
                    .Select(p => new GlobalSearchIndex<AssetInfo>(p, p.Token.Symbol, p.Token.Name, p.Token.Hash.ToString()))
                    .ToArray();
            }
            // Returning from contact details refreshes local names without blocking existing results.
            savedSearchContacts = await dbContext.Contacts.AsNoTracking().ToArrayAsync();
            RebuildContactSearchIndex();
            if (!loadAll) return;
            UpdateResults();

            List<int> recentDAppIds = await dbContext.Settings.GetAsync<List<int>>("dapps/recent") ?? [];
            bool developerModeEnabled = await DAppCatalogPolicy.GetDeveloperModeEnabledAsync(dbContext);
            await DApps.LoadAsync("/api/dapps", TimeSpan.FromDays(1));
            dappIndex = DApps
                .Where(p => p.IsRegularApp && DAppCatalogPolicy.IsDiscoverable(p, developerModeEnabled))
                .Select(p => new GlobalSearchIndex<DApp>(
                    p,
                    recentDAppIds.IndexOf(p.Id),
                    p.NameLocalizer.Localize(),
                    p.DescriptionLocalizer?.Localize(),
                    p.Url,
                    p.Tags is null ? null : string.Join(' ', p.Tags.Select(DApp.LocalizeTag))))
                .ToArray();
        }
        catch
        {
            refreshAll |= loadAll;
            throw;
        }
    }

    void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        query = e.NewTextValue ?? "";
        ErrorText = null;
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
        if (openingResult) return;
        UpdateResults();
        if (Results.Length == 1)
            await OpenResultAsync(Results[0]);
    }

    async void OnResultTapped(object sender, TappedEventArgs e)
    {
        if (e.Parameter is GlobalSearchResult result) await OpenResultAsync(result);
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
                .Select(p => p.Item),
            ..matchingDApps
                .Where(p => !p.IsRecent)
                .Select(p => GlobalSearchResult.FromDApp(p.Item, p.IsRecent))
        ];
    }

    async Task OpenResultAsync(GlobalSearchResult result)
    {
        if (openingResult || !ReferenceEquals(Shell.Current.CurrentPage, this) || !Results.Contains(result)) return;
        openingResult = true;
        int version = searchVersion;
        ErrorText = null;
        try
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
                case GlobalSearchResultType.Contact when result.ChatAccountId is not null:
                    if (!CanOpenChatSearchResult(result)) return;
                    var status = chatSearchContacts[result.ChatAddress!];
                    if (status.CanChat)
                        await Shell.Current.GoToAsync("chat", new ShellNavigationQueryParameters { ["address"] = result.ChatAddress! });
                    else
                        await Shell.Current.GoToAsync("contact-details", new ShellNavigationQueryParameters
                        {
                            ["contact"] = ContactViewModel.FromChatRequest(result.ChatAddress!, status.Status, result.Title)
                        });
                    break;
                case GlobalSearchResultType.Contact:
                    await Shell.Current.GoToAsync("contact-details", new ShellNavigationQueryParameters
                    {
                        ["contact"] = new ContactViewModel(result.Contact!, Strings.ChatStatusUnavailable, displayName: result.Title)
                    });
                    break;
                case GlobalSearchResultType.DApp:
                    await Commands.LaunchDApp.ExecuteAsync(result.DApp!);
                    break;
            }
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(Shell.Current.CurrentPage, this) && version == searchVersion)
                ErrorText = ex.Message;
        }
        finally { openingResult = false; }
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
    public required string? Title { get; init; }
    public required string Subtitle { get; init; }
    public required string IconGlyph { get; init; }
    public string? IconImageSource { get; init; }
    public bool HasIconImage => !string.IsNullOrWhiteSpace(IconImageSource);
    public bool HasIconGlyph => !HasIconImage;
    public bool IsContact => Type == GlobalSearchResultType.Contact;
    public bool IsNotContact => !IsContact;
    public string? AvatarAddress => Contact?.Address ?? ChatAddress;
    public bool IsRecentDApp { get; init; }
    public AssetInfo? Asset { get; init; }
    public Contact? Contact { get; init; }
    public string? ChatAccountId { get; init; }
    public string? ChatAddress { get; init; }
    public DApp? DApp { get; init; }
    public string TypeText => Type switch
    {
        GlobalSearchResultType.Asset => Strings.Asset,
        GlobalSearchResultType.Contact => Strings.HomeContacts,
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

    public static GlobalSearchResult FromContact(Contact contact, string? displayName = null)
    {
        return new()
        {
            Type = GlobalSearchResultType.Contact,
            Title = displayName ?? MessagingService.ResolveChatDisplayName(contact.Label, null),
            Subtitle = contact.Address,
            IconGlyph = "\ue60d",
            Contact = contact
        };
    }

    public static GlobalSearchResult FromChatContact(string accountId, string address, string? displayName = null) => new()
    {
        Type = GlobalSearchResultType.Contact,
        Title = displayName,
        Subtitle = address,
        IconGlyph = "\ue60d",
        ChatAccountId = accountId,
        ChatAddress = address
    };

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
