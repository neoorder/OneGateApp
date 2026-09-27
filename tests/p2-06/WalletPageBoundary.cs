// Link the production WalletPage, LoadingService and NftPageSession. Replace
// only MAUI/storage/navigation and the upstream inventory, not paging logic.
using Neo.Wallets;
using NeoOrder.OneGate.Models;

public class ContentPage
{
    protected virtual void OnAppearing() { }
    protected virtual void OnDisappearing() { }
    protected void OnPropertyChanged(string? name = null) { }
}
public sealed class TappedEventArgs : EventArgs { public object Parameter { get; init; } = null!; }
public sealed class Shell
{
    public static Shell Current { get; } = new();
    public Task GoToAsync(string route, IDictionary<string, object> parameters) => Task.CompletedTask;
}
namespace NeoOrder.OneGate.Controls
{
    public static class PageRefreshBoundary
    {
        public static bool ShouldRefresh(this ContentPage page) => false;
    }
}
namespace NeoOrder.OneGate.Pages
{
    public partial class WalletPage
    {
        void InitializeComponent() { }
    }
}
namespace NeoOrder.OneGate.Models
{
    public sealed class AssetInfo { public decimal? Valuation { get; init; } }
}
namespace NeoOrder.OneGate.Data
{
    public sealed class ApplicationDbContext { public SettingsBoundary Settings { get; } = new(); }
    public sealed class SettingsBoundary
    {
        public T Get<T>(string key) => default!;
        public Task PutAsync<T>(string key, T value) => Task.CompletedTask;
    }
}
namespace NeoOrder.OneGate.Services
{
    public sealed class TokenManager(Func<CancellationToken, IAsyncEnumerable<NFT[]>> pages)
    {
        public Task<IReadOnlyList<AssetInfo>> LoadAssetsAsync() => Task.FromResult<IReadOnlyList<AssetInfo>>([]);
        public IAsyncEnumerable<NFT[]> LoadNFTPagesAsync(bool includeHiddens = false, CancellationToken cancellationToken = default) => pages(cancellationToken);
    }
}
namespace NeoOrder.OneGate.Properties
{
    public static class Strings { public const string LoadingWalletData = "Loading wallet data"; }
}
namespace CommunityToolkit.Maui.Alerts
{
    public static class Toast { public static Task Show(string message) => Task.CompletedTask; }
}
