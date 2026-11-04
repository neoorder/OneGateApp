using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NeoOrder.OneGate.Data;

[Index(nameof(LocalAccountId), nameof(PeerAddress))]
public class UnsentChatMessage
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; }
    public required string LocalAccountId { get; set; }
    public required string PeerAddress { get; set; }
    public required string Text { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? ReplyToSenderAccountId { get; set; }
    public string? ReplyToMessageId { get; set; }
    public string? ErrorMessage { get; set; }
}
