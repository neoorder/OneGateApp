using Neo;

namespace NeoOrder.OneGate.Models;

class DAppPaymentResult
{
    public required UInt256 TransactionHash { get; init; }
    public ulong? BlockTime { get; init; }
    public bool Succeeded { get; init; }
    public bool Confirmed { get; init; }
}
