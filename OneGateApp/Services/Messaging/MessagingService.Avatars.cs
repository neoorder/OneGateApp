using Meshline.Models.Protocol;
using NeoOrder.OneGate.Services.Files;

namespace NeoOrder.OneGate.Services.Messaging;

public sealed partial class MessagingService
{
    public async Task<ContentReference> UploadChatAvatarAsync(string localAccountId, Guid uploadId, string fileName, byte[] jpeg, CancellationToken cancellationToken = default)
    {
        if (jpeg.Length is <= 0 or > ChatAvatarImages.UploadLimit) throw new ArgumentException("Invalid avatar size.", nameof(jpeg));
        var current = client;
        if (!IsEnabled || current is null || AccountId != localAccountId) throw new InvalidOperationException("Chat account changed.");
        using var operation = CreateOperation(cancellationToken);
        var certificate = current.Device ?? throw new InvalidOperationException("Chat device is unavailable.");
        var identity = new FileUploadIdentity(certificate.ToJson(), async (input, token) =>
        {
            await lifecycleLock.WaitAsync(token);
            try
            {
                if (!ReferenceEquals(current, client) || !ReferenceEquals(certificate, current.Device) || AccountId != localAccountId || !IsEnabled)
                    throw new OperationCanceledException(token);
                EnsureCurrentAccount(sessionWallet!, sessionAccount!);
                return await current.DeviceManager.SignAsync(input, token);
            }
            finally { lifecycleLock.Release(); }
        });
        var reference = await fileUploads.UploadAsync(uploadId, "chat-avatar", ChatAvatarImages.GetUploadFileName(fileName), "image/jpeg", jpeg, identity, operation.Token);
        operation.Token.ThrowIfCancellationRequested();
        if (!ReferenceEquals(current, client) || AccountId != localAccountId || !IsEnabled) throw new OperationCanceledException(operation.Token);
        EnsureCurrentAccount(sessionWallet!, sessionAccount!);
        return reference;
    }
}