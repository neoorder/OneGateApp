using Neo.Wallets;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Services;
using Plugin.Maui.ScreenSecurity;

namespace NeoOrder.OneGate.Pages;

public partial class GenerateMnemonicPage : ContentPage
{
    readonly IServiceProvider serviceProvider;
    readonly IScreenSecurity screenSecurity;

    public GenerateMnemonicPage(IServiceProvider serviceProvider, IScreenSecurity screenSecurity)
    {
        this.serviceProvider = serviceProvider;
        this.screenSecurity = screenSecurity;
        InitializeComponent();
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        if (BindingContext is WalletCreationContext context)
            context.Mnemonic = Mnemonic.Create(256);
    }

    protected override void OnAppearing()
    {
        ScreenSecurityCoordinator.Enter(screenSecurity);
    }

    protected override void OnDisappearing()
    {
        ScreenSecurityCoordinator.Exit(screenSecurity);
    }

    async void OnSubmitted(object sender, EventArgs e)
    {
        Page page = serviceProvider.GetServiceOrCreateInstance<VerifyMnemonicPage>();
        page.BindingContext = BindingContext;
        await Navigation.PushAsync(page);
    }
}
