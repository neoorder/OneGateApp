using Neo;
using Neo.Cryptography;
using Neo.Cryptography.ECC;
using Neo.SmartContract;
using Neo.Wallets;
using Neo.Wallets.BIP32;
using NeoOrder.OneGate.Controls.Views;
using NeoOrder.OneGate.Controls.Views.Validation;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using Plugin.Maui.ScreenSecurity;

namespace NeoOrder.OneGate.Pages;

public partial class SelectImportTypePage : ContentPage
{
    readonly IServiceProvider serviceProvider;
    readonly ProtocolSettings protocolSettings;
    readonly IScreenSecurity screenSecurity;

    public required WalletCreationContext CreationContext { get; set { field = value; OnPropertyChanged(); } }
    public int SelectedIndex { get; set { field = value; OnPropertyChanged(); } } = -1;

    public SelectImportTypePage(IServiceProvider serviceProvider, ProtocolSettings protocolSettings, IScreenSecurity screenSecurity)
    {
        this.serviceProvider = serviceProvider;
        this.protocolSettings = protocolSettings;
        this.screenSecurity = screenSecurity;
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        ScreenSecurityCoordinator.Enter(screenSecurity);
    }

    protected override void OnDisappearing()
    {
        ScreenSecurityCoordinator.Exit(screenSecurity);
    }

    void Mnemonic_Validate(object sender, CustomValidationEventArgs e)
    {
        if (e.Value is string value)
        {
            try
            {
                Mnemonic.Parse(value);
                return;
            }
            catch
            {
            }
        }
        e.IsValid = false;
    }

    void PrivateKey_Validate(object sender, CustomValidationEventArgs e)
    {
        if (e.Value is string value)
        {
            if (value.Length == 52)
            {
                try
                {
                    Wallet.GetPrivateKeyFromWIF(value);
                    e.IsValid = true;
                    return;
                }
                catch
                {
                }
            }
            else if (value.Length == 64)
            {
                try
                {
                    byte[] key = Convert.FromHexString(value);
                    e.IsValid = key.Length == 32;
                    return;
                }
                catch
                {
                }
            }
        }
        e.IsValid = false;
    }

    void Nep2_Validate(object sender, CustomValidationEventArgs e)
    {
        if (e.Value is string value)
        {
            try
            {
                byte[] data = value.Base58CheckDecode();
                e.IsValid = data.Length == 39 && data[0] == 0x01 && data[1] == 0x42 && data[2] == 0xe0;
                return;
            }
            catch
            {
            }
        }
        e.IsValid = false;
    }

    async void OnSubmitted(object sender, EventArgs e)
    {
        Submit submit = (Submit)sender;
        using (submit.EnterBusyState())
        {
            if (SelectedIndex == 0)
            {
                CreationContext.Mnemonic = Mnemonic.Parse(editorMnemonic.Text);
                if (CreationContext.Mnemonic.Count == 12)
                {
                    await Task.Run(() =>
                    {
                        byte[] seed = CreationContext.Mnemonic.DeriveSeed();
                        ExtendedKey key_k1 = ExtendedKey.Create(seed, "m/44'/888'/0'/0/0", ECCurve.Secp256k1);
                        ExtendedKey key_r1 = ExtendedKey.Create(seed, "m/44'/888'/0'/0/0", ECCurve.Secp256r1);
                        CreationContext.DerivedKeys = [new(key_k1.PrivateKey), new(key_r1.PrivateKey)];
                    });
                    SelectImportAddressPage page = serviceProvider.GetServiceOrCreateInstance<SelectImportAddressPage>();
                    page.CreationContext = CreationContext;
                    await Navigation.PushAsync(page);
                }
                else
                {
                    Page page = serviceProvider.GetServiceOrCreateInstance<CreatePasswordPage>();
                    page.BindingContext = CreationContext;
                    await Navigation.PushAsync(page);
                }
            }
            else if (SelectedIndex == 1)
            {
                if (entryPrivateKey.Text.Length == 52)
                    CreationContext.PrivateKey = Wallet.GetPrivateKeyFromWIF(entryPrivateKey.Text);
                else
                    CreationContext.PrivateKey = Convert.FromHexString(entryPrivateKey.Text);
                Page page = serviceProvider.GetServiceOrCreateInstance<CreatePasswordPage>();
                page.BindingContext = CreationContext;
                await Navigation.PushAsync(page);
            }
            else if (SelectedIndex == 2)
            {
                string nep2 = entryNep2Key.Text;
                string password = entryPassword.Text;
                bool success = await Task.Run(() =>
                {
                    byte[] key;
                    try
                    {
                        key = Wallet.GetPrivateKeyFromNEP2(nep2, password, protocolSettings.AddressVersion);
                    }
                    catch
                    {
                        return false;
                    }
                    Wallet wallet = Wallet.Create(CreationContext.WalletName, SharedOptions.WalletPath, password, protocolSettings)!;
                    wallet.CreateAccount(key);
                    wallet.Save();
                    return true;
                });
                if (success)
                {
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
                else
                {
                    errMsg.SetError(Strings.ErrorMessageIncorrectPassword);
                }
            }
        }
    }

    static UInt160 CreateAccount(ExtendedKey key)
    {
        return Neo.SmartContract.Contract.CreateSignatureRedeemScript(key.PublicKey).ToScriptHash();
    }
}
