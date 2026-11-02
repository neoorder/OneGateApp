using Meshline.Models.Protocol;
using Neo.Wallets;
using NeoOrder.OneGate.Properties;
using System.Text.Json;

namespace NeoOrder.OneGate.Services.Messaging;

public sealed partial class MessagingService
{
    /// <summary>Reads invitation fields locally. The SDK verifies signature and authorization when sending.</summary>
    public ContactInvite ParseChatInvitation(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 16 * 1024)
            throw new FormatException(Strings.ChatInvalidInvitation);
        ContactInvite invite;
        try
        {
            invite = ProtocolModel.FromJson<TypedProtocolModel>(value) as ContactInvite
                ?? throw new FormatException(Strings.ChatInvalidInvitation);
        }
        catch (JsonException ex) { throw new FormatException(Strings.ChatInvalidInvitation, ex); }
        if (string.IsNullOrWhiteSpace(invite.Inviter) || string.IsNullOrWhiteSpace(invite.SignerDeviceId)
            || invite.ExpiresAt > DateTimeOffset.MaxValue.ToUnixTimeSeconds())
            throw new FormatException(Strings.ChatInvalidInvitation);
        if (invite.ExpiresAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            throw new FormatException(Strings.ChatReceivedInvitationExpired);
        if (invite.Validate(registry.Context) is not null)
            throw new FormatException(Strings.ChatInvalidInvitation);
        string address = invite.Inviter[(invite.Inviter.LastIndexOf(':') + 1)..];
        if (GetContactAccountId(address) != invite.Inviter)
            throw new FormatException(Strings.ChatInvitationNetworkMismatch);
        return invite;
    }

    /// <summary>Signs an invitation locally with the authorized chat device; does not publish or send it.</summary>
    public async Task<ChatInvitation> CreateChatInvitationAsync(string localAccountId, CancellationToken cancellationToken = default)
    {
        await lifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (!IsEnabled) throw new InvalidOperationException(Strings.ChatEnableFirst);
            if (AccountId != localAccountId) throw new InvalidOperationException(Strings.ChatInvitationAccountChanged);
            Wallet wallet = sessionWallet!;
            WalletAccount account = sessionAccount!;
            using var operation = CreateOperation(cancellationToken);
            void OnWalletChanged(object? sender, Wallet? current)
            {
                if (!ReferenceEquals(current, wallet)) operation.Cancel();
            }
            walletProvider.WalletChanged += OnWalletChanged;
            try
            {
                EnsureCurrentAccount(wallet, account);
                long expiry = Math.Min(DateTimeOffset.UtcNow.AddHours(24).ToUnixTimeSeconds(), client!.Device!.ExpiresAt);
                var invite = await client.MessageManager.CreateInviteAsync(DateTimeOffset.FromUnixTimeSeconds(expiry), operation.Token);
                operation.Token.ThrowIfCancellationRequested();
                EnsureCurrentAccount(wallet, account);
                var result = new ChatInvitation(localAccountId, account.Address, invite.SignerDeviceId,
                    invite.ToJson(), DateTimeOffset.FromUnixTimeSeconds(invite.ExpiresAt));
                if (!IsCurrentChatInvitation(result)) throw new InvalidOperationException(Strings.ChatInvitationAccountChanged);
                return result;
            }
            finally { walletProvider.WalletChanged -= OnWalletChanged; }
        }
        finally { lifecycleLock.Release(); }
    }

    /// <summary>Checks only the current local account/device and invitation expiry, without querying relays.</summary>
    public bool IsCurrentChatInvitation(ChatInvitation invitation) => IsEnabled
        && invitation.ExpiresAt > DateTimeOffset.UtcNow && invitation.LocalAccountId == AccountId
        && ReferenceEquals(sessionWallet, walletProvider.GetWallet())
        && ReferenceEquals(sessionAccount, sessionWallet?.GetDefaultAccount())
        && invitation.ExpiresAt.ToUnixTimeSeconds() <= client!.Device!.ExpiresAt
        && client.Device.GetDeviceId(registry.Context) == invitation.SignerDeviceId;
}
