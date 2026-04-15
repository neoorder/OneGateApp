using NeoOrder.OneGate.Services;
using Plugin.Maui.ScreenSecurity;

namespace NeoOrder.OneGate.Pages;

public partial class VerifyMnemonicPage : ContentPage
{
    readonly IServiceProvider serviceProvider;
    readonly IScreenSecurity screenSecurity;

    public bool ShowSkip { get; set { field = value; OnPropertyChanged(); } }

    public VerifyMnemonicPage(IServiceProvider serviceProvider, IScreenSecurity screenSecurity)
    {
        this.serviceProvider = serviceProvider;
        this.screenSecurity = screenSecurity;
        InitializeComponent();
    }

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

    void Word_Clicked(object sender, EventArgs e)
    {
        Button button = (Button)sender;
        if (editorMnemonic.Text?.Contains(button.Text) == true)
        {
            editorMnemonic.Text = editorMnemonic.Text.Replace(button.Text, "").Replace("  ", " ").Trim();
            button.Opacity = 1.0;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(editorMnemonic.Text))
                editorMnemonic.Text = button.Text;
            else
                editorMnemonic.Text += " " + button.Text;
            button.Opacity = 0.1;
        }
    }

    void OnKonamiCodeEntered(object sender, EventArgs e)
    {
        ShowSkip = true;
    }

    async void OnSubmitted(object sender, EventArgs e)
    {
        Page page = serviceProvider.GetServiceOrCreateInstance<CreatePasswordPage>();
        page.BindingContext = BindingContext;
        await Navigation.PushAsync(page);
    }
}
