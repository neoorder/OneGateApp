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
    readonly AddressBookService addressBookService;

    public string? Address { get; set { field = value; OnPropertyChanged(); } }
    public string? Label { get; set; }
    public string? Note { get; set; }

    public NewContactPage(ApplicationDbContext dbContext, ProtocolSettings protocolSettings, IWalletProvider walletProvider, AddressBookService addressBookService)
    {
        this.dbContext = dbContext;
        this.protocolSettings = protocolSettings;
        this.wallet = walletProvider.GetWallet()!;
        this.addressBookService = addressBookService;
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
            e.IsValid = true;
        }
        catch
        {
            e.IsValid = false;
        }
    }

    void OnValidateAddressDuplicity(object sender, CustomValidationEventArgs e)
    {
        string address = (string)e.Value!;
        if (!addressBookService.TryNormalizeAddress(address, out address))
        {
            e.IsValid = false;
            return;
        }
        e.IsValid = !dbContext.Contacts.Any(p => p.Address == address && p.IsAddressBookEntry)
            && !wallet.Contains(address.ToScriptHash(protocolSettings.AddressVersion));
    }

    void OnValidateLabelDuplicity(object sender, CustomValidationEventArgs e)
    {
        string? currentAddress = addressBookService.TryNormalizeAddress(Address, out string address) ? address : null;
        e.IsValid = addressBookService.IsLabelAvailable(e.Value as string, currentAddress);
        if (!e.IsValid)
            e.ErrorMessage = Strings.LabelAlreadyExists;
    }

    async void OnSubmitted(object sender, EventArgs e)
    {
        if (!addressBookService.TryNormalizeAddress(Address, out string address))
        {
            return;
        }
        string label = Label!.Trim();
        if (!addressBookService.IsLabelAvailable(label, address))
        {
            await Toast.Show(Strings.LabelAlreadyExists);
            return;
        }
        Contact? contact = await dbContext.Contacts.FindAsync(address);
        if (contact is null)
        {
            dbContext.Contacts.Add(new Contact
            {
                Address = address,
                Label = label,
                Note = Note?.Trim(),
                IsAddressBookEntry = true
            });
        }
        else
        {
            contact.Label = label;
            contact.Note = Note?.Trim();
            contact.IsAddressBookEntry = true;
        }
        await dbContext.SaveChangesAsync();
        await Toast.Show(Strings.ContactAddedSuccessfully);
        GlobalStates.Invalidate<ContactsPage>();
        GlobalStates.Invalidate<SettingsPage>();
        await Shell.Current.GoToAsync("..");
    }
}
