using Neo;
using Neo.Wallets;
using Neo.Wallets.BIP32;
using NeoOrder.OneGate.Controls.Views;
using NeoOrder.OneGate.Controls.Views.Validation;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using Plugin.Maui.ScreenSecurity;

namespace NeoOrder.OneGate.Pages;

public partial class CreatePasswordPage : ContentPage
{
    readonly IServiceProvider serviceProvider;
    readonly IScreenSecurity screenSecurity;
    readonly ProtocolSettings protocolSettings;

    public CreatePasswordPage(IServiceProvider serviceProvider, IScreenSecurity screenSecurity, ProtocolSettings protocolSettings)
    {
        this.serviceProvider = serviceProvider;
        this.screenSecurity = screenSecurity;
        this.protocolSettings = protocolSettings;
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

    async void OnSubmitted(object sender, EventArgs e)
    {
        Submit submit = (Submit)sender;
        using (submit.EnterBusyState())
        {
            WalletCreationContext context = (WalletCreationContext)BindingContext;
            await Task.Run(() =>
            {
                if (context.PrivateKey is null && context.Mnemonic is not null)
                {
                    byte[] seed = context.Mnemonic.DeriveSeed();
                    ExtendedKey key = ExtendedKey.Create(seed, "m/44'/888'/0'/0/0");
                    context.PrivateKey = key.PrivateKey;
                }
                Wallet wallet = Wallet.Create(context.WalletName, SharedOptions.WalletPath, context.Password!, protocolSettings)!;
                wallet.CreateAccount(context.PrivateKey!);
                wallet.Save();
            });
            bool useBiometricService = await DataProtectionService.CheckAvailabilityAsync();
            if (useBiometricService)
            {
                Page page = serviceProvider.GetServiceOrCreateInstance<RequestCreateBiometricPage>();
                Window.Page = new NavigationPage(page);
            }
            else
            {
                Window.Page = serviceProvider.GetServiceOrCreateInstance<AppShell>();
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
