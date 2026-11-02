using Meshline.Interactions;
using System.Security.Cryptography;
using System.Text;

namespace NeoOrder.OneGate.Services.Messaging;

/// <summary>Protects Meshline's stored secrets with a key held in the platform's secure storage.</summary>
sealed class MeshlineSecretProtector : ISecretProtector
{
    const string StorageKey = "onegate.meshline.secret-key";
    const int KeyLength = 32;
    const int NonceLength = 12;
    const int TagLength = 16;
    const int HeaderLength = NonceLength + TagLength;
    static readonly SemaphoreSlim KeyLock = new(1, 1);
    static readonly UTF8Encoding PurposeEncoding = new(false, true);

    public async Task<byte[]> ProtectAsync(ReadOnlyMemory<byte> plaintext, string purpose, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        cancellationToken.ThrowIfCancellationRequested();
        byte[] associatedData = PurposeEncoding.GetBytes(purpose);
        byte[] input = plaintext.ToArray();
        byte[]? key = null;
        try
        {
            key = await GetKeyAsync(create: true, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            // Persist nonce | authentication tag | ciphertext; bind the SDK's purpose as authenticated data.
            byte[] result = new byte[HeaderLength + input.Length];
            RandomNumberGenerator.Fill(result.AsSpan(0, NonceLength));
            using var aes = new AesGcm(key, TagLength);
            aes.Encrypt(result.AsSpan(0, NonceLength), input, result.AsSpan(HeaderLength),
                result.AsSpan(NonceLength, TagLength), associatedData);
            return result;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
            if (key is not null) CryptographicOperations.ZeroMemory(key);
        }
    }

    public async Task<byte[]> UnprotectAsync(ReadOnlyMemory<byte> protectedData, string purpose, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        cancellationToken.ThrowIfCancellationRequested();
        if (protectedData.Length < HeaderLength)
            throw new CryptographicException("The protected Meshline secret is incomplete.");
        byte[] associatedData = PurposeEncoding.GetBytes(purpose);
        byte[] input = protectedData.ToArray();
        byte[] plaintext = new byte[input.Length - HeaderLength];
        byte[]? key = null;
        try
        {
            key = await GetKeyAsync(create: false, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            using var aes = new AesGcm(key, TagLength);
            aes.Decrypt(input.AsSpan(0, NonceLength), input.AsSpan(HeaderLength),
                input.AsSpan(NonceLength, TagLength), plaintext, associatedData);
            return plaintext;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw;
        }
        finally
        {
            if (key is not null) CryptographicOperations.ZeroMemory(key);
        }
    }

    static async Task<byte[]> GetKeyAsync(bool create, CancellationToken cancellationToken)
    {
        // Multiple clients may first use the protector concurrently; persist only one shared key.
        await KeyLock.WaitAsync(cancellationToken);
        try
        {
            string? storedKey = await SecureStorage.Default.GetAsync(StorageKey);
            cancellationToken.ThrowIfCancellationRequested();
            if (storedKey is null && !create)
                throw new CryptographicException("The Meshline protection key is missing from secure storage.");

            byte[] key = storedKey is null ? RandomNumberGenerator.GetBytes(KeyLength) : Convert.FromBase64String(storedKey);
            try
            {
                if (key.Length != KeyLength)
                    throw new CryptographicException("The Meshline protection key has an invalid length.");
                if (storedKey is null)
                    await SecureStorage.Default.SetAsync(StorageKey, Convert.ToBase64String(key));
                cancellationToken.ThrowIfCancellationRequested();
                return key;
            }
            catch
            {
                CryptographicOperations.ZeroMemory(key);
                throw;
            }
        }
        finally
        {
            KeyLock.Release();
        }
    }
}
