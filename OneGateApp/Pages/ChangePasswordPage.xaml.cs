using CommunityToolkit.Maui.Alerts;
using Neo.Wallets;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Controls.Views;
using NeoOrder.OneGate.Controls.Views.Validation;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using Plugin.Maui.ScreenSecurity;

namespace NeoOrder.OneGate.Pages;

public partial class ChangePasswordPage : ContentPage
{
    readonly IScreenSecurity screenSecurity;
    readonly ApplicationDbContext dbContext;
    readonly Wallet wallet;

    public required string CurrentPassword { get; set; }
    public required string Password { get; set { field = value; OnPropertyChanged(); } }

    public ChangePasswordPage(IScreenSecurity screenSecurity, ApplicationDbContext dbContext, IWalletProvider walletProvider)
    {
        this.screenSecurity = screenSecurity;
        this.dbContext = dbContext;
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
                await Shell.Current.GoToAsync("..");
                if (await dbContext.Settings.ExistsAsync("biometric/credential"))
                {
                    await dbContext.Settings.DeleteAsync("biometric/credential");
                    await Toast.Show(Strings.BiometricResetText);
                }
                else
                {
                    await Toast.Show(Strings.PasswordChanged);
                }
            }
            else
            {
                errMsg.SetError(Strings.ErrorMessageIncorrectPassword);
            }
        }
    }

    void Password_Validate(object sender, CustomValidationEventArgs e)
    {
        PasswordPolicyResult result = PasswordPolicy.Evaluate(e.Value as string);
        e.IsValid = result.IsValid;
        e.ErrorMessage = result.Failure switch
        {
            PasswordPolicyFailure.Required => string.Format(Strings.DefaultRequiredErrorMessage, Strings.Password),
            PasswordPolicyFailure.TooShort => string.Format(Strings.PasswordTooShortError, PasswordPolicy.MinLength),
            PasswordPolicyFailure.TooLong => string.Format(Strings.PasswordTooLongError, PasswordPolicy.MaxLength),
            PasswordPolicyFailure.TooCommon => Strings.PasswordTooCommonError,
            PasswordPolicyFailure.RepeatedPattern => Strings.PasswordRepeatedPatternError,
            PasswordPolicyFailure.TooSimple => Strings.PasswordTooSimpleError,
            _ => null
        };
    }
}
