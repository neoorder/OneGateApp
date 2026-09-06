// Native controls and network/persistence are test boundaries. Actual page and
// LoadingService source is compiled unchanged into this project.
using System.Collections.ObjectModel;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Models;

public class ContentPage
{
    public TestDispatcher Dispatcher { get; } = new();
    protected virtual void OnAppearing() { }
    protected virtual void OnDisappearing() { }
    protected virtual void OnSizeAllocated(double width, double height) { }
    protected void OnPropertyChanged(string? property = null) { }
}
public interface IDispatcherTimer { TimeSpan Interval { get; set; } event EventHandler? Tick; void Start(); void Stop(); }
public class TestTimer : IDispatcherTimer
{
    public TimeSpan Interval { get; set; }
    public event EventHandler? Tick;
    public void Start() { }
    public void Stop() { }
    public void Fire() => Tick?.Invoke(this, EventArgs.Empty);
}
public class TestDispatcher { public IDispatcherTimer CreateTimer() => new TestTimer(); }
public class TestCarousel { public int Position { get; set; } }
public class TestItemsLayout { public int Span { get; set; } = 1; }
public class Button { public object CommandParameter { get; set; } = new(); }
public class TappedEventArgs : EventArgs { public object? Parameter { get; set; } }
public class Shell
{
    public static Shell Current { get; } = new();
    public Task GoToAsync(string route, Dictionary<string, object> parameters) => Task.CompletedTask;
}
public class TestServices(params object[] services) : IServiceProvider
{
    public object? GetService(Type type) => services.Single(item => item.GetType() == type);
}
namespace CommunityToolkit.Maui.Alerts { }
namespace NeoOrder.OneGate.Controls
{
    public static class Toast { public static Task Show(string text) => Task.CompletedTask; }
}
namespace NeoOrder.OneGate.Controls.Views
{
    public class TabBar { public IReadOnlyList<string>? Tabs { get; set; } = ["All"]; public string? SelectedTab { get; set; } = "All"; }
}
namespace NeoOrder.OneGate.Data
{
    public class ApplicationDbContext { public TestSettings Settings { get; } = new(); }
    public class TestSettings
    {
        readonly Dictionary<string, object> values = [];
        public string? FailureKey { get; set; }
        public Task? ReadGate { get; set; }
        public async Task<T?> GetAsync<T>(string key) where T : notnull
        {
            if (ReadGate is not null) await ReadGate;
            if (FailureKey == key) throw new InvalidOperationException("Controlled settings failure");
            return values.TryGetValue(key, out object? value) ? (T)value : default;
        }
        public Task PutAsync<T>(string key, T value) { values[key] = value!; return Task.CompletedTask; }
    }
    public class DApp
    {
        public int Id { get; set; }
        public bool IsGamingApp { get; set; } = true;
        public bool IsRegularApp => !IsGamingApp;
        public bool IsHiddenFromCatalog { get; set; }
        public bool IsInDevelopment { get; set; }
        public ContentWarnings Warnings { get; set; }
        public DAppPlatforms SupportedPlatforms { get; set; } = DAppPlatforms.iOS;
        public string? GameType { get; set; } = "Action";
        public string? GameTypeDisplayName => GameType;
        public string[]? Tags { get; set; }
        public static string? LocalizeGameType(string? gameType) => gameType;
        public static string LocalizeTag(string tag) => tag;
    }
    public class Banner { }
    public class News { }
}
namespace NeoOrder.OneGate.Models
{
    public class CachedCollection<T> : ObservableCollection<T>
    {
        bool loaded;
        public event EventHandler? CollectionLoaded;
        public List<bool> ForcedRequests { get; } = [];
        public Func<Task> Load { get; set; } = () => Task.CompletedTask;
        public async Task LoadAsync(string url, TimeSpan ttl, bool forceRefresh = false)
        {
            ForcedRequests.Add(forceRefresh);
            // Match production: disk cache is only loaded/signalled once per
            // collection instance; later failed refreshes do not signal at all.
            if (!loaded)
            {
                loaded = true;
                CollectionLoaded?.Invoke(this, EventArgs.Empty);
            }
            await Load();
            CollectionLoaded?.Invoke(this, EventArgs.Empty);
        }
    }
}
namespace NeoOrder.OneGate.Models.AppLinks
{
    public class LaunchDAppAction
    {
        public Uri Uri { get; } = new("https://example.com");
        public static LaunchDAppAction? TryCreate(string url) => null;
    }
}
namespace NeoOrder.OneGate.Services
{
    public static class Extensions
    {
        public static T GetServiceOrCreateInstance<T>(this IServiceProvider services) => (T)services.GetService(typeof(T))!;
        public static bool ShouldRefresh(this ContentPage page) => false;
    }
}
namespace NeoOrder.OneGate.Properties { public static class Strings { public static string All => "All"; } }
namespace NeoOrder.OneGate.Pages
{
    public static class Commands
    {
        public static TestCommand CheckForUpdates { get; } = new();
        public static TestCommand LaunchDApp { get; } = new();
        public static TestCommand OpenUrl { get; } = new();
    }
    public class TestCommand { public Task ExecuteAsync(object value) => Task.CompletedTask; }
    public partial class GamingPage
    {
        readonly TestItemsLayout GamesItemsLayout = new();
        readonly Controls.Views.TabBar gameTypeTabBar = new();
        void InitializeComponent() { }
    }
    public partial class DAppsPage
    {
        readonly Controls.Views.TabBar tabbarCategory = new();
        readonly Controls.Views.TabBar tabbarFavoriteOrRecent = new() { Tabs = ["Recent", "Favorite"], SelectedTab = "Recent" };
        void InitializeComponent() { }
    }
    public partial class HomePage
    {
        readonly TestCarousel carouselView = new();
        void InitializeComponent() { }
    }
}
