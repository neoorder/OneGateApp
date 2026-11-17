namespace NeoOrder.OneGate.Models;

public sealed class TransactionHistoryItem
{
    public required string Title { get; init; }
    public string? AmountText { get; init; }
    public string? DirectionText { get; init; }
    public required string TimeText { get; init; }
    public string? CounterpartyText { get; init; }
    public required string TransactionHash { get; init; }
    public string? BlockText { get; init; }
    public bool HasAmountText => !string.IsNullOrWhiteSpace(AmountText);
    public bool HasDirectionText => !string.IsNullOrWhiteSpace(DirectionText);
    public bool HasCounterpartyText => !string.IsNullOrWhiteSpace(CounterpartyText);
    public bool HasBlockText => !string.IsNullOrWhiteSpace(BlockText);
}
