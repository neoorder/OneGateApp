// Standalone service tests retain the actual protocol and service. Only the
// platform storage, discovery, UI dispatch and page-construction edges are fake.
namespace NeoOrder.OneGate.Services.RemoteDebug
{
    sealed class MdnsRemoteDebuggerDiscovery : IAsyncDisposable
    {
        public event Action<string, string, int>? RemoteDebuggerDiscovered { add { } remove { } }
        public Task StartAsync() => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

namespace NeoOrder.OneGate.Pages
{
    public class LaunchDAppPage : Services.RemoteDebug.IRemoteDebugSessionHost
    {
        public void ConfigureRemoteDebug(string id, Services.RemoteDebug.RemoteDebugService service) => service.AttachSessionHost(id, this);
        public void ApplyQueryAttributes(IDictionary<string, object> query) { }
        public Task<System.Text.Json.Nodes.JsonObject> GetRemoteStatusAsync() => Task.FromResult(new System.Text.Json.Nodes.JsonObject());
        public Task<System.Text.Json.Nodes.JsonNode?> EvaluateRemoteAsync(string expression) => Task.FromResult<System.Text.Json.Nodes.JsonNode?>(null);
        public Task<byte[]> CaptureRemoteScreenshotAsync() => Task.FromResult(Array.Empty<byte>());
        public Task ReloadRemoteAsync(bool ignoreCache) => Task.CompletedTask;
        public Task StopRemoteAsync() { BoundaryHooks.OnHostStopped?.Invoke(); return Task.CompletedTask; }
    }
}

namespace NeoOrder.OneGate
{
    static class ServiceProviderExtensions
    {
        public static T GetServiceOrCreateInstance<T>(this IServiceProvider services) where T : new()
        {
            BoundaryHooks.BeforePageCreate?.Invoke();
            return new();
        }
    }
    static class BoundaryHooks
    {
        public static Action? BeforePageCreate;
        public static Action? OnOpenWindow;
        public static Action? OnHostStopped;
    }
    static class MainThread
    {
        public static Task InvokeOnMainThreadAsync(Func<Task> action) => action();
    }
    sealed class SecureStorage
    {
        public static SecureStorage Default { get; } = new();
        public Task<string?> GetAsync(string key) => Task.FromResult<string?>(null);
        public Task SetAsync(string key, string value) => Task.CompletedTask;
    }
    static class DeviceInfo
    {
        public static string Name => "Regression harness";
        public static string Platform => "Test";
    }
    sealed class Application
    {
        public static Application Current { get; } = new();
        public void OpenWindow(Window window) => BoundaryHooks.OnOpenWindow?.Invoke();
    }
    sealed class Window(NavigationPage page) { public NavigationPage Page { get; } = page; }
    sealed class NavigationPage(Pages.LaunchDAppPage page) { public Pages.LaunchDAppPage Page { get; } = page; }
}
