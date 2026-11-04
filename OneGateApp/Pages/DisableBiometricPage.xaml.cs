using CommunityToolkit.Maui.Alerts;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;

namespace NeoOrder.OneGate.Pages;

public partial class DisableBiometricPage : ContentPage
{
    readonly WalletAuthorizationService walletAuthorizationService;

    public DisableBiometricPage(WalletAuthorizationService walletAuthorizationService)
    {
        this.walletAuthorizationService = walletAuthorizationService;
        InitializeComponent();
    }

    async void OnDisableClicked(object sender, EventArgs e)
    {
        await walletAuthorizationService.ClearBiometricCredentialAsync();
        GlobalStates.Invalidate<SettingsPage>();
        await Shell.Current.GoToAsync("..");
        await Toast.Show(Strings.BiometricDisabledText);
    }
}
