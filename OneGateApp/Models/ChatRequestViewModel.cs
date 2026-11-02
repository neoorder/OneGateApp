using Meshline.Models.Client;
using NeoOrder.OneGate.Services.Messaging;
using System.ComponentModel;
using System.Globalization;

namespace NeoOrder.OneGate.Models;

public sealed class ChatRequestViewModel(ChatContactDetails details, string? displayName) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    public string LocalAccountId => details.LocalAccountId;
    public string? MessageId => details.Request?.MessageId;
    public DateTimeOffset RequestCreatedAt => details.Request!.CreatedAt;
    public string Address => details.Address;
    public string? DisplayName { get; private set; } = displayName;

    internal void UpdateDisplayName(string? value)
    {
        if (DisplayName == value) return;
        DisplayName = value;
        PropertyChanged?.Invoke(this, new(nameof(DisplayName)));
    }

    public string? Note => details.Request?.Note;
    public string CreatedAt => details.Request?.CreatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) ?? "";
    public string Status => details.Status;
    public bool IsIncoming => details.Request?.Direction == ContactRequestDirection.Incoming;
    public bool CanRespond => IsIncoming && !details.IsContact;
}
