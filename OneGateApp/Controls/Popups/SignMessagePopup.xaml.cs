using Neo.Wallets;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using System.Text;

namespace NeoOrder.OneGate.Controls.Popups;

public partial class SignMessagePopup : MyPopup<string?>
{
    readonly WalletAuthorizationService walletAuthorizationService;

    public string? Account { get; set { field = value; OnPropertyChanged(); } }
    public string[] Addresses { get; set { field = value; OnPropertyChanged(); } }
    public required string Message
    {
        get;
        set
        {
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ReadableMessage));
            OnPropertyChanged(nameof(HasReadableMessage));
        }
    }
    // Best-effort human-readable form of an encoded payload, so the user isn't blind-signing an opaque blob.
    public string? ReadableMessage => DecodeReadable(Message);
    public bool HasReadableMessage => !string.IsNullOrEmpty(ReadableMessage) && ReadableMessage != Message;

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

    static string? DecodeReadable(string? message)
    {
        if (string.IsNullOrEmpty(message)) return null;
        string hex = message.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? message[2..] : message;
        if (hex.Length >= 2 && hex.Length % 2 == 0 && hex.All(Uri.IsHexDigit))
        {
            try
            {
                string text = Encoding.UTF8.GetString(Convert.FromHexString(hex));
                if (IsPrintable(text)) return text;
            }
            catch { }
        }
        try
        {
            string text = Encoding.UTF8.GetString(Convert.FromBase64String(message));
            if (IsPrintable(text)) return text;
        }
        catch { }
        return null;
    }

    static bool IsPrintable(string text)
        => text.Length > 0 && text.All(c => !char.IsControl(c) || c is '\n' or '\r' or '\t');
}
