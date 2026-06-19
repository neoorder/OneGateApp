using Neo;
using Neo.Cryptography;
using Neo.Extensions;
using Neo.Wallets;
using System.Text.Json.Serialization;

namespace NeoOrder.OneGate.Models;

public class AuthenticationChallengePayload
{
    static readonly TimeSpan AuthenticationTimeout = TimeSpan.FromMinutes(5);

    public required string Action { get; init; }
    [JsonPropertyName("grant_type")]
    public required string GrantType { get; init; }
    [JsonPropertyName("allowed_algorithms")]
    public required string[] AllowedAlgorithms { get; init; }
    public required string Domain { get; init; }
    public required uint[] Networks { get; init; }
    public required ulong Nonce { get; init; }
    public required uint Timestamp { get; init; }
    public Uri? Callback { get; init; }

    public void Validate(ProtocolSettings protocolSettings)
    {
        if (Action != "Authentication")
            throw new NotSupportedException("Unsupported action");
        if (GrantType != "Signature")
            throw new NotSupportedException("Unsupported grant type");
        if (!AllowedAlgorithms.Contains("ECDSA-P256"))
            throw new NotSupportedException("No supported algorithm");
        if (string.IsNullOrWhiteSpace(Domain))
            throw new InvalidOperationException("Domain cannot be empty");
        if (!Networks.Contains(protocolSettings.Network))
            throw new NotSupportedException("No supported network");
        if (DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(Timestamp) > AuthenticationTimeout)
            throw new InvalidOperationException("Request expired");
    }

    public AuthenticationResponsePayload CreateResponse(WalletAccount account, ProtocolSettings protocolSettings)
    {
        uint timestamp = (uint)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        byte[] message;
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(protocolSettings.Network);
            writer.Write(Nonce);
            writer.Write(timestamp);
            writer.Write(account.ScriptHash);
            writer.WriteVarString(Action);
            writer.WriteVarString(Domain);
            message = stream.ToArray();
        }
        KeyPair key = account.GetKey()!;
        return new AuthenticationResponsePayload
        {
            Algorithm = "ECDSA-P256",
            Network = protocolSettings.Network,
            PublicKey = key.PublicKey,
            Address = account.Address,
            Nonce = Nonce,
            Timestamp = timestamp,
            Signature = Crypto.Sign(message, key)
        };
    }
}
