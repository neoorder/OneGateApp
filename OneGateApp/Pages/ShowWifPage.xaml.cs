using Neo.Wallets;
using Plugin.Maui.ScreenSecurity;

namespace NeoOrder.OneGate.Pages;

public partial class ShowWifPage : ContentPage
{
    readonly IScreenSecurity screenSecurity;

    public string PrivateKey { get; set { field = value; OnPropertyChanged(); } }

    public ShowWifPage(IScreenSecurity screenSecurity, IWalletProvider walletProvider)
    {
        this.screenSecurity = screenSecurity;
        this.PrivateKey = walletProvider.GetWallet()!.GetDefaultAccount()!.GetKey()!.Export();
        InitializeComponent();
    }

#if !MACCATALYST
    protected override void OnAppearing()
    {
        base.OnAppearing();
        screenSecurity.ActivateScreenSecurityProtection();
    }

    protected override void OnDisappearing()
    {
        screenSecurity.DeactivateScreenSecurityProtection();
        base.OnDisappearing();
    }
#endif
}
