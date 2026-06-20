using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Extensions;
using Microsoft.EntityFrameworkCore;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Controls.Popups;
using NeoOrder.OneGate.Controls.Views.Validation;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using Contact = NeoOrder.OneGate.Data.Contact;

namespace NeoOrder.OneGate.Pages;

public partial class EditContactPage : ContentPage, IQueryAttributable
{
    readonly IServiceProvider serviceProvider;
    readonly ApplicationDbContext dbContext;
    readonly AddressBookService addressBookService;

    public required Contact Contact { get; set { field = value; OnPropertyChanged(); } }

    public EditContactPage(IServiceProvider serviceProvider, ApplicationDbContext dbContext, AddressBookService addressBookService)
    {
        this.serviceProvider = serviceProvider;
        this.dbContext = dbContext;
        this.addressBookService = addressBookService;
        InitializeComponent();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("contact", out var contact))
        {
            Contact = (Contact)contact;
        }
        else
        {
            string address = query["address"].ToString()!;
            Contact = dbContext.Contacts.Single(p => p.Address == address);
        }
    }

    async void OnSubmitted(object sender, EventArgs e)
    {
        string label = Contact.Label.Trim();
        string? note = Contact.Note?.Trim();
        if (!addressBookService.IsLabelAvailable(label, Contact.Address))
        {
            await Toast.Show(Strings.LabelAlreadyExists);
            return;
        }
        await dbContext.Contacts
            .Where(p => p.Address == Contact.Address)
            .ExecuteUpdateAsync(builder => builder
                .SetProperty(p => p.Label, _ => label)
                .SetProperty(p => p.Note, _ => note)
                .SetProperty(p => p.IsAddressBookEntry, _ => true));
        await Toast.Show(Strings.ContactUpdatedSuccessfully);
        GlobalStates.Invalidate<ContactsPage>();
        GlobalStates.Invalidate<SettingsPage>();
        await Shell.Current.GoToAsync("..");
    }

    void OnValidateLabelDuplicity(object sender, CustomValidationEventArgs e)
    {
        e.IsValid = addressBookService.IsLabelAvailable(e.Value as string, Contact.Address);
        if (!e.IsValid)
            e.ErrorMessage = Strings.LabelAlreadyExists;
    }

    async void OnDelete(object sender, EventArgs e)
    {
        var popup = serviceProvider.GetServiceOrCreateInstance<ConfirmationPopup>();
        popup.Title = Strings.DeleteConfirmation;
        popup.Message = Strings.DeleteContactText;
        popup.AcceptText = Strings.Delete;
        popup.IsDanger = true;
        var result = await this.ShowPopupAsync<bool>(popup);
        if (!result.Result) return;
        Contact? contact = await dbContext.Contacts.SingleOrDefaultAsync(p => p.Address == Contact.Address);
        if (contact is not null)
        {
            dbContext.Contacts.Remove(contact);
            await dbContext.SaveChangesAsync();
        }
        await Toast.Show(Strings.ContactDeletedSuccessfully);
        GlobalStates.Invalidate<ContactsPage>();
        GlobalStates.Invalidate<SettingsPage>();
        await Shell.Current.GoToAsync("..");
    }
}
