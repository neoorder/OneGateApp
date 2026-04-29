using Neo;
using NeoOrder.OneGate.Models;

namespace NeoOrder.OneGate.Controls.Popups;

public partial class SelectAssetPopup : MyPopup<AssetInfo?>
{
    IReadOnlyList<AssetInfo>? assets_all;

    public required IReadOnlyList<AssetInfo> Assets { get; set { field = value; OnPropertyChanged(); } }
    public required UInt160 SelectedHash { get; set { field = value; OnPropertyChanged(); } }

    public SelectAssetPopup()
    {
        InitializeComponent();
    }

    void OnSearch(object sender, TextChangedEventArgs e)
    {
        assets_all ??= Assets;
        if (string.IsNullOrWhiteSpace(e.NewTextValue))
        {
            Assets = assets_all;
        }
        else
        {
            string filter = e.NewTextValue.Trim();
            Assets = assets_all
                .Where(p => p.Token.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) || p.Token.Symbol.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
    }

    async void OnSelectAsset(object sender, TappedEventArgs e)
    {
        if (e.Parameter is AssetInfo asset)
            await CloseAsync(asset);
    }

    async void OnCancel(object sender, EventArgs e)
    {
        await CloseAsync(null);
    }
}
