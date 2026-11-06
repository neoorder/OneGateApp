// Navigation-only doubles. PaymentAction, AppLinkAction and TokenAmount are linked production source.
public class Page { }
public class NavigationPage(Page page) : Page { public Page CurrentPage => page; }
public interface IQueryAttributable { void ApplyQueryAttributes(IDictionary<string, object> query); }
public class Shell
{
    public IDictionary<string, object>? Query { get; private set; }
    public Task GoToAsync(string route, IDictionary<string, object> query) { Query = query; return Task.CompletedTask; }
    public Task GoToAsync(string route) => Task.CompletedTask;
}
namespace NeoOrder.OneGate.Pages { public class SendPage : Page { } }
namespace NeoOrder.OneGate.Services
{
    public static class SharedOptions { public const string OneGateDomain = "example.invalid"; }
    public static class ServiceExtensions { public static T GetServiceOrCreateInstance<T>(this IServiceProvider _) where T : new() => new(); }
}
namespace NeoOrder.OneGate.Models.AppLinks
{
    static class AuthenticationAction { public static AppLinkAction? TryCreate(Uri _) => null; }
    static class LaunchDAppAction { public static AppLinkAction? TryCreate(Uri _) => null; }
    static class ViewNewsAction { public static AppLinkAction? TryCreate(Uri _) => null; }
}
