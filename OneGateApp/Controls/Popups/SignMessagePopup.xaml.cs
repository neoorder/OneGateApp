using Neo.Wallets;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using System.Text;

namespace NeoOrder.OneGate.Controls.Popups;

public partial class SignMessagePopup : MyPopup<string?>
{
    const int MaxReadableMessageInputLength = 16 * 1024;

    readonly WalletAuthorizationService walletAuthorizationService;

    public string? Account { get; set { field = value; OnPropertyChanged(); } }
    public string[] Addresses { get; set { field = value; OnPropertyChanged(); } }
    public bool IsBase64Encoded
    {
        get;
        set
        {
            field = value;
            UpdateReadableMessage();
        }
    }
    public required string Message
    {
        get;
        set
        {
            field = value;
            UpdateReadableMessage();
            OnPropertyChanged();
        }
    }
    // Human-readable form of the actual base64-decoded payload that will be signed.
    public string? ReadableMessage { get; private set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasReadableMessage)); } }
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

    void UpdateReadableMessage()
    {
        ReadableMessage = DecodeReadable(Message, IsBase64Encoded);
    }

    static string? DecodeReadable(string? message, bool isBase64Encoded)
    {
        if (!isBase64Encoded || string.IsNullOrEmpty(message) || message.Length > MaxReadableMessageInputLength)
            return null;

        try
        {
            string text = Encoding.UTF8.GetString(Convert.FromBase64String(message));
            if (IsPrintable(text)) return text;
        }
        catch (FormatException) { }
        catch (ArgumentException) { }
        return null;
    }

    static bool IsPrintable(string text)
        => text.Length > 0 && text.All(c => !char.IsControl(c) || c is '\n' or '\r' or '\t');
}
