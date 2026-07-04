#if WINDOWS

using NeoOrder.OneGate.Properties;
using Windows.Security.Credentials.UI;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.DataProtection;
using Windows.Storage.Streams;

namespace NeoOrder.OneGate.Services;

partial class DataProtectionService
{
    public static partial async Task<bool> CheckAvailabilityAsync()
    {
        var result = await UserConsentVerifier.CheckAvailabilityAsync();
        return result == UserConsentVerifierAvailability.Available;
    }

    public static partial async Task<bool> AuthenticateAsync(string? title, string? message)
    {
        var result = await UserConsentVerifier.RequestVerificationAsync(message ?? Strings.VerifyBiometricText);
        return result switch
        {
            UserConsentVerificationResult.Verified => true,
            UserConsentVerificationResult.Canceled => false,
            UserConsentVerificationResult.DeviceNotPresent or UserConsentVerificationResult.NotConfiguredForUser or UserConsentVerificationResult.DisabledByPolicy => throw new NotSupportedException(),
            UserConsentVerificationResult.DeviceBusy or UserConsentVerificationResult.RetriesExhausted => throw new InvalidOperationException(),
            _ => throw new Exception(),
        };
    }

    public static partial async Task<byte[]> ProtectAsync(string plainText)
    {
        if (!await AuthenticateAsync())
            throw new OperationCanceledException();
        var provider = new DataProtectionProvider("LOCAL=user");
        IBuffer buffer = CryptographicBuffer.ConvertStringToBinary(plainText, BinaryStringEncoding.Utf8);
        buffer = await provider.ProtectAsync(buffer);
        CryptographicBuffer.CopyToByteArray(buffer, out byte[] enctypted);
        return enctypted;
    }

    public static partial async Task<string> UnprotectAsync(byte[] protectedData, string? title, string? message)
    {
        if (!await AuthenticateAsync(title, message))
            throw new OperationCanceledException();
        var provider = new DataProtectionProvider("LOCAL=user");
        IBuffer buffer = CryptographicBuffer.CreateFromByteArray(protectedData);
        buffer = await provider.UnprotectAsync(buffer);
        return CryptographicBuffer.ConvertBinaryToString(BinaryStringEncoding.Utf8, buffer);
    }
}
#endif
