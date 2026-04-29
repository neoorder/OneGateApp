using NeoOrder.OneGate.Services;
using Plugin.Maui.ScreenSecurity;

namespace NeoOrder.OneGate.Pages;

public partial class VerifyMnemonicPage : ContentPage
{
    readonly IServiceProvider serviceProvider;
    readonly IScreenSecurity screenSecurity;

    public VerifyMnemonicPage(IServiceProvider serviceProvider, IScreenSecurity screenSecurity)
    {
        this.serviceProvider = serviceProvider;
        this.screenSecurity = screenSecurity;
        InitializeComponent();
    }

#if !MACCATALYST
    protected override void OnAppearing()
    {
        base.OnAppearing();
        ScreenSecurityCoordinator.Enter(screenSecurity);
    }

    protected override void OnDisappearing()
    {
        ScreenSecurityCoordinator.Exit(screenSecurity);
        base.OnDisappearing();
    }
#endif

    void Word_Clicked(object sender, EventArgs e)
    {
        Button button = (Button)sender;
        string word = button.Text;
        if (string.IsNullOrEmpty(word)) return;
        if (button.Opacity < 0.5)
        {
            string text = editorMnemonic.Text ?? string.Empty;
            int index = text.LastIndexOf(word, StringComparison.Ordinal);
            if (index < 0) return;
            editorMnemonic.Text = text.Remove(index, word.Length).Replace("  ", " ").Trim();
            button.Opacity = 1.0;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(editorMnemonic.Text))
                editorMnemonic.Text = word;
            else
                editorMnemonic.Text += " " + word;
            button.Opacity = 0.1;
        }
    }

    async void OnSubmitted(object sender, EventArgs e)
    {
        Page page = serviceProvider.GetServiceOrCreateInstance<CreatePasswordPage>();
        page.BindingContext = BindingContext;
        await Navigation.PushAsync(page);
    }
}
