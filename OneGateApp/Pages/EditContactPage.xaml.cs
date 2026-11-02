using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Extensions;
using Microsoft.EntityFrameworkCore;
using NeoOrder.OneGate.Controls;
using NeoOrder.OneGate.Controls.Popups;
using NeoOrder.OneGate.Controls.Views.Validation;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Models;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services;
using NeoOrder.OneGate.Services.Messaging;
using Contact = NeoOrder.OneGate.Data.Contact;

namespace NeoOrder.OneGate.Pages;

public partial class EditContactPage : ContentPage, IQueryAttributable
{
    readonly IServiceProvider serviceProvider;
    readonly ApplicationDbContext dbContext;
    readonly AddressBookService addressBookService;
    readonly MessagingService messagingService;
    bool hasCompleted;

    public bool IsWorking
    {
        get;
        private set
        {
            field = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanEdit));
        }
    }
    public string? ErrorText { get; private set { field = value; OnPropertyChanged(); } }
    public bool IsNewContact { get; private set { field = value; OnPropertyChanged(); } }
    public bool CanEdit => !IsWorking && !hasCompleted;

    public required Contact Contact { get; set { field = value; OnPropertyChanged(); } }

    public EditContactPage(IServiceProvider serviceProvider, ApplicationDbContext dbContext, AddressBookService addressBookService, MessagingService messagingService)
    {
        this.serviceProvider = serviceProvider;
        this.dbContext = dbContext;
        this.addressBookService = addressBookService;
        this.messagingService = messagingService;
        InitializeComponent();
    }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        IsNewContact = false;
        Contact? source;
        if (query.TryGetValue("chat-contact", out var chatContact) && chatContact is ContactViewModel { IsSaved: false } entry
            && (entry.CanChat || entry.HasOutgoingRequest))
        {
            // Prefer a record already saved elsewhere; opening the editor alone never saves a contact.
            source = dbContext.Contacts.AsNoTracking().SingleOrDefault(p => p.Address == entry.Address);
            if (source is null)
            {
                source = new Contact { Address = entry.Address, Label = "" };
                IsNewContact = true;
            }
        }
        else
        {
            string address = query["address"].ToString()!;
            source = dbContext.Contacts.AsNoTracking().SingleOrDefault(p => p.Address == address);
            if (source is null)
            {
                Contact = new Contact { Address = address, Label = "" };
                hasCompleted = true;
                OnPropertyChanged(nameof(CanEdit));
                ErrorText = Strings.ContactDeletedSuccessfully;
                return;
            }
        }
        // Form edits stay private until the user explicitly saves them.
        Contact = new Contact { Address = source.Address, Label = source.Label, Note = source.Note };
        hasCompleted = false;
        OnPropertyChanged(nameof(CanEdit));
        ErrorText = null;
    }

    async void OnSubmitted(object sender, EventArgs e) => await UpdateContactAsync(delete: false);

    async void OnDelete(object sender, EventArgs e) => await UpdateContactAsync(delete: true);

    async Task UpdateContactAsync(bool delete)
    {
        if (!CanEdit || delete && IsNewContact || !ReferenceEquals(Shell.Current.CurrentPage, this)) return;
        Contact contact = Contact;
        string address = contact.Address;
        string label = contact.Label?.Trim() ?? "";
        string? chatAccountId = messagingService.IsEnabled ? messagingService.AccountId : null;
        bool IsCurrent() => ReferenceEquals(Shell.Current.CurrentPage, this) && ReferenceEquals(Contact, contact);
        IsWorking = true;
        ErrorText = null;
        try
        {
            bool updated = false;
            if (delete)
            {
                var popup = serviceProvider.GetServiceOrCreateInstance<ConfirmationPopup>();
                popup.Title = Strings.DeleteConfirmation;
                popup.Message = Strings.DeleteContactText;
                popup.AcceptText = Strings.Delete;
                popup.IsDanger = true;
                var result = await this.ShowPopupAsync<bool>(popup);
                if (!result.Result || !IsCurrent()) return;
                await dbContext.Contacts.Where(p => p.Address == address).ExecuteDeleteAsync();
            }
            else
            {
                string? note = contact.Note?.Trim();
                if (!addressBookService.IsLabelAvailable(label, address))
                {
                    ErrorText = Strings.LabelAlreadyExists;
                    return;
                }
                if (IsNewContact)
                {
                    var saved = new Contact { Address = address, Label = label, Note = note };
                    dbContext.Contacts.Add(saved);
                    try
                    {
                        await dbContext.SaveChangesAsync();
                        updated = true;
                        IsNewContact = false;
                    }
                    finally { dbContext.Entry(saved).State = EntityState.Detached; }
                }
                else
                {
                    updated = await dbContext.Contacts
                        .Where(p => p.Address == address)
                        .ExecuteUpdateAsync(builder => builder
                            .SetProperty(p => p.Label, _ => label)
                            .SetProperty(p => p.Note, _ => note)) > 0;
                }
            }
            // Keep completed operations from being submitted again if navigation fails.
            if (ReferenceEquals(Contact, contact)) hasCompleted = true;
            // An address removed during editing must not be reported as a successful update.
            string completionText = updated ? Strings.ContactUpdatedSuccessfully : Strings.ContactDeletedSuccessfully;
            // The address book is authoritative, even if the optional chat mirror fails.
            if (updated && chatAccountId is not null)
            {
                try { await messagingService.SetChatContactAliasAsync(chatAccountId, address, label); }
                catch (Exception)
                {
                    completionText = Strings.ContactNameSyncFailed;
                }
            }
            if (!IsCurrent()) return;
            await Toast.Show(completionText);
            if (IsCurrent()) await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            if (IsCurrent()) ErrorText = ex.Message;
        }
        finally { IsWorking = false; }
    }

    void OnValidateLabelDuplicity(object sender, CustomValidationEventArgs e)
    {
        e.IsValid = addressBookService.IsLabelAvailable(e.Value as string, Contact.Address);
        if (!e.IsValid)
            e.ErrorMessage = Strings.LabelAlreadyExists;
    }
}
