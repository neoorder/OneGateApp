using Microsoft.EntityFrameworkCore;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Services;
using Contact = NeoOrder.OneGate.Data.Contact;

namespace NeoOrder.OneGate.Pages;

public partial class ContactsPage : ContentPage
{
    readonly AddressBookService addressBookService;
    Contact[] allContacts = [];

    public LoadingService LoadingService { get; set { field = value; OnPropertyChanged(); } }
    public Contact[]? Contacts { get; set { field = value; OnPropertyChanged(); } }

    public ContactsPage(AddressBookService addressBookService)
    {
        this.LoadingService = new(LoadContactsAsync);
        this.addressBookService = addressBookService;
        InitializeComponent();
        LoadingService.BeginLoad();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (this.ShouldRefresh())
            LoadingService.BeginLoad();
    }

    async Task LoadContactsAsync()
    {
        allContacts = await addressBookService.GetEntriesAsync();
        Contacts = allContacts;
    }

    void OnSearch(object sender, TextChangedEventArgs e)
    {
        string filter = e.NewTextValue?.Trim() ?? "";
        if (filter.Length == 0)
        {
            Contacts = allContacts;
            return;
        }

        Contacts = allContacts
            .Where(p =>
                p.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                p.Address.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                (p.Note?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToArray();
    }
}
