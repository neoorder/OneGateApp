// Exercise the linked production authorization service with real Neo wallet
// states, replacing only platform prompts, MAUI views and the settings store.
public sealed class Page { public object GetParentWindow() => new(); }
public sealed class EmptyServices : IServiceProvider { public object? GetService(Type type) => null; }
namespace NeoOrder.OneGate.Services
{
    public static class ServiceExtensions
    {
        public static T GetServiceOrCreateInstance<T>(this IServiceProvider services) where T : new() => new();
    }
    public sealed class ProgressWindowOverlay : IDisposable
    {
        public ProgressWindowOverlay(object window, string title, string message) { }
        public void Dispose() { }
    }
    public static class DataProtectionService
    {
        public static Func<Task<bool>> Authenticate { get; set; } = () => Task.FromResult(false);
        public static Func<Task<string>> Unprotect { get; set; } = () => Task.FromResult("");
        public static Task<bool> AuthenticateAsync(string title, string? message) => Authenticate();
        public static Task<string> UnprotectAsync(byte[] credential, string title, string? message) => Unprotect();
    }
}
namespace NeoOrder.OneGate.Controls.Popups
{
    public sealed class WalletAuthorizationPopup
    {
        public string? Title { get; set; }
        public string? Message { get; set; }
        public string? Domain { get; set; }
    }
}
namespace CommunityToolkit.Maui.Extensions
{
    public sealed class PopupResult<T> { public T? Result { get; set; } }
    public static class PopupExtensions
    {
        public static Task<PopupResult<T>> ShowPopupAsync<T>(this Page page, object popup) => Task.FromResult(new PopupResult<T>());
    }
}
namespace NeoOrder.OneGate.Data
{
    public sealed class ApplicationDbContext { public SettingsStore Settings { get; } = new(); }
    public sealed class SettingsStore
    {
        public Task<T?> GetAsync<T>(string key) => Task.FromResult((T?)(object)new byte[] { 1 });
    }
}
namespace NeoOrder.OneGate.Properties
{
    public static class Strings
    {
        public const string LoginRequestText = "Login request";
        public const string Domain = "Domain";
        public const string UnlockingWallet = "Unlocking wallet";
    }
}
