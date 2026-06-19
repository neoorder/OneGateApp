using Neo.Wallets;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;

namespace NeoOrder.OneGate.Controls.Popups;

public partial class SignMessagePopup : MyPopup<string?>
{
    readonly WalletAuthorizationService walletAuthorizationService;

    public string? Account { get; set { field = value; OnPropertyChanged(); } }
    public string[] Addresses { get; set { field = value; OnPropertyChanged(); } }
    public required string Message { get; set { field = value; OnPropertyChanged(); } }

    public SignMessagePopup(IWalletProvider walletProvider, WalletAuthorizationService walletAuthorizationService)
    {
        this.walletAuthorizationService = walletAuthorizationService;
        Addresses = walletProvider.GetWallet()!.GetAccounts().Select(p => p.Address).ToArray();
        InitializeComponent();
    }

    async void OnSubmit(object sender, EventArgs e)
    {
        if (await walletAuthorizationService.RequestAuthorizationAsync(this.FindAncestor<Page>(), Strings.SignMessage, Strings.SignMessageRequestText))
            await CloseAsync(Account);
    }

    async void OnCancel(object sender, EventArgs e)
    {
        await CloseAsync(null);
    }
}
