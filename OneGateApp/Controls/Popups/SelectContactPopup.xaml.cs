using NeoOrder.OneGate.Services;
using Contact = NeoOrder.OneGate.Data.Contact;

namespace NeoOrder.OneGate.Controls.Popups;

public partial class SelectContactPopup : MyPopup<Contact?>
{
    readonly AddressBookService addressBookService;
    int searchRequestId;

    public Contact[] Contacts { get; set { field = value; OnPropertyChanged(); } } = [];

    public SelectContactPopup(AddressBookService addressBookService)
    {
        this.addressBookService = addressBookService;
        InitializeComponent();
        LoadData();
    }

    async void OnSearch(object sender, TextChangedEventArgs e)
    {
        int requestId = Interlocked.Increment(ref searchRequestId);
        try
        {
            Contact[] contacts = await addressBookService.GetSuggestionsAsync(e.NewTextValue, 30);
            if (requestId == searchRequestId)
                Contacts = contacts;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            if (requestId == searchRequestId)
                Contacts = [];
        }
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
        try
        {
            Contacts = await addressBookService.GetEntriesAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            Contacts = [];
        }
    }
}
