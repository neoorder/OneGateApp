using System.Security.Cryptography;
using System.Text;
using NeoOrder.OneGate.DebugProtocol;
using Xunit;

namespace OneGate.DebugProtocol.Tests;

public sealed class ProtocolTests
{
    [Fact]
    public void PairingInvitationRoundTrips()
    {
        using DebugIdentity identity = DebugIdentity.Create();
        DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        PairingInvitation expected = new()
        {
            PairingId = Guid.Parse("5bb2ff82-f194-46bd-8b0e-45173bcb694f"),
            ExpiresAt = now.AddMinutes(2),
            DebuggerName = "Erik's PC",
            DebuggerPublicKey = DebugIdentity.ToUncompressedPublicKey(identity.PublicKey),
            PairingSecret = Enumerable.Range(0, 32).Select(p => (byte)p).ToArray(),
            Endpoints = [new("tcp://192.168.1.10:42310"), new("tcp://[fd00::10]:42310")]
        };

        PairingInvitation actual = PairingInvitation.Parse(expected.ToUri().AbsoluteUri, now);

        Assert.Equal(expected.PairingId, actual.PairingId);
        Assert.Equal(expected.ExpiresAt, actual.ExpiresAt);
        Assert.Equal(expected.DebuggerName, actual.DebuggerName);
        Assert.Equal(expected.DebuggerPublicKey, actual.DebuggerPublicKey);
        Assert.Equal(expected.PairingSecret, actual.PairingSecret);
        Assert.Equal(expected.Endpoints, actual.Endpoints);
    }

    [Fact]
    public void ExpiredInvitationIsRejected()
    {
        using DebugIdentity identity = DebugIdentity.Create();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        PairingInvitation invitation = new()
        {
            PairingId = Guid.NewGuid(),
            ExpiresAt = now.AddSeconds(1),
            DebuggerPublicKey = DebugIdentity.ToUncompressedPublicKey(identity.PublicKey),
            PairingSecret = RandomNumberGenerator.GetBytes(32),
            Endpoints = [new("tcp://127.0.0.1:5000")]
        };

        Assert.Throws<InvalidOperationException>(() => PairingInvitation.Parse(invitation.ToUri().AbsoluteUri, now.AddSeconds(2)));
    }

    [Fact]
    public void IdentitySignaturesVerifyAndRejectTampering()
    {
        using DebugIdentity identity = DebugIdentity.Create();
        byte[] payload = "pairing transcript"u8.ToArray();
        byte[] signature = identity.Sign(payload);

        Assert.True(DebugIdentity.Verify(identity.PublicKey, payload, signature));
        payload[0] ^= 0x01;
        Assert.False(DebugIdentity.Verify(identity.PublicKey, payload, signature));
    }

    [Fact]
    public void RemoteDebuggerAndDebugTargetExchangeEncryptedFrames()
    {
        using EphemeralKey debuggerKey = new();
        using EphemeralKey debugTargetKey = new();
        byte[] debuggerSecret = debuggerKey.DeriveSecret(debugTargetKey.PublicKey);
        byte[] debugTargetSecret = debugTargetKey.DeriveSecret(debuggerKey.PublicKey);
        byte[] pairingSecret = Enumerable.Repeat((byte)0x42, 32).ToArray();
        byte[] transcript = SHA256.HashData("transcript"u8);
        using SecureSession debugger = SecureSession.Create(DebugPeerRole.RemoteDebugger, debuggerSecret, pairingSecret, transcript);
        using SecureSession debugTarget = SecureSession.Create(DebugPeerRole.DebugTarget, debugTargetSecret, pairingSecret, transcript);

        byte[] command = debugger.Encrypt("start"u8);
        byte[] response = debugTarget.Encrypt("ready"u8);

        Assert.Equal("start", Encoding.UTF8.GetString(debugTarget.Decrypt(command)));
        Assert.Equal("ready", Encoding.UTF8.GetString(debugger.Decrypt(response)));
        Assert.Throws<CryptographicException>(() => debugTarget.Decrypt(command));
    }

    [Fact]
    public void TamperedFrameIsRejectedWithoutAdvancingSequence()
    {
        using EphemeralKey debuggerKey = new();
        using EphemeralKey debugTargetKey = new();
        byte[] pairingSecret = RandomNumberGenerator.GetBytes(32);
        byte[] transcript = SHA256.HashData("transcript"u8);
        using SecureSession debugger = SecureSession.Create(DebugPeerRole.RemoteDebugger, debuggerKey.DeriveSecret(debugTargetKey.PublicKey), pairingSecret, transcript);
        using SecureSession debugTarget = SecureSession.Create(DebugPeerRole.DebugTarget, debugTargetKey.DeriveSecret(debuggerKey.PublicKey), pairingSecret, transcript);
        byte[] original = debugger.Encrypt("payload"u8);
        byte[] tampered = original.ToArray();
        tampered[^1] ^= 0x80;

        Assert.Throws<AuthenticationTagMismatchException>(() => debugTarget.Decrypt(tampered));
        Assert.Equal("payload", Encoding.UTF8.GetString(debugTarget.Decrypt(original)));
    }

    [Fact]
    public void SecureChannelMatchesNodeProtocolVector()
    {
        byte[] sharedSecret = Enumerable.Range(0, 32).Select(p => (byte)p).ToArray();
        byte[] pairingSecret = Enumerable.Range(32, 32).Select(p => (byte)p).ToArray();
        byte[] transcript = SHA256.HashData("onegate-vector"u8);
        using SecureSession debugger = SecureSession.Create(
            DebugPeerRole.RemoteDebugger,
            sharedSecret,
            pairingSecret,
            transcript);

        Assert.Equal(
            "0000000000000001fbc9ed9a2bbcd21c1d8c728014d471a6efd46ae636ba398492b72ddfd8",
            Convert.ToHexStringLower(debugger.Encrypt("hello OneGate"u8)));
    }
}
