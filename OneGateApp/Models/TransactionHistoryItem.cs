namespace NeoOrder.OneGate.Models;

public sealed class TransactionHistoryItem
{
    public required string Title { get; init; }
    public required string AmountText { get; init; }
    public required string DirectionText { get; init; }
    public required string TimeText { get; init; }
    public required string CounterpartyText { get; init; }
    public required string TransactionHash { get; init; }
    public string? BlockText { get; init; }
    public bool HasBlockText => !string.IsNullOrWhiteSpace(BlockText);
}
