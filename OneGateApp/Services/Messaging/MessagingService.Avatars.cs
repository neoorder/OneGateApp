using Meshline.Models.Protocol;
using Meshline.Models;
using Meshline.Models.Client;
using Microsoft.EntityFrameworkCore;
using NeoOrder.OneGate.Data;
using NeoOrder.OneGate.Services.Files;

namespace NeoOrder.OneGate.Services.Messaging;

public sealed partial class MessagingService
{
    readonly SemaphoreSlim avatarStorageLock = new(1, 1);
    readonly Dictionary<string, long> pendingAvatarRevisions = new(StringComparer.Ordinal);
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
        await StorePendingChatAvatarAsync(localAccountId, new ProfileUpdate { Avatar = reference });
        operation.Token.ThrowIfCancellationRequested();
        return reference;
    }

    string PendingAvatarKey(string accountId) => $"chat/avatar-pending/{registry.Context.Registry}/{accountId}";

    public async Task<ProfileUpdate?> GetPendingChatAvatarAsync(string localAccountId, CancellationToken cancellationToken)
    {
        ValidatePendingAccount(localAccountId);
        using var scope = scopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        string? value = await database.Settings.GetAsync(PendingAvatarKey(localAccountId));
        cancellationToken.ThrowIfCancellationRequested();
        ValidatePendingAccount(localAccountId);
        if (value is null) return null;
        if (value == "delete") return new() { Avatar = FieldUpdate<ContentReference>.Delete };
        var reference = ProtocolModel.FromJson<ContentReference>(value);
        if (reference is null || reference.Validate() is not null) throw new InvalidDataException("Invalid pending avatar.");
        return new() { Avatar = reference };
    }

    static string PendingAvatarValue(ProfileUpdate update) => update.Avatar.IsDeleted ? "delete" : update.Avatar.Value!.ToJson();

    async Task StorePendingChatAvatarAsync(string accountId, ProfileUpdate update)
    {
        if (!update.Avatar.IsSpecified && !update.Avatar.IsDeleted) return;
        await avatarStorageLock.WaitAsync();
        try
        {
            using var scope = scopeFactory.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            // Only a newer confirmed profile can complete this edit; an older callback may still be queued.
            long revision = client?.Profile is { } profile && profile.Account == accountId ? profile.UpdatedAt : -1;
            await database.Settings.PutAsync(PendingAvatarKey(accountId), PendingAvatarValue(update));
            pendingAvatarRevisions[accountId] = revision;
        }
        finally { avatarStorageLock.Release(); }
    }

    async Task CompletePendingChatAvatarAsync(string accountId, ProfileUpdate update, long? confirmedRevision = null)
    {
        if (!update.Avatar.IsSpecified && !update.Avatar.IsDeleted) return;
        await avatarStorageLock.WaitAsync();
        try
        {
            if (confirmedRevision is { } confirmed && pendingAvatarRevisions.TryGetValue(accountId, out var revision)
                && confirmed <= revision) return;
            using var scope = scopeFactory.CreateScope();
            var database = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            string key = PendingAvatarKey(accountId), value = PendingAvatarValue(update);
            if (await database.Settings.Where(p => p.Key == key && p.Value == value).ExecuteDeleteAsync() > 0)
                pendingAvatarRevisions.Remove(accountId);
        }
        finally { avatarStorageLock.Release(); }
    }
}