namespace NeoOrder.OneGate.Models;

public class TransactionPreviewWarning
{
    public required string Title { get; init; }
    public required string Message { get; init; }
    public bool IsHighRisk { get; init; }
}
