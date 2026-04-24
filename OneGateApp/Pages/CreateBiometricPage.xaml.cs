using CommunityToolkit.Maui.Alerts;
using Neo.Wallets;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Controls.Views;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using Plugin.Maui.ScreenSecurity;

namespace NeoOrder.OneGate.Pages;

public partial class CreateBiometricPage : ContentPage
{
    readonly IScreenSecurity screenSecurity;
    readonly ApplicationDbContext dbContext;
    readonly Wallet wallet;

    public required string Password { get; set; }

    public CreateBiometricPage(IScreenSecurity screenSecurity, ApplicationDbContext dbContext, IWalletProvider walletProvider)
    {
        this.screenSecurity = screenSecurity;
        this.dbContext = dbContext;
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
            await dbContext.Settings.PutAsync("biometric/credential", encrypted);
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
