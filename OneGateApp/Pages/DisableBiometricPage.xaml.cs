using CommunityToolkit.Maui.Alerts;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;

namespace NeoOrder.OneGate.Pages;

public partial class DisableBiometricPage : ContentPage
{
    readonly ApplicationDbContext dbContext;
    readonly WalletAuthorizationService authorizationService;

    public DisableBiometricPage(ApplicationDbContext dbContext, WalletAuthorizationService authorizationService)
    {
        this.dbContext = dbContext;
        this.authorizationService = authorizationService;
        InitializeComponent();
    }

    async void OnDisableClicked(object sender, EventArgs e)
    {
        bool authorized;
        try
        {
            authorized = await authorizationService.RequestAuthorizationAsync(this, Strings.DisableBiometric, Strings.DisableBiometricText);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        if (!authorized) return;

        await dbContext.Settings.DeleteAsync("biometric/credential");
        GlobalStates.Invalidate<SettingsPage>();
        await Shell.Current.GoToAsync("..");
        await Toast.Show(Strings.BiometricDisabledText);
    }
}
