using CommunityToolkit.Maui.Alerts;
using Neo;
using Neo.Wallets;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Controls.Views.Validation;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using Contact = NeoOrder.OneGate.Data.Contact;

namespace NeoOrder.OneGate.Pages;

public partial class NewContactPage : ContentPage, IQueryAttributable
{
    readonly ApplicationDbContext dbContext;
    readonly ProtocolSettings protocolSettings;
    readonly Wallet wallet;

    public string? Address { get; set { field = value; OnPropertyChanged(); } }
    public string? Label { get; set; }

    public NewContactPage(ApplicationDbContext dbContext, ProtocolSettings protocolSettings, IWalletProvider walletProvider)
    {
        this.dbContext = dbContext;
        this.protocolSettings = protocolSettings;
        this.wallet = walletProvider.GetWallet()!;
        InitializeComponent();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("address", out var address))
            Address = (string)address;
    }

    void OnValidateAddress(object sender, CustomValidationEventArgs e)
    {
        if (e.Value is not string address)
        {
            e.IsValid = false;
            return;
        }
        try
        {
            address.ToScriptHash(protocolSettings.AddressVersion);
        }
        catch
        {
            e.IsValid = false;
        }
    }

    void OnValidateAddressDuplicity(object sender, CustomValidationEventArgs e)
    {
        string address = (string)e.Value!;
        e.IsValid = !dbContext.Contacts.Any(p => p.Address == address)
            && !wallet.Contains(address.ToScriptHash(protocolSettings.AddressVersion));
    }

    async void OnSubmitted(object sender, EventArgs e)
    {
        dbContext.Contacts.Add(new Contact
        {
            Address = Address!,
            Label = Label!
        });
        await dbContext.SaveChangesAsync();
        await Toast.Show(Strings.ContactAddedSuccessfully);
        GlobalStates.Invalidate<ContactsPage>();
        GlobalStates.Invalidate<SettingsPage>();
        await Shell.Current.GoToAsync("..");
    }
}
