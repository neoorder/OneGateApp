using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace NeoOrder.OneGate.DebugProtocol;

public enum DebugPeerRole
{
    RemoteDebugger,
    DebugTarget
}

public sealed class EphemeralKey : IDisposable
{
    readonly ECDiffieHellman key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

    public byte[] PublicKey => key.ExportSubjectPublicKeyInfo();

    public byte[] DeriveSecret(ReadOnlySpan<byte> peerPublicKey)
    {
        using ECDiffieHellman peer = ECDiffieHellman.Create();
        peer.ImportSubjectPublicKeyInfo(peerPublicKey, out int bytesRead);
        if (bytesRead != peerPublicKey.Length)
            throw new CryptographicException("Unexpected trailing ephemeral-key bytes.");
        return key.DeriveRawSecretAgreement(peer.PublicKey);
    }

    public void Dispose() => key.Dispose();
}

public sealed class SecureSession : IDisposable
{
    const int KeyLength = 32;
    const int NoncePrefixLength = 4;
    const int TagLength = 16;
    static readonly byte[] ProtocolInfo = "OneGate.RemoteDebug.v1"u8.ToArray();

    readonly byte[] sendKey;
    readonly byte[] receiveKey;
    readonly byte[] sendNoncePrefix;
    readonly byte[] receiveNoncePrefix;
    ulong nextSendSequence = 1;
    ulong nextReceiveSequence = 1;

    SecureSession(byte[] sendKey, byte[] receiveKey, byte[] sendNoncePrefix, byte[] receiveNoncePrefix)
    {
        this.sendKey = sendKey;
        this.receiveKey = receiveKey;
        this.sendNoncePrefix = sendNoncePrefix;
        this.receiveNoncePrefix = receiveNoncePrefix;
    }

    public static SecureSession Create(DebugPeerRole role, ReadOnlySpan<byte> sharedSecret, ReadOnlySpan<byte> pairingSecret, ReadOnlySpan<byte> transcriptHash)
    {
        if (pairingSecret.Length != 32)
            throw new ArgumentException("Pairing secret must be 32 bytes.", nameof(pairingSecret));
        byte[] salt = SHA256.HashData(Concat(pairingSecret, transcriptHash));
        byte[] keyMaterial = HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret.ToArray(), 2 * KeyLength + 2 * NoncePrefixLength, salt, ProtocolInfo);
        byte[] debuggerKey = keyMaterial[..KeyLength];
        byte[] debugTargetKey = keyMaterial[KeyLength..(2 * KeyLength)];
        byte[] debuggerNonce = keyMaterial[(2 * KeyLength)..(2 * KeyLength + NoncePrefixLength)];
        byte[] debugTargetNonce = keyMaterial[(2 * KeyLength + NoncePrefixLength)..];
        CryptographicOperations.ZeroMemory(keyMaterial);
        return role == DebugPeerRole.RemoteDebugger
            ? new(debuggerKey, debugTargetKey, debuggerNonce, debugTargetNonce)
            : new(debugTargetKey, debuggerKey, debugTargetNonce, debuggerNonce);
    }

    public byte[] Encrypt(ReadOnlySpan<byte> plaintext)
    {
        ulong sequence = nextSendSequence++;
        byte[] result = new byte[8 + plaintext.Length + TagLength];
        BinaryPrimitives.WriteUInt64BigEndian(result, sequence);
        Span<byte> ciphertext = result.AsSpan(8, plaintext.Length);
        Span<byte> tag = result.AsSpan(8 + plaintext.Length, TagLength);
        Span<byte> nonce = stackalloc byte[12];
        CreateNonce(sendNoncePrefix, sequence, nonce);
        using AesGcm aes = new(sendKey, TagLength);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, result.AsSpan(0, 8));
        return result;
    }

    public byte[] Decrypt(ReadOnlySpan<byte> frame)
    {
        if (frame.Length < 8 + TagLength)
            throw new CryptographicException("Encrypted frame is too short.");
        ulong sequence = BinaryPrimitives.ReadUInt64BigEndian(frame);
        if (sequence != nextReceiveSequence)
            throw new CryptographicException("Encrypted frame sequence is invalid.");
        int plaintextLength = frame.Length - 8 - TagLength;
        byte[] plaintext = new byte[plaintextLength];
        Span<byte> nonce = stackalloc byte[12];
        CreateNonce(receiveNoncePrefix, sequence, nonce);
        using AesGcm aes = new(receiveKey, TagLength);
        aes.Decrypt(nonce, frame.Slice(8, plaintextLength), frame[^TagLength..], plaintext, frame[..8]);
        nextReceiveSequence++;
        return plaintext;
    }

    static void CreateNonce(ReadOnlySpan<byte> prefix, ulong sequence, Span<byte> nonce)
    {
        prefix.CopyTo(nonce);
        BinaryPrimitives.WriteUInt64BigEndian(nonce[NoncePrefixLength..], sequence);
    }

    static byte[] Concat(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second)
    {
        byte[] result = new byte[first.Length + second.Length];
        first.CopyTo(result);
        second.CopyTo(result.AsSpan(first.Length));
        return result;
    }

    public void Dispose()
    {
        CryptographicOperations.ZeroMemory(sendKey);
        CryptographicOperations.ZeroMemory(receiveKey);
        CryptographicOperations.ZeroMemory(sendNoncePrefix);
        CryptographicOperations.ZeroMemory(receiveNoncePrefix);
    }
}
