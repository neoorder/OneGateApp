// Only native UI, persistence, and network boundaries are substituted. The page
// source linked by this project contains the production search/event logic.
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Models;

public class ContentPage
{
    public TestDispatcher Dispatcher { get; } = new();
    protected virtual void OnAppearing() { }
    protected void OnPropertyChanged(string? property = null) { }
}
public class TestDispatcher
{
    readonly List<Action> actions = [];
    public void DispatchDelayed(TimeSpan delay, Action action) => actions.Add(action);
    public void Flush() { Action[] pending = [.. actions]; actions.Clear(); foreach (Action action in pending) action(); }
}
public class TextChangedEventArgs(string? text) : EventArgs { public string? NewTextValue => text; }
public class TappedEventArgs : EventArgs { public object? Parameter { get; set; } }
public class TestSearchBar { public bool Focus() => true; public void Unfocus() { } }
public class Shell
{
    public static Shell Current { get; } = new();
    public Task GoToAsync(string route, Dictionary<string, object> args) => Task.CompletedTask;
}
public class TestServices(CachedCollection<DApp> catalog) : IServiceProvider
{
    public object? GetService(Type type) => type == typeof(CachedCollection<DApp>) ? catalog : null;
}
namespace Microsoft.EntityFrameworkCore
{
    public static class QueryExtensions
    {
        public static IEnumerable<T> AsNoTracking<T>(this IEnumerable<T> source) => source;
        public static Task<T[]> ToArrayAsync<T>(this IEnumerable<T> source) => Task.FromResult(source.ToArray());
    }
}
namespace NeoOrder.OneGate.Data
{
    public class ApplicationDbContext
    {
        public TestSettings Settings { get; } = new();
        public List<Contact> Contacts { get; } = [];
    }
    public class TestSettings
    {
        public Dictionary<string, object> Values { get; } = [];
        public Exception? Failure { get; set; }
        public Func<Task>? BeforeRead { get; set; }
        public async Task<T?> GetAsync<T>(string key) where T : notnull
        {
            if (BeforeRead is not null) await BeforeRead();
            if (Failure is not null) throw Failure;
            return Values.TryGetValue(key, out object? value) ? (T)value : default;
        }
    }
    public class Contact { public string Label { get; set; } = ""; public string Address { get; set; } = ""; }
    [Flags] public enum ContentWarnings { None = 0, Violence = 1 }
    [Flags] public enum DAppPlatforms { None = 0, Android = 1, iOS = 2, MacCatalyst = 4, Windows = 8, All = 15 }
    public class DApp
    {
        public int Id { get; set; }
        public ContentWarnings Warnings { get; set; }
        public bool IsHiddenFromCatalog { get; set; }
        public bool IsInDevelopment { get; set; }
        public DAppPlatforms SupportedPlatforms { get; set; } = DAppPlatforms.All;
        public string Name { get; set; } = "";
        public bool IsRegularApp { get; set; } = true;
        public string Url { get; set; } = "https://example.com";
        public string? IconUrl { get; set; }
        public string[]? Tags { get; set; }
        public Dictionary<string, string> NameLocalizer => new() { ["en"] = Name };
        public Dictionary<string, string>? DescriptionLocalizer => null;
        public static string LocalizeTag(string tag) => tag;
    }
}
namespace NeoOrder.OneGate.Models
{
    public class CachedCollection<T> : List<T>
    {
        public Func<Task> Load { get; set; } = () => Task.CompletedTask;
        public Task LoadAsync(string url, TimeSpan ttl) => Load();
    }
    public class AssetInfo { public Token Token { get; } = new(); public string DisplayBalance => "0"; }
    public class Token { public string Symbol => "NEO"; public string Name => "Neo"; public string Hash => "hash"; public string? Icon => null; }
}
namespace NeoOrder.OneGate.Services
{
    public class TokenManager { public Func<Task<IReadOnlyList<AssetInfo>>> Load { get; set; } = () => Task.FromResult<IReadOnlyList<AssetInfo>>([]); public Task<IReadOnlyList<AssetInfo>> LoadAssetsAsync() => Load(); }
    public class LoadingService(Func<Task> action)
    {
        public event EventHandler? Loaded;
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        public bool IsLoading => false;
        public async void BeginLoad() { await action(); Loaded?.Invoke(this, EventArgs.Empty); PropertyChanged?.Invoke(this, new(nameof(IsLoading))); }
    }
    public static class Extensions
    {
        public static T GetServiceOrCreateInstance<T>(this IServiceProvider services) => (T)services.GetService(typeof(T))!;
        public static bool ShouldRefresh(this ContentPage page) => false;
        public static string? Localize(this Dictionary<string, string> value) => value.Values.FirstOrDefault();
    }
}
namespace NeoOrder.OneGate.Properties
{
    public static class Strings
    {
        public static string Asset => "Asset";
        public static string AddressBook => "Address book";
        public static string Apps => "Apps";
    }
}
namespace NeoOrder.OneGate.Pages
{
    public static class Commands { public static TestCommand LaunchDApp { get; } = new(); }
    public class TestCommand
    {
        public DApp? Last { get; set; }
        public Task ExecuteAsync(DApp app) { Last = app; return Task.CompletedTask; }
    }
    public partial class GlobalSearchPage
    {
        readonly TestSearchBar searchBar = new();
        void InitializeComponent() { }
        public async Task LoadForTestAsync() { await LoadSearchDataAsync(); OnLoaded(this, EventArgs.Empty); }
        public void ChangeQueryForTest(string text) => OnSearchTextChanged(this, new(text));
        public void ConfirmForTest() => OnSearchButtonPressed(this, EventArgs.Empty);
        public Task OpenForTestAsync(GlobalSearchResult result) => OpenResultAsync(result);
    }
}
