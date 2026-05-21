using NeoOrder.OneGate.Pages;

namespace NeoOrder.OneGate;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
    }

    static AppShell()
    {
        Routing.RegisterRoute("authenticate", typeof(AuthenticatePage));
        Routing.RegisterRoute("full", typeof(FullScreenImagePage));
        Routing.RegisterRoute("scan", typeof(ScanPage));
        Routing.RegisterRoute("home/contacts", typeof(ContactsPage));
        Routing.RegisterRoute("home/contacts/edit", typeof(EditContactPage));
        Routing.RegisterRoute("home/contacts/new", typeof(NewContactPage));
        Routing.RegisterRoute("home/news/details", typeof(NewsDetailsPage));
        Routing.RegisterRoute("home/settings", typeof(SettingsPage));
        Routing.RegisterRoute("home/settings/about", typeof(AboutPage));
        Routing.RegisterRoute("home/settings/assets/hidden", typeof(HiddenAssetsPage));
        Routing.RegisterRoute("home/settings/biometric/create", typeof(CreateBiometricPage));
        Routing.RegisterRoute("home/settings/biometric/disable", typeof(DisableBiometricPage));
        Routing.RegisterRoute("home/settings/developer", typeof(DeveloperToolsPage));
        Routing.RegisterRoute("home/settings/language", typeof(LanguagePage));
        Routing.RegisterRoute("home/settings/news", typeof(NewsSettingsPage));
        Routing.RegisterRoute("home/settings/wallet/details", typeof(WalletDetailsPage));
        Routing.RegisterRoute("dapps/details", typeof(DAppDetailsPage));
        Routing.RegisterRoute("dapps/launch", typeof(LaunchDAppPage));
        Routing.RegisterRoute("wallet/asset/details", typeof(AssetDetailsPage));
        Routing.RegisterRoute("wallet/asset/details/receive", typeof(ReceivePage));
        Routing.RegisterRoute("wallet/asset/details/send", typeof(SendPage));
        Routing.RegisterRoute("wallet/details", typeof(WalletDetailsPage));
        Routing.RegisterRoute("wallet/details/delete", typeof(DeleteWalletPage));
        Routing.RegisterRoute("wallet/details/export", typeof(ExportWalletPage));
        Routing.RegisterRoute("wallet/details/export/wif", typeof(ShowWifPage));
        Routing.RegisterRoute("wallet/details/export/nep2", typeof(ShowNep2Page));
        Routing.RegisterRoute("wallet/details/name", typeof(ChangeWalletNamePage));
        Routing.RegisterRoute("wallet/details/password", typeof(ChangePasswordPage));
        Routing.RegisterRoute("wallet/nft/details", typeof(NFTDetailsPage));
        Routing.RegisterRoute("wallet/nft/details/send", typeof(SendNFTPage));
        Routing.RegisterRoute("wallet/receive", typeof(ReceivePage));
        Routing.RegisterRoute("wallet/send", typeof(SendPage));
        Routing.RegisterRoute("wallet/sending", typeof(SendingPage));
    }
}
