using CommunityToolkit.Maui.Extensions;
using NeoOrder.OneGate.Controls.Popups;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Services;

namespace NeoOrder.OneGate.Pages;

public partial class DAppDetailsPage : ContentPage, IQueryAttributable
{
    readonly IServiceProvider serviceProvider;
    readonly ApplicationDbContext dbContext;

    public bool IsFavorite { get; set { field = value; OnPropertyChanged(); } }

    public DAppDetailsPage(IServiceProvider serviceProvider, ApplicationDbContext dbContext, IHomeShortcutService homeShortcutService)
    {
        this.serviceProvider = serviceProvider;
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
        if (!dapp.CanReport) ToolbarItems.Remove(reportButton);
        List<int>? favorites = await dbContext.Settings.GetAsync<List<int>>("dapps/favorite");
        IsFavorite = favorites?.Contains(dapp.Id) ?? false;
    }

    void OnFavoriteClicked(object sender, EventArgs e)
    {
        IsFavorite = !IsFavorite;
    }

    async void OnReportClicked(object sender, EventArgs e)
    {
        DApp dapp = (DApp)BindingContext;
        if (!dapp.CanReport) return;
        var popup = serviceProvider.GetServiceOrCreateInstance<DAppReportPopup>();
        popup.DApp = dapp;
        await this.ShowPopupAsync<bool>(popup);
    }
}
