#if ANDROID

using Android.App;
using Android.Content;
using Android.Hardware.Biometrics;
using Android.OS;
using Android.Runtime;
using Android.Security.Keystore;
using Java.Lang;
using Java.Security;
using Javax.Crypto;
using Javax.Crypto.Spec;
using NeoOrder.OneGate.Properties;
using System.Text;

namespace NeoOrder.OneGate.Services;

partial class DataProtectionService
{
    const string KeyAlias = "onegate_secret_key";

    sealed class BiometricCallback(TaskCompletionSource<bool> tcs) : BiometricPrompt.AuthenticationCallback
    {
        public override void OnAuthenticationSucceeded(BiometricPrompt.AuthenticationResult? result)
        {
            tcs.TrySetResult(true);
        }

        public override void OnAuthenticationError([GeneratedEnum] BiometricErrorCode errorCode, ICharSequence? errString)
        {
            if (errorCode == BiometricErrorCode.Canceled || errorCode == BiometricErrorCode.UserCanceled)
                tcs.TrySetResult(false);
            else
                tcs.TrySetException(new System.Exception($"Biometric error {errorCode}: {errString}"));
        }

        public override void OnAuthenticationFailed()
        {
        }
    }

    sealed class NegativeClickListener(Action action) : Java.Lang.Object, IDialogInterfaceOnClickListener
    {
        public void OnClick(IDialogInterface? dialog, int which) => action();
    }

    public static partial Task<bool> CheckAvailabilityAsync()
    {
        Activity? activity = Platform.CurrentActivity;
        if (activity is null) return Task.FromResult(false);
        BiometricManager? manager = (BiometricManager?)activity.GetSystemService(Context.BiometricService);
        if (manager is null) return Task.FromResult(false);
        BiometricCode result = manager.CanAuthenticate((int)BiometricManagerAuthenticators.BiometricStrong);
        if (result != BiometricCode.Success) return Task.FromResult(false);
        try
        {
            GetOrCreateSecretKey().Dispose();
            return Task.FromResult(true);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    public static partial async Task<bool> AuthenticateAsync(string? title, string? message)
    {
        return await AuthenticateWithCipherAsync(title: title, message: message);
    }

    public static partial async Task<byte[]> ProtectAsync(string plainText)
    {
        using var secretKey = GetOrCreateSecretKey();
        using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        cipher.Init(CipherMode.EncryptMode, secretKey);
        if (!await AuthenticateWithCipherAsync(cipher: cipher))
            throw new System.OperationCanceledException();
        byte[] iv = cipher.GetIV()!;
        byte[] cipherText = cipher.DoFinal(Encoding.UTF8.GetBytes(plainText))!;
        byte[] result = new byte[iv.Length + cipherText.Length];
        Buffer.BlockCopy(iv, 0, result, 0, iv.Length);
        Buffer.BlockCopy(cipherText, 0, result, iv.Length, cipherText.Length);
        return result;
    }

    public static partial async Task<string> UnprotectAsync(byte[] protectedData, string? title, string? message)
    {
        using var secretKey = GetOrCreateSecretKey();
        using var cipher = Cipher.GetInstance("AES/GCM/NoPadding")!;
        byte[] iv = protectedData[..12];
        byte[] cipherText = protectedData[12..];
        var spec = new GCMParameterSpec(128, iv);
        cipher.Init(CipherMode.DecryptMode, secretKey, spec);
        if (!await AuthenticateWithCipherAsync(cipher, title, message))
            throw new System.OperationCanceledException();
        byte[] plainBytes = cipher.DoFinal(cipherText)!;
        return Encoding.UTF8.GetString(plainBytes);
    }

    static ISecretKey GetOrCreateSecretKey()
    {
        var keyStore = KeyStore.GetInstance("AndroidKeyStore")!;
        keyStore.Load(null);
        if (!keyStore.ContainsAlias(KeyAlias))
        {
            var keyGenerator = KeyGenerator.GetInstance(KeyProperties.KeyAlgorithmAes, "AndroidKeyStore")!;
            var spec = new KeyGenParameterSpec.Builder(KeyAlias, KeyStorePurpose.Encrypt | KeyStorePurpose.Decrypt)
                .SetBlockModes(KeyProperties.BlockModeGcm)
                .SetEncryptionPaddings(KeyProperties.EncryptionPaddingNone)
                .SetKeySize(256)
                .SetUserAuthenticationRequired(true)
                .SetUserAuthenticationParameters(0, (int)KeyPropertiesAuthType.BiometricStrong)
                .SetIsStrongBoxBacked(true)
                .Build();
            keyGenerator.Init(spec);
            return keyGenerator.GenerateKey()!;
        }
        var key = keyStore.GetKey(KeyAlias, null)!;
        return key.JavaCast<ISecretKey>();
    }

    static Task<bool> AuthenticateWithCipherAsync(Cipher? cipher = null, string? title = null, string? message = null)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Activity activity = Platform.CurrentActivity!;
        var executor = activity.MainExecutor!;
        var cancelSignal = new CancellationSignal();
        var callback = new BiometricCallback(tcs);
        var builder = new BiometricPrompt.Builder(activity)
            .SetTitle(title ?? Strings.BiometricAuthentication)
            .SetSubtitle(message ?? Strings.VerifyBiometricText);
        builder = builder.SetNegativeButton(Strings.Cancel, executor, new NegativeClickListener(() => tcs.TrySetCanceled()));
        var prompt = builder.Build();
        if (cipher is null)
        {
            prompt.Authenticate(cancelSignal, executor, callback);
        }
        else
        {
            var crypto = new BiometricPrompt.CryptoObject(cipher);
            prompt.Authenticate(crypto, cancelSignal, executor, callback);
        }
        return tcs.Task;
    }
}
#endif
