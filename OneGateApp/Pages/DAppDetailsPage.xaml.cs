using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Services;

namespace NeoOrder.OneGate.Pages;

public partial class DAppDetailsPage : ContentPage, IQueryAttributable
{
    readonly ApplicationDbContext dbContext;

    public bool IsFavorite { get; set { field = value; OnPropertyChanged(); } }

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
        DApp dapp = (DApp)BindingContext;
        List<int>? favorites = await dbContext.Settings.GetAsync<List<int>>("dapps/favorite");
        IsFavorite = favorites?.Contains(dapp.Id) ?? false;
    }

    void OnFavoriteClicked(object sender, EventArgs e)
    {
        IsFavorite = !IsFavorite;
    }
}
