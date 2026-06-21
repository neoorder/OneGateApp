namespace NeoOrder.OneGate.Models;

public class TransactionPreviewAssetChange
{
    public required string Title { get; init; }
    public required string AmountText { get; init; }
    public required string DetailText { get; init; }
    public bool IsOutgoing { get; init; }
    public bool IsIncoming { get; init; }
}
