namespace NeoOrder.OneGate.Services.Messaging;

public sealed record ChatInvitation(string LocalAccountId, string Address, string SignerDeviceId,
    string Value, DateTimeOffset ExpiresAt);
