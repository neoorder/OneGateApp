namespace NeoOrder.OneGate.Models;

public enum ActivityRecordKind
{
    DAppConnection,
    WalletAuthorization,
    Signature,
    Transaction,
    VaultOperation
}

public class ActivityRecord
{
    public ActivityRecordKind Kind { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int? DAppId { get; set; }
    public string? DAppName { get; set; }
    public string? DAppHost { get; set; }
    public string? TransactionHash { get; set; }
}
