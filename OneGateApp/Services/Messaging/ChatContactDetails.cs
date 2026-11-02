using Meshline.Models.Client;
using Meshline.Models.Protocol;
using NeoOrder.OneGate.Properties;

namespace NeoOrder.OneGate.Services.Messaging;

public sealed record ChatContactDetails(string LocalAccountId, string Address, AccountProfile? Profile,
    ContactInfo? Contact, ContactRequestInfo? Request)
{
    public bool IsContact => Contact?.State == ContactRelationshipState.Active;
    public bool CanCreateRequest => !IsContact && (Request is null || Request is
        { Direction: ContactRequestDirection.Outgoing, SendState: MessageSendState.Failed or MessageSendState.Canceled });
    public string Status => GetStatus(Contact, Request);

    internal static string GetStatus(ContactInfo? contact, ContactRequestInfo? request)
    {
        if (contact?.State == ContactRelationshipState.Active) return Strings.ChatContactConnected;
        if (request?.Direction == ContactRequestDirection.Incoming) return Strings.ChatRequestReceived;
        return request?.SendState switch
        {
            MessageSendState.RelayAccepted or MessageSendState.TargetAccepted => Strings.ChatRequestWaiting,
            MessageSendState.Queued => Strings.ChatMessageQueued,
            MessageSendState.Submitting => Strings.ChatMessageSending,
            MessageSendState.SubmissionUnknown => Strings.ChatMessageUnconfirmed,
            MessageSendState.Failed => Strings.ChatMessageFailed,
            MessageSendState.Canceled => Strings.OperationCancelled,
            _ => request is null ? Strings.HomeAddressOnly : Strings.ChatStatusUnavailable
        };
    }
}
