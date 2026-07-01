using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;

namespace NeoOrder.OneGate.Pages;

public partial class DAppDetailsPage : ContentPage, IQueryAttributable
{
    readonly ApplicationDbContext dbContext;

    public bool IsFavorite { get; set { field = value; OnPropertyChanged(); } }
    public string SourceHost { get; private set { field = value; OnPropertyChanged(); } } = "";
    public string SourceStatus { get; private set { field = value; OnPropertyChanged(); } } = "";
    public string WebsiteHost { get; private set { field = value; OnPropertyChanged(); } } = "";
    public bool HasWebsite { get; private set { field = value; OnPropertyChanged(); } }
    public string RecentActivityText { get; private set { field = value; OnPropertyChanged(); } } = "";
    public string TagsDisplay { get; private set { field = value; OnPropertyChanged(); } } = "";
    public bool HasTags { get; private set { field = value; OnPropertyChanged(); } }

    public DAppDetailsPage(ApplicationDbContext dbContext, IHomeShortcutService homeShortcutService)
    {
        this.dbContext = dbContext;
        InitializeComponent();
        if (!homeShortcutService.IsSupported)
            ToolbarItems.Remove(addToHomeScreenButton);
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        BindingContext = query["dapp"];
    }

    protected override async void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        if (BindingContext is not DApp dapp) return;

        SourceHost = GetHost(dapp.Url) ?? dapp.Url;
        SourceStatus = dapp.Id > 0 ? Strings.DAppCatalogStatus : Strings.DAppExternalStatus;
        WebsiteHost = GetHost(dapp.Website) ?? "";
        HasWebsite = !string.IsNullOrWhiteSpace(dapp.Website);
        TagsDisplay = string.Join(", ", (dapp.Tags ?? [])
            .Select(DApp.LocalizeTag)
            .Append(dapp.GameTypeDisplayName)
            .OfType<string>()
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.CurrentCultureIgnoreCase));
        HasTags = !string.IsNullOrWhiteSpace(TagsDisplay);

        List<int>? favorites = await dbContext.Settings.GetAsync<List<int>>("dapps/favorite");
        IsFavorite = favorites?.Contains(dapp.Id) ?? false;
        List<int>? recents = await dbContext.Settings.GetAsync<List<int>>("dapps/recent");
        RecentActivityText = recents?.Contains(dapp.Id) == true
            ? Strings.DAppRecentlyOpened
            : Strings.DAppNotRecentlyOpened;
    }

    void OnFavoriteClicked(object sender, EventArgs e)
    {
        IsFavorite = !IsFavorite;
    }

    static string? GetHost(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            return uri.Host;
        return url;
    }
}
