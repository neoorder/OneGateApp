// Link the actual production click handler; replace only MAUI rendering/navigation.
namespace NeoOrder.OneGate.Pages
{
    public class Page { public object? BindingContext { get; set; } }
    public class ContentPage : Page
    {
        public Navigation Navigation { get; } = new();
        protected void OnPropertyChanged() { }
        protected virtual void OnAppearing() { }
        protected virtual void OnDisappearing() { }
    }
    public sealed class Navigation { public Task PushAsync(Page page) => Task.CompletedTask; }
    public sealed class CreatePasswordPage : Page { }
    public sealed class Button
    {
        public string Text { get; set; } = "";
        public double Opacity { get; set; } = 1;
    }
    public sealed class Editor { public string Text { get; set; } = ""; }
    public partial class VerifyMnemonicPage
    {
        readonly Editor editorMnemonic = new();
        void InitializeComponent() { }
        public void SelectForTest(Button word) => Word_Clicked(word, EventArgs.Empty);
        public string PhraseForTest => editorMnemonic.Text;
    }
}
namespace NeoOrder.OneGate.Services
{
    public static class ServiceExtensions
    {
        public static T GetServiceOrCreateInstance<T>(this IServiceProvider services) where T : new() => new();
    }
    public static class ScreenSecurityCoordinator
    {
        public static void Enter(Plugin.Maui.ScreenSecurity.IScreenSecurity screenSecurity) { }
        public static void Exit(Plugin.Maui.ScreenSecurity.IScreenSecurity screenSecurity) { }
    }
}
namespace Plugin.Maui.ScreenSecurity { public interface IScreenSecurity { } }
public sealed class ScreenSecurity : Plugin.Maui.ScreenSecurity.IScreenSecurity { }
public sealed class EmptyServices : IServiceProvider { public object? GetService(Type type) => null; }
