using Meshline.Models.Client;

namespace NeoOrder.OneGate.Services.Messaging;

public sealed record PendingChatMessage(Guid Id, string LocalAccountId, string Address, string Text, DateTimeOffset CreatedAt)
{
    public MessageRef? ReplyTo { get; init; }
    public MessageSendStatus? SendStatus { get; init; }
    public string? ErrorMessage { get; init; }
    public bool HasSubmissionStarted { get; init; }
    public bool IsSubmitting => SendStatus is null && ErrorMessage is null;
    public bool CanStartSubmission => IsSubmitting && !HasSubmissionStarted;
}
