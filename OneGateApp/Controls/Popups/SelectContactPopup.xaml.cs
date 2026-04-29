using Microsoft.EntityFrameworkCore;
using NeoOrder.OneGate.Data;
using Contact = NeoOrder.OneGate.Data.Contact;

namespace NeoOrder.OneGate.Controls.Popups;

public partial class SelectContactPopup : MyPopup<Contact?>
{
    readonly ApplicationDbContext dbContext;
    Contact[]? contacts_all;

    public required Contact[] Contacts { get; set { field = value; OnPropertyChanged(); } }

    public SelectContactPopup(ApplicationDbContext dbContext)
    {
        this.dbContext = dbContext;
        InitializeComponent();
        LoadData();
    }

    void OnSearch(object sender, TextChangedEventArgs e)
    {
        contacts_all ??= Contacts;
        if (string.IsNullOrWhiteSpace(e.NewTextValue))
        {
            Contacts = contacts_all;
        }
        else
        {
            string filter = e.NewTextValue.Trim();
            Contacts = contacts_all
                .Where(p => p.Label.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
    }

    async void OnSelectContact(object sender, TappedEventArgs e)
    {
        if (e.Parameter is Contact contact)
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
        Contacts = await dbContext.Contacts.ToArrayAsync();
    }
}
