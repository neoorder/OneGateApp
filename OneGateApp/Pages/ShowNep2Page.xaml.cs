using Neo.Wallets;
using NeoOrder.OneGate.Controls.Views;
using Plugin.Maui.ScreenSecurity;

namespace NeoOrder.OneGate.Pages;

public partial class ShowNep2Page : ContentPage
{
    readonly IScreenSecurity screenSecurity;
    readonly IWalletProvider walletProvider;

    public string? Password { get; set { field = value; OnPropertyChanged(); } }
    public string? PrivateKey { get; set { field = value; OnPropertyChanged(); } }

    public ShowNep2Page(IScreenSecurity screenSecurity, IWalletProvider walletProvider)
    {
        this.screenSecurity = screenSecurity;
        this.walletProvider = walletProvider;
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

    async void OnSubmit(object sender, EventArgs e)
    {
        Submit submit = (Submit)sender;
        using (submit.EnterBusyState())
        {
            Wallet wallet = walletProvider.GetWallet()!;
            KeyPair key = wallet.GetDefaultAccount()!.GetKey()!;
            PrivateKey = await Task.Run(() => key.Export(Password!, wallet.ProtocolSettings.AddressVersion));
        }
    }
}
