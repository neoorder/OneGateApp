using Neo.Wallets;
using NeoOrder.OneGate.Pages;
using NeoOrder.OneGate.Services.Messaging;

namespace NeoOrder.OneGate;

public partial class AppShell : Shell
{
    public AppShell(MessagingService messagingService, IWalletProvider walletProvider)
    {
        this.messagingService = messagingService;
        this.walletProvider = walletProvider;
        InitializeComponent();
        Loaded += OnUnreadLoaded;
        Unloaded += OnUnreadUnloaded;
    }

    static AppShell()
    {
        Routing.RegisterRoute("authenticate", typeof(AuthenticatePage));
        Routing.RegisterRoute("full", typeof(FullScreenImagePage));
        Routing.RegisterRoute("scan", typeof(ScanPage));
        Routing.RegisterRoute("home/add-contact", typeof(NewContactPage));
        Routing.RegisterRoute("home/edit-contact", typeof(EditContactPage));
        Routing.RegisterRoute("home/contact-details", typeof(ContactDetailsPage));
        Routing.RegisterRoute("home/chat-request", typeof(ChatRequestPage));
        Routing.RegisterRoute("home/chat-requests", typeof(ChatRequestsPage));
        Routing.RegisterRoute("home/chat", typeof(ChatPage));
        Routing.RegisterRoute("home/chat-invitation", typeof(ChatInvitationPage));
        Routing.RegisterRoute("home/search", typeof(GlobalSearchPage));
        Routing.RegisterRoute("home/news/details", typeof(NewsDetailsPage));
        Routing.RegisterRoute("home/settings", typeof(SettingsPage));
        Routing.RegisterRoute("home/settings/chat-settings", typeof(ChatSettingsPage));
        Routing.RegisterRoute("home/settings/about", typeof(AboutPage));
        Routing.RegisterRoute("home/settings/activity", typeof(ActivityCenterPage));
        Routing.RegisterRoute("home/settings/assets/hidden", typeof(HiddenAssetsPage));
        Routing.RegisterRoute("home/settings/biometric/create", typeof(CreateBiometricPage));
        Routing.RegisterRoute("home/settings/biometric/disable", typeof(DisableBiometricPage));
        Routing.RegisterRoute("home/settings/content", typeof(ContentSettingsPage));
        Routing.RegisterRoute("home/settings/developer", typeof(DeveloperToolsPage));
        Routing.RegisterRoute("home/settings/language", typeof(LanguagePage));
        Routing.RegisterRoute("home/settings/news", typeof(NewsSettingsPage));
        Routing.RegisterRoute("home/settings/security", typeof(SecurityCenterPage));
        Routing.RegisterRoute("home/settings/wallet/details", typeof(WalletDetailsPage));
        Routing.RegisterRoute("dapps/details", typeof(DAppDetailsPage));
        Routing.RegisterRoute("gaming/details", typeof(DAppDetailsPage));
        Routing.RegisterRoute("launch", typeof(LaunchDAppPage));
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
