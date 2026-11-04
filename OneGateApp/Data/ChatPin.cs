using Microsoft.EntityFrameworkCore;

namespace NeoOrder.OneGate.Data;

[PrimaryKey(nameof(LocalAccountId), nameof(PeerAddress))]
public class ChatPin
{
    public required string LocalAccountId { get; set; }
    public required string PeerAddress { get; set; }
}
