using Neo;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;

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
        if (!UInt160.TryParse(ContractHash?.Trim(), out UInt160 hash))
        {
            ErrorMessage = Strings.InvalidContractHash;
            return;
        }
        try
        {
            await tokenManager.AddCustomTokenAsync(hash);
            await CloseAsync(true);
        }
        catch
        {
            ErrorMessage = Strings.TokenNotFound;
        }
    }

    async void OnCancel(object sender, EventArgs e) => await CloseAsync(false);
}
