using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services.Messaging;
using Contact = NeoOrder.OneGate.Data.Contact;

namespace NeoOrder.OneGate.Models;

public sealed class ContactViewModel(Contact contact, string chatStatus, bool canChat = false, bool hasIncomingRequest = false, bool isSaved = true, string? displayName = null, bool hasOutgoingRequest = false)
{
    public static ContactViewModel FromChatContact(string address, string? displayName = null) => new(
        new Contact { Address = address, Label = "" },
        Strings.ChatContactConnected, canChat: true, isSaved: false, displayName: displayName);

    public static ContactViewModel FromChatRequest(string address, string status, string? displayName = null) => new(
        new Contact { Address = address, Label = "" }, status, isSaved: false, displayName: displayName, hasOutgoingRequest: true);

    public bool IsSaved { get; } = isSaved;
    public string Address { get; } = contact.Address;
    public string Label { get; } = contact.Label;
    public string? DisplayName { get; } = displayName ?? MessagingService.ResolveChatDisplayName(contact.Label, null);
    public string? Note { get; } = contact.Note;
    public string ChatStatus { get; } = chatStatus;
    public bool CanChat { get; } = canChat;
    public bool HasOutgoingRequest { get; } = hasOutgoingRequest;
    public string ChatRequestText => hasIncomingRequest || HasOutgoingRequest ? Strings.ChatViewRequest : Strings.ChatRequest;
    public string ChatRequestRoute => "//home/chat-request"
        + "?address=" + Uri.EscapeDataString(Address);
}
