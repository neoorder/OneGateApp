using CommunityToolkit.Maui.Alerts;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;

namespace NeoOrder.OneGate.Pages;

public partial class DisableBiometricPage : ContentPage
{
    readonly ApplicationDbContext dbContext;
    readonly WalletAuthorizationService walletAuthorizationService;

    public DisableBiometricPage(ApplicationDbContext dbContext, WalletAuthorizationService walletAuthorizationService)
    {
        this.dbContext = dbContext;
        this.walletAuthorizationService = walletAuthorizationService;
        InitializeComponent();
    }

    async void OnDisableClicked(object sender, EventArgs e)
    {
        if (!await walletAuthorizationService.RequestAuthorizationAsync(this, Strings.DisableBiometric, Strings.DisableBiometricText)) return;

        await dbContext.Settings.DeleteAsync("biometric/credential");
        GlobalStates.Invalidate<SettingsPage>();
        await Shell.Current.GoToAsync("..");
        await Toast.Show(Strings.BiometricDisabledText);
    }
}
