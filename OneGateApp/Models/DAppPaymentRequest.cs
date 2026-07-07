using Neo;
using Neo.SmartContract;
using System.Numerics;

namespace NeoOrder.OneGate.Models;

class DAppPaymentRequest
{
    public required UInt160 Asset { get; init; }
    public UInt160? From { get; init; }
    public required UInt160 To { get; init; }
    public required BigInteger Amount { get; init; }
    public ContractParameter? Data { get; init; }
    public string? Purpose { get; init; }
    public string? Details { get; init; }
    public int? TimeoutSeconds { get; init; }

    public string? DisplayDetails
    {
        get
        {
            string? purpose = string.IsNullOrWhiteSpace(Purpose) ? null : Purpose.Trim();
            string? details = string.IsNullOrWhiteSpace(Details) ? null : Details.Trim();
            return (purpose, details) switch
            {
                (null, null) => null,
                (not null, null) => purpose,
                (null, not null) => details,
                _ => $"{purpose}{Environment.NewLine}{details}"
            };
        }
    }

    public TimeSpan ConfirmationTimeout => TimeSpan.FromSeconds(TimeoutSeconds ?? 45);

    public void Validate()
    {
        if (Asset == UInt160.Zero)
            throw new DapiException(10002, "Invalid asset");
        if (To == UInt160.Zero)
            throw new DapiException(10002, "Invalid recipient");
        if (Amount <= 0)
            throw new DapiException(10002, "Invalid amount");
        if (TimeoutSeconds is < 1 or > 120)
            throw new DapiException(10002, "Invalid timeout");
    }
}
