#if IOS || MACCATALYST

using Foundation;
using LocalAuthentication;
using NeoOrder.OneGate.Properties;
using Security;

namespace NeoOrder.OneGate.Services;

partial class DataProtectionService
{
    const string Service = "OneGate";
    const string Account = "main_wallet";

    public static partial Task<bool> CheckAvailabilityAsync()
    {
        var context = new LAContext();
        bool available = context.CanEvaluatePolicy(LAPolicy.DeviceOwnerAuthenticationWithBiometrics, out NSError? _);
        return Task.FromResult(available);
    }

    public static partial async Task<bool> AuthenticateAsync(string? title, string? message)
    {
        var context = new LAContext();
        var (result, _) = await context.EvaluatePolicyAsync(LAPolicy.DeviceOwnerAuthenticationWithBiometrics, message ?? Strings.VerifyBiometricText);
        return result;
    }

    public static partial async Task<byte[]> ProtectAsync(string plainText)
    {
        if (!await AuthenticateAsync())
            throw new OperationCanceledException();
        var deleteQuery = new SecRecord(SecKind.GenericPassword)
        {
            Service = Service,
            Account = Account
        };
        SecKeyChain.Remove(deleteQuery);
        var record = new SecRecord(SecKind.GenericPassword)
        {
            Service = Service,
            Account = Account,
            Label = "OneGate Wallet Password",
            ValueData = NSData.FromString(plainText),
            AccessControl = new SecAccessControl(SecAccessible.WhenUnlockedThisDeviceOnly, SecAccessControlCreateFlags.BiometryAny)
        };
        var status = SecKeyChain.Add(record);
        return status switch
        {
            SecStatusCode.Success => [],
            SecStatusCode.UserCanceled => throw new OperationCanceledException(),
            _ => throw new Exception($"Keychain add failed: {status}"),
        };
    }

    public static partial async Task<string> UnprotectAsync(byte[] protectedData, string? title, string? message)
    {
        var query = new SecRecord(SecKind.GenericPassword)
        {
            Service = Service,
            Account = Account,
            AuthenticationContext = new LAContext
            {
                LocalizedReason = message ?? Strings.VerifyBiometricText
            }
        };
        var result = SecKeyChain.QueryAsData(query, 1) ?? throw new OperationCanceledException();
        return result[0].ToString();
    }
}
#endif
