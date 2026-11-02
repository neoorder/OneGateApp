using Meshline.Models.Client;
using Meshline.Models.Protocol;
using Neo.Wallets;
using NeoOrder.OneGate.Properties;
using NeoOrder.OneGate.Services.Notifications;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NeoOrder.OneGate.Services.Messaging;

public sealed partial class MessagingService
{
    public bool SupportsPushGateway =>
#if WINDOWS
        NeoOrder.OneGate.Services.Notifications.WindowsPushNotifications.IsConfigured;
#else
        DeviceInfo.Platform == DevicePlatform.Android || DeviceInfo.Platform == DevicePlatform.iOS
        || DeviceInfo.Platform == DevicePlatform.MacCatalyst;
#endif
    public bool ChatNotificationsEnabled => NotificationPreferences.IsChatEnabled(client?.Device?.GetDeviceId(registry.Context));

    public void SetChatNotificationsEnabled(bool enabled)
    {
        if (!IsEnabled || client?.Device is not { } device
            || !ReferenceEquals(sessionWallet, walletProvider.GetWallet())
            || !ReferenceEquals(sessionAccount, sessionWallet?.GetDefaultAccount()))
            throw new InvalidOperationException("No current authorized chat device.");
        NotificationPreferences.SetChatEnabled(device.GetDeviceId(registry.Context), enabled);
    }

    public bool HasPushGateway => client?.Device?.PushGateway is { Length: > 0 };
    public bool HasPendingPushGatewayUpdate => activationPath is { } path && File.Exists(PushDeclarationPath(path));
    public bool NeedsPushGatewayDeclaration => SupportsPushGateway && IsEnabled && !HasPushGateway && !HasPendingPushGatewayUpdate;
    bool promptingPush;

    public async Task PromptPushGatewayAsync(Page page)
    {
        if (promptingPush || !NotificationPreferences.Enabled || !ChatNotificationsEnabled || !NeedsPushGatewayDeclaration
            || Shell.Current?.CurrentPage != page || !isForeground) return;
        var account = AccountId;
        string device = client!.Device!.GetDeviceId(registry.Context);
        string key = PushPromptKey(device);
        if (Preferences.Default.Get(key, false)) return;
        promptingPush = true;
        Preferences.Default.Set(key, true);
        try
        {
            if (!await page.DisplayAlertAsync(Strings.ChatEnableNotifications, Strings.ChatPushSetupPrompt, Strings.OK, Strings.Cancel)) return;
            if (AccountId != account || !NotificationPreferences.Enabled || !ChatNotificationsEnabled || !NeedsPushGatewayDeclaration
                || Shell.Current?.CurrentPage != page || !isForeground) return;
            await ConfigurePushGatewayAsync(page);
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (AccountId == account && Shell.Current?.CurrentPage == page && isForeground)
                await page.DisplayAlertAsync(Strings.ChatEnableNotifications, Strings.ChatPushSetupFailed, Strings.OK);
        }
        finally { promptingPush = false; }
    }

