using System.Security.Cryptography;

namespace NeoOrder.OneGate.DebugProtocol;

public sealed class DebugIdentity : IDisposable
{
    readonly ECDsa key;

    DebugIdentity(ECDsa key)
    {
        this.key = key;
    }

    public byte[] PublicKey => key.ExportSubjectPublicKeyInfo();
    public byte[] PrivateKey => key.ExportPkcs8PrivateKey();
    public string KeyId => Convert.ToHexStringLower(SHA256.HashData(PublicKey))[..16];

    public static byte[] ToUncompressedPublicKey(ReadOnlySpan<byte> subjectPublicKeyInfo)
    {
        using ECDsa identity = ECDsa.Create();
        identity.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out int bytesRead);
        if (bytesRead != subjectPublicKeyInfo.Length)
            throw new CryptographicException("Unexpected trailing identity-key bytes.");
        ECParameters parameters = identity.ExportParameters(false);
        if (parameters.Q.X is not { Length: 32 } x || parameters.Q.Y is not { Length: 32 } y)
            throw new CryptographicException("The identity key is not a P-256 public key.");
        byte[] result = new byte[65];
        result[0] = 0x04;
        x.CopyTo(result, 1);
        y.CopyTo(result, 33);
        return result;
    }

    public static DebugIdentity Create()
        => new(ECDsa.Create(ECCurve.NamedCurves.nistP256));

    public static DebugIdentity Import(ReadOnlySpan<byte> privateKey)
    {
        ECDsa key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(privateKey, out int bytesRead);
        if (bytesRead != privateKey.Length)
        {
            key.Dispose();
            throw new CryptographicException("Unexpected trailing identity bytes.");
        }
        return new(key);
    }

    public byte[] Sign(ReadOnlySpan<byte> payload)
        => key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

    public static bool Verify(ReadOnlySpan<byte> publicKey, ReadOnlySpan<byte> payload, ReadOnlySpan<byte> signature)
    {
        using ECDsa key = ECDsa.Create();
        key.ImportSubjectPublicKeyInfo(publicKey, out int bytesRead);
        return bytesRead == publicKey.Length
            && key.VerifyData(payload, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    public void Dispose() => key.Dispose();
}
