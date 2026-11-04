using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Extensions;
using Neo.Wallets;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Controls.Popups;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;

namespace NeoOrder.OneGate.Pages;

public partial class DeleteWalletPage : ContentPage
{
    readonly IServiceProvider serviceProvider;
    readonly WalletAuthorizationService walletAuthorizationService;
    readonly WalletProvider walletProvider;

    public DeleteWalletPage(IServiceProvider serviceProvider, WalletAuthorizationService walletAuthorizationService, IWalletProvider walletProvider)
    {
        this.serviceProvider = serviceProvider;
        this.walletAuthorizationService = walletAuthorizationService;
        this.walletProvider = (WalletProvider)walletProvider;
        InitializeComponent();
    }

    async void OnSubmitted(object sender, EventArgs e)
    {
        var popup = serviceProvider.GetServiceOrCreateInstance<ConfirmationPopup>();
        popup.Title = Strings.DeleteWallet;
        popup.Message = Strings.WalletDeletionIrreversibleWarning;
        popup.AcceptText = Strings.Delete;
        popup.IsDanger = true;
        var result = await this.ShowPopupAsync<bool>(popup);
        if (!result.Result) return;
        await walletAuthorizationService.ClearBiometricCredentialAsync();
        walletProvider.DeleteWallet();
        await Toast.Show(Strings.WalletDeleted);
        Window window = Window;
        Window[] windows = Application.Current!.Windows.Where(p => p != window).ToArray();
        foreach (var w in windows)
            Application.Current!.CloseWindow(w);
        WelcomePage page = serviceProvider.GetServiceOrCreateInstance<WelcomePage>();
        window.Page = new NavigationPage(page);
    }
}
