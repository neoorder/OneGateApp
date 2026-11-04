using Microsoft.EntityFrameworkCore;

namespace NeoOrder.OneGate.Data;

[PrimaryKey(nameof(LocalAccountId), nameof(PeerAddress))]
public class ChatDraft
{
    public required string LocalAccountId { get; set; }
    public required string PeerAddress { get; set; }
    public required string Text { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string? ReplyToSenderAccountId { get; set; }
    public string? ReplyToMessageId { get; set; }
}
