using CommunityToolkit.Maui.Alerts;
using Neo.Wallets;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Controls.Views;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using Plugin.Maui.ScreenSecurity;

namespace NeoOrder.OneGate.Pages;

public partial class CreateBiometricPage : ContentPage
{
    readonly IScreenSecurity screenSecurity;
    readonly WalletAuthorizationService walletAuthorizationService;
    readonly Wallet wallet;

    public required string Password { get; set; }

    public CreateBiometricPage(IScreenSecurity screenSecurity, WalletAuthorizationService walletAuthorizationService, IWalletProvider walletProvider)
    {
        this.screenSecurity = screenSecurity;
        this.walletAuthorizationService = walletAuthorizationService;
        this.wallet = walletProvider.GetWallet()!;
        InitializeComponent();
    }

#if !(IOS || MACCATALYST)
    protected override void OnAppearing()
    {
        screenSecurity.ActivateScreenSecurityProtection();
    }

    protected override void OnDisappearing()
    {
        screenSecurity.DeactivateScreenSecurityProtection();
    }
#endif

    async void OnSubmitted(object sender, EventArgs e)
    {
        Submit submit = (Submit)sender;
        using (submit.EnterBusyState())
        {
            bool isPasswordCorrect = await Task.Run(() => wallet.VerifyPassword(Password));
            if (!isPasswordCorrect)
            {
                errMsg.SetError(Strings.ErrorMessageIncorrectPassword);
                return;
            }
            byte[] encrypted;
            try
            {
                encrypted = await DataProtectionService.ProtectAsync(Password);
            }
            catch (OperationCanceledException)
            {
                await Toast.Show(Strings.OperationCancelled);
                return;
            }
            catch
            {
                await Toast.Show(Strings.BiometricUnavailableText);
                return;
            }
            await walletAuthorizationService.SetBiometricCredentialAsync(encrypted);
            GlobalStates.Invalidate<SettingsPage>();
            await this.GoBackAsync();
            await Toast.Show(Strings.BiometricCredentialCreatedText);
        }
    }

    protected override bool OnBackButtonPressed()
    {
        _ = this.GoBackAsync();
        return true;
    }
}