    public async Task ConfigurePushGatewayAsync(Page page, CancellationToken cancellationToken = default)
    {
        await lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (!IsEnabled || client?.Device is null) throw new InvalidOperationException(Strings.ChatEnableFirst);
            if (!HasPendingPushGatewayUpdate && HasPushGateway) return;
            if (!SupportsPushGateway) throw new NotSupportedException();
            Wallet wallet = sessionWallet!;
            WalletAccount account = sessionAccount!;
            string path = PushDeclarationPath(activationPath!);
            using var operation = CreateOperation(cancellationToken);
            void WalletChanged(object? sender, Wallet? current) { if (!ReferenceEquals(current, wallet)) operation.Cancel(); }
            walletProvider.WalletChanged += WalletChanged;
            try
            {
                if (!await MainThread.InvokeOnMainThreadAsync(() => walletAuthorizationService.RequestAuthorizationAsync(
                    page, Strings.ChatEnableNotifications, Strings.ChatPushSetupPrompt)))
                    throw new OperationCanceledException();
                operation.Token.ThrowIfCancellationRequested();
                EnsureCurrentAccount(wallet, account);
                try
                {
                    // This recreates the client with an account signer and stops ordinary synchronization.
                    await InitializeCoreAsync(operation.Token, forActivation: true);
                    EnsureCurrentAccount(wallet, account);
                    var current = client!;
                    var local = current.Device ?? throw new InvalidOperationException(Strings.ChatEnableFirst);
                    var intent = File.Exists(path)
                        ? JsonSerializer.Deserialize(await File.ReadAllTextAsync(path, operation.Token), PushDeclarationJsonContext.Default.PushDeclarationIntent)
                            ?? throw new InvalidDataException("Invalid push declaration state.")
                        : new PushDeclarationIntent(local.GetDeviceId(registry.Context), pushOptions.CallbackUrl);
                    if (intent.DeviceId != local.GetDeviceId(registry.Context)
                        || intent.Gateway != pushOptions.CallbackUrl)
                        throw new InvalidOperationException("Resolve the pending push declaration before changing the device or gateway.");
                    await SavePushIntentAsync(path, intent, operation.Token);
                    Preferences.Default.Set(PushPromptKey(intent.DeviceId), true);
                    // A crash after renewal leaves the new certificate in SDK storage. Reuse it.
                    if (local.PushGateway != intent.Gateway)
                    {
                        local = await current.DeviceManager.RenewDeviceAsync(new DeviceRenewalOptions
                        { PushGateway = intent.Gateway }, operation.Token);
                    }
                    // Establishment merges a fresh authoritative state, preserves other devices,
                    // and durably reconciles/reuses an interrupted signed publication. Do not
                    // reconstruct or overwrite the SDK's pending request from an app snapshot.
                    await current.EstablishAccountAsync(cancellationToken: operation.Token);
                    if (current.Device is not { } updated || updated.PushGateway != intent.Gateway
                        || current.DeviceState?.Certificates.FirstOrDefault(c => c.GetDeviceId(registry.Context) == intent.DeviceId)?.ToJson()
                            != updated.ToJson())
                        throw new InvalidOperationException("The relay has not accepted the push declaration yet.");
                    EnsureCurrentAccount(wallet, account);
                    File.Delete(path);
                }
                finally
                {
                    // Never retain the account signer in the ordinary messaging session.
                    await CloseCoreAsync();
                    if (isForeground && !operation.IsCancellationRequested) await InitializeCoreAsync(operation.Token);
                }
            }
            finally { walletProvider.WalletChanged -= WalletChanged; }
        }
        finally { lifecycleLock.Release(); NotifyChanged(); }
    }

    string PushPromptKey(string deviceId) => "push.prompt." + Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(deviceId + "\n" + pushOptions.CallbackUrl)));

    static string PushDeclarationPath(string activation) => Path.Combine(Path.GetDirectoryName(activation)!, "push-declaration.json");
    static async Task SavePushIntentAsync(string path, PushDeclarationIntent intent, CancellationToken token)
    {
        await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(intent, PushDeclarationJsonContext.Default.PushDeclarationIntent), token);
        token.ThrowIfCancellationRequested();
        File.Move(path + ".tmp", path, overwrite: true);
    }

    internal async Task<(string Account, string Device, string Network)?> GetPushNavigationIdentityAsync()
    {
        await lifecycleLock.WaitAsync();
        try
        {
            if (!isForeground || !IsEnabled || client?.Device is not { } certificate
                || !ReferenceEquals(sessionWallet, walletProvider.GetWallet())
                || !ReferenceEquals(sessionAccount, sessionWallet?.GetDefaultAccount())) return null;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (certificate.NotBefore > now || certificate.ExpiresAt <= now) return null;
            return (certificate.Account, certificate.GetDeviceId(registry.Context), registry.Context.ToString());
        }
        finally { lifecycleLock.Release(); }
    }
}

internal sealed record PushDeclarationIntent(string DeviceId, string Gateway);
[JsonSerializable(typeof(PushDeclarationIntent))]
internal partial class PushDeclarationJsonContext : JsonSerializerContext;
