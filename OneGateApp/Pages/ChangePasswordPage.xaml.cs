using CommunityToolkit.Maui.Alerts;
using Neo.Wallets;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Controls.Views;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using Plugin.Maui.ScreenSecurity;

namespace NeoOrder.OneGate.Pages;

public partial class ChangePasswordPage : ContentPage
{
    readonly IScreenSecurity screenSecurity;
    readonly WalletAuthorizationService walletAuthorizationService;
    readonly Wallet wallet;

    public required string CurrentPassword { get; set; }
    public required string Password { get; set { field = value; OnPropertyChanged(); } }

    public ChangePasswordPage(IScreenSecurity screenSecurity, WalletAuthorizationService walletAuthorizationService, IWalletProvider walletProvider)
    {
        this.screenSecurity = screenSecurity;
        this.walletAuthorizationService = walletAuthorizationService;
        wallet = walletProvider.GetWallet()!;
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

    async void OnSubmitted(object sender, EventArgs e)
    {
        Submit submit = (Submit)sender;
        using (submit.EnterBusyState())
        {
            bool success = await Task.Run(() => wallet.ChangePassword(CurrentPassword, Password));
            if (success)
            {
                bool biometricWasEnabled = await walletAuthorizationService.ClearBiometricCredentialAsync();
                GlobalStates.Invalidate<SettingsPage>();
                await Shell.Current.GoToAsync("..");
                await Toast.Show(biometricWasEnabled ? Strings.BiometricResetText : Strings.PasswordChanged);
            }
            else
            {
                errMsg.SetError(Strings.ErrorMessageIncorrectPassword);
            }
        }
    }
}
