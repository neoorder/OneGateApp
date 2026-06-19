using NeoOrder.OneGate.Data;
using Contact = NeoOrder.OneGate.Data.Contact;

namespace NeoOrder.OneGate.Pages;

public partial class ContactTransactionPage : ContentPage, IQueryAttributable
{
    public required ContactTransfer Transfer { get; set { field = value; OnPropertyChanged(); } }
    public required Contact Contact { get; set { field = value; OnPropertyChanged(); } }

    public ContactTransactionPage()
    {
        InitializeComponent();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        Transfer = (ContactTransfer)query["transfer"];
        Contact = (Contact)query["contact"];
    }
}
