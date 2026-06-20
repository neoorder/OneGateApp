using NeoOrder.OneGate.Services;
using Contact = NeoOrder.OneGate.Data.Contact;

namespace NeoOrder.OneGate.Controls.Popups;

public partial class SelectContactPopup : MyPopup<Contact?>
{
    readonly AddressBookService addressBookService;

    public required Contact[] Contacts { get; set { field = value; OnPropertyChanged(); } }

    public SelectContactPopup(AddressBookService addressBookService)
    {
        this.addressBookService = addressBookService;
        InitializeComponent();
        LoadData();
    }

    async void OnSearch(object sender, TextChangedEventArgs e)
    {
        Contacts = await addressBookService.GetSuggestionsAsync(e.NewTextValue, 30);
    }

    async void OnSelectContact(object sender, TappedEventArgs e)
    {
        Contact contact = (Contact)e.Parameter!;
        await CloseAsync(contact);
    }

    async void OnCancel(object sender, EventArgs e)
    {
        await CloseAsync(null);
    }

    async void LoadData()
    {
        await Task.WhenAll(LoadContactsAsync());
    }

    async Task LoadContactsAsync()
    {
        Contacts = await addressBookService.GetEntriesAsync();
    }
}
