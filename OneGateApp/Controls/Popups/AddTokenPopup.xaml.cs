using Neo;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using NeoOrder.OneGate.Services.RPC;

namespace NeoOrder.OneGate.Controls.Popups;

public partial class AddTokenPopup : MyPopup<bool>
{
    readonly TokenManager tokenManager;

    public string? ContractHash
    {
        get;
        set { field = value; OnPropertyChanged(); ErrorMessage = null; }
    }
    public string? ErrorMessage
    {
        get;
        set { field = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasError)); }
    }
    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public AddTokenPopup(TokenManager tokenManager)
    {
        this.tokenManager = tokenManager;
        InitializeComponent();
    }

    async void OnAdd(object sender, EventArgs e)
    {
        if (!UInt160.TryParse(ContractHash?.Trim() ?? string.Empty, out UInt160? parsedHash) || parsedHash is null)
        {
            ErrorMessage = Strings.InvalidContractHash;
            return;
        }
        UInt160 hash = parsedHash;
        try
        {
            await tokenManager.AddCustomTokenAsync(hash);
            await CloseAsync(true);
        }
        catch (RpcException)
        {
            ErrorMessage = Strings.TokenNotFound;
        }
        catch
        {
            ErrorMessage = Strings.UnknownError;
        }
    }

    async void OnCancel(object sender, EventArgs e) => await CloseAsync(false);
}
