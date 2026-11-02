using NeoOrder.OneGate.Services.Notifications;

namespace NeoOrder.OneGate.Services.Messaging;

public sealed partial class MessagingService
{
    internal async Task<PushDeviceIdentity?> GetPushIdentityAsync(bool unregister = false, CancellationToken cancellationToken = default)
    {
        await lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (disposed || client?.Device is not { } certificate) return null;
            if (!unregister && (!IsEnabled || !ReferenceEquals(sessionWallet, walletProvider.GetWallet())
                || !ReferenceEquals(sessionAccount, sessionWallet?.GetDefaultAccount()))) return null;
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (certificate.NotBefore > now || certificate.ExpiresAt <= now) return null;
            var current = client;
            return new(certificate.GetDeviceId(registry.Context), certificate.ToJson(), async (input, token) =>
            {
                await lifecycleLock.WaitAsync(token);
                try
                {
                    // Never let a queued update sign with another account or a replacement device.
                    if (!ReferenceEquals(current, client) || !ReferenceEquals(certificate, current.Device)
                        || !unregister && (!IsEnabled || !ReferenceEquals(sessionWallet, walletProvider.GetWallet())
                            || !ReferenceEquals(sessionAccount, sessionWallet?.GetDefaultAccount())))
                        throw new OperationCanceledException(token);
                    return await current.DeviceManager.SignAsync(input, token);
                }
                finally { lifecycleLock.Release(); }
            });
        }
        finally { lifecycleLock.Release(); }
    }
}
